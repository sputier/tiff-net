# Architecture — design

Status: **validated on 2026-11-05**. How the library is organised to meet the security, performance and maintainability
requirements of the [review](../review-2026-11-05.md). The public surface is in [api.md](api.md), the format coverage in
[coverage.md](coverage.md).

## Constraints

- net10.0 only; Windows, Linux, .NET MAUI (Android, iOS): no reflection that trimming cannot see, no native code, no
  `unsafe` (Span, `MemoryMarshal`, `BinaryPrimitives` and `System.Numerics.Vector` instead), `IsTrimmable` and
  `IsAotCompatible` with zero warnings, little and big endian *files* on little endian *machines* (a check refuses big endian
  machines at start-up rather than giving wrong pixels).
- No dependency. Deflate through `System.IO.Compression` (zlib-ng in .NET 10); everything else written here.

## Layers

```
 Public API          TiffFile · TiffImage · TiffMetadata · options · exceptions
 ─────────────────────────────────────────────────────────────────────────────
 Pixel pipeline      plan (which strips/tiles, where they land) → parallel decode → assemble (interleave/planar,
                     region crop, orientation) → convert (scale, type)          · same in reverse for writing
 Codecs              None · PackBits · LZW · Deflate · JPEG decoder             (stateless per call, pooled state)
 Predictors          horizontal 8–64 bit · floating point 16–64 bit             (Span, vectorised)
 Structure           header · directory parser · tag model · directory writer   (every value bounded)
 Source              ITiffSource: file (SafeFileHandle + RandomAccess) · memory · ResourceBudget
```

Each layer only calls the one below it. The structure layer never decodes pixels; the codecs never see a directory.

### Source

- `FileSource` (from `Open`): a `SafeFileHandle` and `RandomAccess.Read` at explicit offsets. No shared position, so several
  threads read strips at once without locking.
- `MemorySource` (from `Read`): the bytes of the stream, read once into one array (bounded by `MaxInputBytes`); a
  non-seekable stream is read in chunks into a pooled growing buffer, never past the limit.
- Every read is `ReadExactly(offset, span)` with `offset + length ≤ size` checked first; short reads of a file mean
  truncation and give a `TiffException`.

### Structure

- `DirectoryReader` parses a directory into `TagEntry` values `(code, type, count, inline value or offset)`, checks
  `count × size(type) ≤ size − offset` *before* reading the values, bounds the entries (`MaxTagsPerDirectory`), detects loops
  with a visited set of offsets, and builds an `ImageDescriptor` (geometry, samples, layout, strip/tile tables).
- Strip and tile tables: their length must equal the number of strips/tiles implied by the geometry, which must itself be
  coherent with the size of the file (a strip or tile needs at least one byte of offset table: `count ≤ size / 4`). The tables
  are read into `long[]` of the exact count, nothing bigger. This closes SEC-01 by construction.
- Unknown tags are stored as raw bytes in a dictionary keyed by code: linear time, no field-info merging (SEC-03).
- `DirectoryWriter` sorts tags, aligns values, writes classic or BigTIFF entries.

### Codecs

```csharp
internal interface ITiffDecoder
{
    // Decodes one strip or tile. 'destination' has exactly the decoded size; fewer bytes produced is a TiffException.
    void Decode(ReadOnlySpan<byte> source, Span<byte> destination, DecodeContext context);
}
internal interface ITiffEncoder
{
    // Encodes one strip or tile into a pooled growable buffer; returns the encoded length.
    int Encode(ReadOnlySpan<byte> source, IBufferWriter<byte> destination, EncodeContext context);
}
```

- **Decoders write directly into the destination** (the final array, or a strip-sized pooled buffer when the destination
  needs reordering): one copy at most per byte (PERF-08).
- **Deflate**: `ZLibStream` over a read-only stream wrapping the source memory, decompressing into the destination. Output larger than the destination is a
  `TiffException` (decompression bombs are bounded by the destination size, which is bounded by `MaxPixelBytes`).
- **LZW**: own decoder (back-referencing table, state in locals, 64-bit bit buffer; prototype in
  `research/review-2026-11/performance/bench/Kernels.cs`), both bit orders; encoder with a hash table.
- **PackBits**: span loops, with run detection on writing.
- **JPEG**: own decoder, see below.

### Predictors

- Applied in place on the decoded strip, row by row, with typed spans (`MemoryMarshal.Cast`), no allocation (PERF-04/05).
- Horizontal: prefix sum per row and channel (vectorised where the stride allows); encoding is a vectorised difference.
- Floating point (Technical Note 3): byte-plane un-shuffle into a per-worker pooled row buffer, then the byte-wise prefix sum,
  then the reverse; 16, 32 and 64-bit.
