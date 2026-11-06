# Changelog

## Unreleased

### Added

- `TiffFile.OpenAsync`, `TiffFile.Open(Stream)` (read on demand, without a copy), `TiffFile.Read(ReadOnlyMemory<byte>)`.
- `ReadPixels(T[])` and `ReadPixels(Memory<T>)` decode on every core into a buffer of the application; `ReadRegionAsync`,
  `ReadChannelAsync`, `SaveAsync(path)`; progress (`IProgress<double>`) on the asynchronous reads and saves.
- `TiffImage.ReadIndices` (palette indices), `TiffImageWriter.SetExtraChannels`, `TiffMetadata.PixelTags` and `CopyFrom`,
  `TiffReadOptions.Constrained`, `TiffWriteOptions.SkipUnsupportedPages`.

### Changed

- `SaveAsync(Stream, options, progress, cancellationToken)` and `ReadPixelsAsync(scale, order, progress, cancellationToken)`: a
  progress parameter comes before the token (a token passed by position must now be named).
- `SetPixels` removes the tags of the ranges of the former values; MinIsWhite, palette and YCbCr images are read without them.
- Faster: planar and FITS-cube reads, normalised and converted reads, 12 and 14-bit samples, big-endian files, predictors,
  LZW, files of many small strips, single-strip Deflate images (in bounded memory), JPEG without AVX2.

## 0.1.0

The first version, written from scratch after the [review of the first implementation](docs/review-2026-11-05.md).

### Reading

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
- Metadata: the common tags as properties, any other tag by its code, Astro-TIFF headers kept byte for byte.
- Every written combination is read back with the same samples by tifffile, and by Pillow for the types it supports.

### Platforms

- net10.0 on Windows and Linux, NativeAOT (checked in CI), .NET MAUI on Android (built in CI, run on an emulator by hand);
  little-endian processors.
  Trimmable and AOT-compatible, no dependency.
