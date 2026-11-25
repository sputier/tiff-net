# Changelog

## Unreleased

### Added

- `TiffImage.Blocks` (`TiffBlock`: offset and byte count of each strip or tile, in file order), `ReadBlock`, `ReadBlockAsync`
  and `TryGetBlockMemory`: the stored, still compressed bytes of an image, without decoding them, for every image including
  one whose compression the library does not read (#142).
- `TiffCompression.Unknown` and `TiffPhotometric.Unknown`, `TiffStorage.RawCompressionCode` and `RawPhotometricCode` (the exact code
  stored in the file; for a named code, the numeric value of the enumeration member) and `TiffImage.IsSupported`
  (`UnsupportedReason is null`). The guide page "Images the library cannot decode" lists the members that never throw for such an
  image, and tests guarantee them in strict mode and not (#143).
- `TiffTag.FileOffset`: the position in the file of the first byte of a tag's value as stored (the value field of the entry
  when the value fits in it), for tags read from a file (main directories, SubIFDs, EXIF and GPS); `null` for a tag created in
  memory. It is not part of equality and is never written (#145).

### Changed

- **Behaviour change.** When a file holds a compression or a photometric interpretation that `TiffCompression` or
  `TiffPhotometric` does not name (34713, 32803, CMYK, Lab...), `TiffStorage.Compression` and `TiffStorage.Photometric` used to
  return an unnamed value of the enumeration (the code cast to it); they now return `Unknown`, and the exact code is in
  `RawCompressionCode` or `RawPhotometricCode`. Code that compared against a numeric cast, such as
  `(int)storage.Compression == 34713`, must compare the raw property instead. `Unknown` is refused when writing:
  `TiffWriteOptions.Compression = TiffCompression.Unknown` throws an `ArgumentException` that names the property, and no file is
  created or changed. `TiffStorage` has two new required properties; an application that builds one through its initializer must
  set them (the constructor is internal, so only a `with` expression exists in application code, and it is unaffected) (#143).

## 0.1.0

The first version, written from scratch after an audit of a first implementation.

### Reading

- `TiffFile.Open` from a path, `OpenAsync`, `Open(Stream)` (read on demand, without a copy) and `Read(ReadOnlyMemory<byte>)`.
- `ReadPixels(T[])` and `ReadPixels(Memory<T>)` decode on every core into a buffer of the application; `ReadRegionAsync` and
  `ReadChannelAsync`; progress (`IProgress<double>`) on the asynchronous reads and saves.
- `TiffImage.ReadIndices` (palette indices) and `TiffReadOptions.Constrained` (options for a small device).

- Classic TIFF and BigTIFF, both byte orders, multi-page files, `SubIFDs`, EXIF and GPS sub-directories (read only).
- Grey, RGB, `MinIsWhite`, palette; unsigned samples of 1 to 16, 32 and 64 bits, signed ones of 8 to 64 bits, floating point
  of 16, 32 and 64 bits; extra
  channels (data and alpha).
- Strips and tiles, interleaved and planar; the 8 orientations applied, pixels always delivered top-down.
- Compressions: none, PackBits, LZW, Deflate, JPEG (baseline and progressive, bit-exact with libjpeg-turbo); horizontal and
  floating point predictors.
- `ReadRawPixels`, `ReadPixels` (stored or normalized values), regions, channels, planar order, into a buffer of the
  application, asynchronously and cancellably; decoded on every core.
- Images the library does not handle are listed with their metadata and the reason; their pixels throw
  `NotSupportedException`.
- Bounded resources on untrusted files (`TiffReadOptions`): every allocation a file asks for is counted before it is made;
  148 million fuzzed inputs without a finding.

### Writing

- `TiffFile.Save`, to a path (through a temporary file that replaces the target) or to a stream, and `SaveAsync` to a stream;
  none,
  PackBits, LZW and Deflate with the predictor that suits the samples, strips or tiles, interleaved or planar, BigTIFF when
  needed; encoded on every core, the file identical whatever the number of threads.
- Saving an image opened from a file costs what reading it would: its pixels count in `MaxPixelBytes`, and a file that does
  not hold the pixels it declares is refused before anything is allocated; `Save` can be cancelled.
- `TiffImageWriter` writes images larger than an array, rows in order or tiles in any order, with bounded memory.
- `SaveAsync` also to a path; `TiffImageWriter.SetExtraChannels`; `TiffWriteOptions.SkipUnsupportedPages` to save the pages
  the library can write from a file that has others.
- Metadata: the common tags as properties, any other tag by its code, Astro-TIFF headers kept byte for byte;
  `TiffMetadata.PixelTags` and `CopyFrom`. `SetPixels` removes the tags that described the former values (their ranges);
  MinIsWhite, palette and YCbCr images are read without them.
- Every written combination is read back with the same samples by tifffile, and by Pillow for the types it supports.

### Performance

- Reads and writes use every core; the figures, per scenario and with how to tune them, are in `docs/performance.md`.
- Tuned decoders: planar and FITS-cube reads, normalised and converted reads, 12 and 14-bit samples, big-endian files,
  predictors, LZW, files of many small strips, JPEG (4:2:0 colour conversion with vectors, and without AVX2).
- Large strips (more than 4 MB, 19 MB for LZW) are decoded as a stream in bounded memory, and a file is opened without the
  `RandomAccess` hint, which made a whole-image read of a file outside the system cache 1.3 to 2.2 times slower.

### Platforms

- net10.0 on Windows and Linux, NativeAOT (checked in CI), .NET MAUI on Android (built in CI, run on an emulator by hand);
  little-endian processors.
  Trimmable and AOT-compatible, no dependency.
