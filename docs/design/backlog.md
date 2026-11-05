# Backlog 1.0

Derived from the validated [API](api.md), [coverage](coverage.md) and [architecture](architecture.md), and from the
[review](../review-2026-11-05.md) whose findings each story closes (column "Closes"). Sizes: S ≤ 1 day, M a few days, L more
than a week.

**Definition of done** (every story): automated tests written and passing on ubuntu and windows; public members documented
(the build fails otherwise); no warning (analyzers `latest-recommended`, trimming, AOT); `dotnet-code-reviewer` run on the
change; the coverage, reliability or performance documentation updated when the story changes what they state; benchmarks
re-run when the story touches a hot path.

Lots are ordered: a lot starts when the previous one is done, except where noted. Within a lot, stories can run in parallel
unless a dependency is given.

## Lot 0 — Foundations

| ID | Story | Acceptance criteria | Closes | Size |
| --- | --- | --- | --- | --- |
| F-01 | Solution skeleton | `Eleris.TiffNet.slnx`, `src/Eleris.TiffNet` (net10.0, nullable, `TreatWarningsAsErrors`, `GenerateDocumentationFile`, `AnalysisLevel latest-recommended`, `IsTrimmable`, `IsAotCompatible`, SourceLink, deterministic), `tests/Eleris.TiffNet.Tests` (xUnit), `benchmarks/Eleris.TiffNet.Benchmarks`; `global.json` pins the SDK; a big-endian machine is refused at start-up (test via an internal hook) | MAINT-14, MAINT-16, SEC-15 | S |
| F-02 | Public API tracking | `Microsoft.CodeAnalysis.PublicApiAnalyzers` with `PublicAPI.Shipped.txt` / `Unshipped.txt`; any public change shows in review | MAINT-16 | S |
| F-03 | CI | ubuntu + windows build and test; LF check (`git ls-files --eol`); `permissions:` minimal; actions pinned by SHA; Dependabot (nuget, github-actions); package job runs `tools/verify_package.py` (from FitsNet, adapted): content, metadata, use from a new net10 project | MAINT-03, MAINT-06, MAINT-18, SEC-15 | S |
| F-04 | Release pipeline | `release.yml` (tag = version, test, pack, verify, `nuget` environment, GitHub release), `RELEASING.md`, `CHANGELOG.md`, `EnablePackageValidation`, `SECURITY.md` | MAINT-06, SEC-15 | S |
| F-05 | Documentation skeleton | MkDocs (`mkdocs.yml`, `docs/index.md`), `tools/DocGen` from FitsNet generating API pages (`--check` in CI), `tools/check_docs.py`, examples project compiled and run by the tests; design pages kept under `docs/design/` | MAINT-11 | M |
| F-06 | Interop corpus | `tools/make_fixtures.py` (from `research/review-2026-11/interop`) generates the tifffile corpus (≈ 2 200 files) with SHA-256 of the samples; expectations committed, files generated in CI; `TIFFNET_SKIP_INTEROP=1` skips the live tests; an `interop` CI job | MAINT-05 | M |
| F-07 | Siril and domain fixtures | Siril 1.4.4 files regenerated from the `siril-cli` scripts and cropped (≤ 1 MB each): 8/16/32-bit grey and RGB, Astro-TIFF header, ICC; domain cases (thumbnail in IFD0, grey with 3 samples, associated alpha, MinIsWhite, palette, 12-bit packed); provenance in `tests/Fixtures/README.md` | MAINT-10 | S |
| F-08 | Hostile fixtures and test helpers | the 20 files of `research/…/security/repro` plus `tools/make_hostile.py` (from `tiffcraft.py`, `craft_*.py`); a test helper asserting "fails with `TiffException` within T seconds and A bytes allocated" | SEC-01…07 | S |
| F-09 | Fuzz harness | `tools/Fuzz` from the review's structure-aware fuzzer, seed replay, corpus from F-06/F-07; runnable locally; wired to CI in lot 3 | MAINT-09 | M |