- Byte swapping of big endian files with `BinaryPrimitives.ReverseEndianness` over spans (PERF-07), after the predictor as
  the specification orders it.

### Pixel pipeline

1. **Plan**: from the descriptor and the request (whole image or region, order, type), the list of strips/tiles to decode and,
   for each, where its rows land. A region decodes only the strips/tiles it covers.
2. **Decode in parallel**: `Parallel.ForEach` over the plan with `MaxDegreeOfParallelism`; each worker reads the compressed
   bytes (`RandomAccess` or a slice of memory), decodes, applies predictor and byte order, then copies rows to their place.
   Workers rent their buffers from `ArrayPool` and return them. Cancellation is checked between strips.
3. **Assemble**: interleaved strips decode straight into the result when no reordering is needed; tiles, planes, regions and
   orientation go through a typed copy (row spans, planar-to-interleaved with a vectorised gather where possible).
4. **Convert**: when the requested type is not the stored one, a strip is converted as it lands, so that no full-size array of
   the stored type exists (peak memory ≈ the result, PERF-04).

Writing mirrors it: the image is cut into strips/tiles, encoded in parallel into pooled buffers, then appended to the output **in
order** by one writer, so the file does not depend on the number of threads (PERF-01). At most `2 × MaxDegreeOfParallelism`
encoded strips are in flight, which bounds the memory of a write.

### JPEG decoder

- Scope: ITU-T T.81 baseline and progressive (SOF0, SOF1, SOF2) with Huffman coding, 8-bit precision, 1 to 3 components (4 is
  refused: CMYK), any sampling factors up to 4, restart intervals, `DNL` refused. Technical Note 2 specifics: abbreviated
  table-specification streams in `JPEGTables` merged with each strip/tile's stream; the colour transform from YCbCr when
  `PhotometricInterpretation = 6` (or the Adobe marker says so); `ReferenceBlackWhite` honoured; upsampling of chroma with the
  same filter as libjpeg's default ("fancy" upsampling) so that results match libjpeg-turbo within ±1.
- IDCT: the integer "islow" algorithm of libjpeg (bit-exact reference), vectorised later if measurements ask for it.
- Bounds, by design rather than after the fact (SEC-04, SEC-05):
  - frame dimensions must equal the strip/tile dimensions declared by TIFF;
  - the coefficient buffer of progressive images (`blocks × 64 × 2` bytes) is counted in the budget **before** allocation;
  - the number of scans is limited by `MaxJpegScans` (default 100; a normal progressive image has about 10);
  - Huffman tables are validated (code lengths, total ≤ 256, no over-subscribed tree) and decoding uses a lookup table plus a
    bounded slow path;
  - an `EOBRUN` cannot exceed the number of remaining blocks; a scan that makes no progress is an error;
  - cancellation is checked between scans and MCU rows.
- Oracle: libjpeg-turbo through Pillow/tifffile in the interop tests.

### Resource budget

`ResourceBudget` is created per read from `TiffReadOptions` and passed down. Every allocation whose size comes from the file
asks the budget first: directory tables, tag values, decode destinations, JPEG coefficient buffers. It refuses with a
`TiffException` whose `Limit` property names the limit, so the application can tell "too big for this device" from "damaged".
Allocations that do not depend on the file (pooled codec state) are not counted.

Rules enforced by tests:

- No allocation bigger than `min(size of the file, a limit)` before the data that justifies it has been read.
- No loop whose number of iterations is not bounded by the size of the file or by a limit.
- No `OutOfMemoryException`, `IndexOutOfRangeException`, `OverflowException` or `NullReferenceException` escapes: integer
  arithmetic on file values is `checked` or done in `long` with explicit bounds.

## Testing