## Lot 1 — Structure and metadata (reading)

| ID | Story | Acceptance criteria | Closes | Size |
| --- | --- | --- | --- | --- |
| S-01 | Sources | `FileSource` (`SafeFileHandle` + `RandomAccess`), `MemorySource`, bounded copy of a non-seekable stream (`MaxInputBytes`), `ReadExactly(offset, span)` with bounds; caller stream failures rethrown as they are (any exception type) | SEC-10, SEC-11 | S |
| S-02 | Options, budget, exceptions | `TiffReadOptions` (all limits of the API, `Default`), `ResourceBudget`, `TiffException` with `Limit`; messages carry the file name only | SEC-05 (part), SEC-09 | S |
| S-03 | Header and directory parser | classic and BigTIFF, `II`/`MM`, all field types, entries bounded, values checked against the file size before reading, loops detected, `Strict` behaviour; unknown tags stored in linear time | SEC-01, SEC-03, SEC-13, MAINT-05 (BE BigTIFF) | M |
| S-04 | Image descriptor | geometry, samples, photometric, layout, strip/tile tables of exactly the implied count and coherent with the file size; required tags; refusal messages for unsupported features (`NotSupportedException`) | SEC-01, SEC-02, DOM-20 | M |
| S-05 | `TiffFile` reading | `Open`, `Read`, `ReadAsync`, `Count`, indexer, `Primary` (NewSubfileType/SubfileType), `SubImages`, `Dispose`; hostile fixtures of the structure kind fail within bounds | DOM-04, DOM-07 | M |
| S-06 | Metadata reading | `TiffMetadata` typed properties (description, software, date, resolution, sample range, ICC, XMP, IPTC…), generic indexer, `TiffTag` typed accessors, EXIF and GPS sub-directories as tag lists, ASCII and UTF-8 text, multi-string tags | DOM-01 | M |
| S-07 | `TiffStorage` | read-only description of the stored layout (compression, predictor, layout, planar, byte order, BigTIFF, orientation, photometric, bits per sample) | — | S |

## Lot 2 — Pixels (reading, uncompressed)

| ID | Story | Acceptance criteria | Closes | Size |
| --- | --- | --- | --- | --- |
| P-01 | Pixel pipeline | plan / decode / assemble for strips, tiles, separate planes; decode straight into the destination when no reordering is needed; pooled buffers, no allocation per strip; destination counted in the budget | PERF-08, PERF-09, PERF-14, SEC-05 | M |
| P-02 | Parallel decoding | `MaxDegreeOfParallelism`; identical results on 1 and n threads; cancellation between strips | PERF-03 | M |
| P-03 | Sample types and byte order | 8 to 64-bit integers, Float16/32/64; big endian swapped with `ReverseEndianness`; NaN/infinity/−0 preserved | PERF-07 | S |
| P-04 | Pixel API | `ReadRawPixels`, `ReadPixels` (Stored / Normalized, conversion rules of the API), `ReadPixels(Span)`, `ReadChannel`, `ReadRegion`, `ReadRawRegion`, interleaved and planar order, `ReadPixelsAsync`; region reads decode only the strips/tiles they cover | DOM-05, DOM-13, DOM-12, PERF-11 | M |
| P-05 | Orientation | values 1 to 8 applied (including transpositions), typed row operations | PERF-15, need 28 of the review | S |
| P-06 | Colour model and extra channels | `ColorModel` from the file, `ExtraChannels` with their kinds, grey with 3+ samples stays grey | DOM-02, DOM-03 | S |
| P-07 | Low depths, MinIsWhite, palette | unpacking of 1–16-bit unsigned samples (both fill orders), `Normalized` by `2^bits − 1`; MinIsWhite inverted; palette expanded to RGB 16-bit | decision 4 | M |

## Lot 3 — Codecs (reading)