| Kind | Content | Oracle |
| --- | --- | --- |
| Unit | each layer: directory parsing, each codec, predictors, assembly, conversions | known vectors, round trips |
| Interop | the tifffile corpus (types × layouts × compressions × predictors × byte orders × BigTIFF, ≈ 2 200 files) generated by `tools/make_fixtures.py`; Siril 1.4.4 files (cropped, with their Astro-TIFF header and ICC profile); Pillow and libjpeg-turbo for JPEG | SHA-256 of the samples computed by tifffile; for writing, tifffile reads our files back. Live tests skipped with `TIFFNET_SKIP_INTEROP=1`, a CI job runs them, the committed expectations run everywhere |
| Hostile | the 20 files of `research/review-2026-11/security/repro` and generated ones (`tools/make_hostile.py`) | each fails with `TiffException` within a time and allocation bound asserted by the test |
| Fuzzing | the review's structure-aware fuzzer moved into `tools/Fuzz` (seed replay); a short run in CI on every change of the structure or codec layers; long campaigns on demand | no exception other than `TiffException`/`NotSupportedException`; no input slower than 2 s; allocations within the budget |
| Benchmarks | BenchmarkDotNet in `benchmarks/`: read/write × sample type × compression × predictor × layout, 24 and 61 MP | the targets below; regressions compared to the previous run |
| Mutation | Stryker.NET on demand on the structure layer and codecs | survival rate tracked |

## Performance targets

From the measurements of the review (8-core laptop, warm cache). They are acceptance criteria for the corresponding stories,
measured by the benchmarks of the repository.

| Scenario | First implementation | Target |
| --- | --- | --- |
| Read u16 24 MP, none | 4.4 GB/s | ≥ 4 GB/s (I/O bound) |
| Read u16 24 MP, Deflate + horizontal predictor | 160 MB/s | ≥ 1 GB/s on 8 cores, ≥ 250 MB/s on 1 |
| Read f32 RGB 24 MP, Deflate + floating point predictor | 143 MB/s, 654 MB allocated | ≥ 1 GB/s on 8 cores; allocations ≤ 1.05 × the pixels |
| Write u16 24 MP, Deflate + predictor | 4.4 s | ≤ 0.5 s on 8 cores |
| Write f32 RGB 24 MP, Deflate + predictor | 8.0 s | ≤ 1 s on 8 cores |
| Peak memory, read f32 61 MP compressed | 2.05 × the pixels | ≤ 1.1 × the pixels |
| Read 24 MP baseline JPEG RGB | (not measured) | within 2× of libjpeg-turbo through Pillow on 1 core |

## Repository layout

```
src/Eleris.TiffNet/              the library (Structure/, Codecs/, Jpeg/, Pipeline/, public types at the root)
tests/Eleris.TiffNet.Tests/      unit, interop, hostile, documentation examples
tests/Fixtures/                  committed fixtures with a README of their provenance; the tifffile corpus is generated
benchmarks/Eleris.TiffNet.Benchmarks/
tools/                           make_fixtures.py, make_hostile.py, Fuzz/, DocGen (from FitsNet), check_docs.py, verify_package.py
docs/                            MkDocs guide, standard coverage, reliability, generated API pages, examples run by the tests
research/                        review artifacts (not built)
```

CI as FitsNet's (ubuntu + windows, docs, package verification, LF check) plus the interop job and a short fuzz job; release
workflow, CHANGELOG and RELEASING copied from FitsNet; `EnablePackageValidation`, SourceLink, deterministic build, actions pinned
by SHA, minimal `permissions:`, Dependabot.

## Decisions

| # | Question | Decision (2026-11-05) |
| --- | --- | --- |
| 1 | Old-style JPEG (compression 6) | **Not supported** (`NotSupportedException`). Deprecated since 1995; supporting it means a second, heuristic JPEG front end, the kind of code that produced many libtiff CVEs. |
| 2 | Default write compression | **Deflate + predictor** (`TiffWriteOptions.Default`). |
| 3 | Mutable or immutable model | **Mutable, like FitsNet**, which stays mutable too. Header and metadata edits are frequent and would become `with` chains or builders; pixel buffers are processed in the application's own arrays, so immutability of the I/O objects buys little. Concurrent *reading* is guaranteed; small value objects (tags, resolution, storage, options) are immutable records. |
| 4 | Low bit depths, palette | **Read**: samples of 1 to 16 bits unpacked (covers masks and 12/14-bit packed camera data) and palettes expanded to RGB 16-bit. **Not** the CCITT fax compressions (about 1 000 lines, a large share of libtiff's CVEs). |
| 5 | Images larger than one array | **In 1.0, in its own lot**: `TiffImageWriter` (rows in order or tiles in any order, BigTIFF). The pipeline is already strip-based; the cost is mostly the tests on files beyond 4 GB, isolated in a dedicated CI job. |
| 6 | TIFF ↔ FITS bridge | **A separate package** referencing both libraries (Astro-TIFF header ↔ `FitsHeader`, row flip, `WCS`/`BAYERPAT` adaptation, interleaved ↔ planar). It needs a text header parser in FitsNet (`FitsHeader.Parse`). |