| ID | Story | Acceptance criteria | Closes | Size |
| --- | --- | --- | --- | --- |
| C-01 | PackBits decoder | span loops; output bounded by the destination | — | S |
| C-02 | LZW decoder | back-referencing table, state in locals, both bit orders (old-style included); prototype of the review as starting point; benchmarked against it | PERF-10 | M |
| C-03 | Deflate decoder | `ZLibStream` into the destination; overflow of the destination is a `TiffException`; codes 8 and 32946 | PERF-02, SEC-06 | S |
| C-04 | Predictors (decoding) | horizontal 8–64-bit and floating point 16–64-bit, in place, typed spans, vectorised where the stride allows; no allocation per row | PERF-04, PERF-05, PERF-06 | M |
| C-05 | Interop gate | 100 % of the tifffile corpus readable except the features refused by design, samples equal to tifffile's (SHA-256); Siril fixtures read with their metadata | MAINT-05, MAINT-13 | S |
| C-06 | Hostile gate and fuzzing in CI | every hostile fixture fails within its time and allocation bound; short fuzz job in CI on changes of the structure/codec layers; no exception other than `TiffException`/`NotSupportedException` | SEC-01…07, MAINT-09 | S |

## Lot 4 — JPEG decoder (can start after lot 1, in parallel with lots 2–3)

| ID | Story | Acceptance criteria | Closes | Size |
| --- | --- | --- | --- | --- |
| J-01 | Marker parser and Huffman decoding | SOI/APPn/DQT/DHT/SOF0-1/SOS/DRI/RSTn/EOI; Huffman tables validated (lengths, ≤ 256 symbols, no over-subscribed tree), lookup table decoding; anything else refused with `NotSupportedException` | SEC-04 | M |
| J-02 | Baseline decoding | islow integer IDCT bit-exact with libjpeg; 1 or 3 components, sampling factors up to 4; restart intervals | — | M |
| J-03 | Progressive decoding | spectral selection and successive approximation, `EOBRUN` bounded by the remaining blocks; coefficient buffer counted in the budget before allocation; `MaxJpegScans`; no-progress scans refused | SEC-04, SEC-05 | L |
| J-04 | Colour and upsampling | YCbCr → RGB (and `ReferenceBlackWhite`), "fancy" upsampling, results within ±1 of libjpeg-turbo | — | M |
| J-05 | JPEG in TIFF (Technical Note 2) | `JPEGTables` abbreviated streams, strips and tiles, frame size equal to the strip/tile size, YCbCr photometric delivered as RGB; Pillow/tifffile oracle files in the interop corpus | — | M |
| J-06 | JPEG hardening | hostile JPEG fixtures of the review (scan amplification, coefficient bombs) fail within bounds; JPEG-focused fuzzing; benchmark vs libjpeg-turbo (target: within 2× on one core) | SEC-04, SEC-05 | M |

## Lot 5 — Writing

| ID | Story | Acceptance criteria | Closes | Size |
| --- | --- | --- | --- | --- |
| W-01 | Creating images | `TiffImage.Create`, `SetPixels`, explicit colour model (refusal for 3+ channels without it), `SetExtraChannels`, interleaved or planar input; one copy | DOM-02, DOM-03, DOM-06 | S |
| W-02 | Directory writer | classic and BigTIFF (`Auto`/`Never`/`Always`), little endian, sorted tags, aligned values, multi-page chains | — | M |
| W-03 | `TiffFile.Save` | path (temporary file + replace, permissions kept, cleanup never masks the original exception), stream (from the position, non-seekable through a bounded buffer), `SaveAsync`, multi-page | SEC-12 | M |
| W-04 | Metadata writing and round trip | typed and generic tags written; on read → save every non-structural tag is kept except offset-bearing ones (documented list); Siril Astro-TIFF + ICC survive byte for byte | DOM-01 | M |
| W-05 | Encoders | None, PackBits, LZW (hash table), Deflate (`ZLibStream`, `TiffCompressionLevel`) | PERF-02, PERF-12, PERF-16 | M |
| W-06 | Predictors (encoding) | horizontal vectorised, floating point; `Auto` rules (none for 64-bit integers) | PERF-05 | S |
| W-07 | Layouts | strips (~256 KB), tiles (`TileSize`), separate planes | — | S |
| W-08 | Parallel encoding | strips/tiles encoded in parallel, appended in order; output identical on 1 and n threads; at most 2 × degree strips in flight | PERF-01 | M |
| W-09 | `TiffWriteOptions` | defaults (Deflate + predictor), `From(storage)` (JPEG → Deflate), JPEG writing refused | decision 2 | S |
| W-10 | Interop gate (writing) | every combination type × channels × compression × predictor × layout read back identically by tifffile; files readable by Pillow/WIC where they support the type | MAINT-05 | S |

## Lot 6 — Large images

| ID | Story | Acceptance criteria | Closes | Size |
| --- | --- | --- | --- | --- |
| L-01 | `TiffImageWriter` | `WriteRows` (strips, in order), `WriteTile` (tiles, any order, each once), `Complete` (refuses gaps), `Dispose` without `Complete` abandons; BigTIFF decided from the geometry; bounded memory | DOM-08, decision 5 | M |
| L-02 | Beyond 4 GB | a dedicated CI job writes and reads back (by region) a file larger than 4 GB, checks samples by sampling and hashing | decision 5 | S |

## Lot 7 — Performance and hardening

| ID | Story | Acceptance criteria | Closes | Size |
| --- | --- | --- | --- | --- |
| H-01 | Benchmarks at target | the targets of the architecture page met on the reference machine (read/write × type × compression, 24 and 61 MP), results committed | PERF-* | M |
| H-02 | Memory peaks | peak working set ≤ 1.1 × the pixels for whole-image reads, bounded for `TiffImageWriter`; measured by a test or benchmark | PERF-04, PERF-11 | S |
| H-03 | Long fuzzing campaign | ≥ 50 M executions over all layers and JPEG, report committed; findings fixed | SEC-* | M |
| H-04 | Mutation testing | Stryker.NET on structure and codecs; surviving mutants reviewed | MAINT-20 | S |
| H-05 | Platforms | NativeAOT publish of a sample app (Windows, Linux) with zero warnings; a MAUI sample reading and writing on Android (emulator in CI if feasible, otherwise a manual check documented) | — | M |

## Lot 8 — Documentation and release

| ID | Story | Acceptance criteria | Closes | Size |
| --- | --- | --- | --- | --- |
| D-01 | Guide | getting started, files and pages, reading pixels (conversions, normalisation, orientation, planar), metadata and Astro-TIFF, writing, large images, threads; examples compiled and run by the tests | MAINT-11, DOM-18 | M |
| D-02 | Standard coverage and reliability | `docs/standard-coverage.md` (from `coverage.md`, "Supported" = implemented and tested), `docs/reliability.md` (errors, limits for untrusted files, memory, threads, DoS bounds) | MAINT-21 | S |
| D-03 | Performance page | measured throughput and memory per scenario, how to tune (`MaxDegreeOfParallelism`, compression level) | — | S |
| D-04 | Release 0.1.0 | CHANGELOG, package verified, tag, NuGet publication | MAINT-06 | S |

## Outside this repository

| ID | Story | Where | Size |
| --- | --- | --- | --- |
| X-01 | `FitsHeader.Parse(string text)` for headers that are not in 2880-byte blocks (Astro-TIFF lines, LF/CRLF, short lines) | Eleris.FitsNet | S |
| X-02 | Bridge package: Astro-TIFF header ↔ `FitsHeader` (structural keywords ignored), row flip, `WCS`/`BAYERPAT` adaptation, interleaved ↔ planar, ICC kept aside; tests on the Siril fixtures against astropy | new repository | M |
| X-03 | Move FitsNet to net10.0 only, as decided for this library | Eleris.FitsNet | S |
