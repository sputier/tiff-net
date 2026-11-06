# Writing

`TiffImage.Create<T>(pixels, width, height, channels, colorModel)` makes an image from pixels of the application (copied once,
interleaved or planar); with 3 channels or more the colour model is required, so that a stack of filters never becomes RGB by
accident. `SetPixels` replaces the pixels of an image, `SetExtraChannels` says what the extra channels hold (alpha or data).
`TiffFile.Save` writes the pages:

- **To a path**: a temporary file next to the target replaces it, so that a failure leaves an existing file as it was; the
  permissions of a replaced file are kept, and on Unix the temporary file has them from its creation (a private file is never
  readable by others while it is written); the file being read can be the target. A symbolic link is written through: do not
  save into a folder that other users can write.
- **An image opened from a file** is saved band after band, but its pixels count in the `MaxPixelBytes` the file was opened
  with, as reading the whole image would: open a file larger than 2 GB of pixels with a larger limit to save it again, or copy
  it by region into a `TiffImageWriter`.
- **To a stream**: from its position, left open; a stream that cannot seek is written through a temporary file (readable by
  its owner only on Unix). `SaveAsync` (to a stream or a path) encodes on the threads of the pool and reports its progress to an `IProgress<double>`.

The defaults write **Deflate with the predictor that suits the samples**, in strips of about 256 KB, as classic TIFF unless the
file could exceed 4 GB: lossless, compact, readable by every TIFF reader. JPEG is not written.

<!-- example: WritingExamples.Options -->
```csharp
var options = TiffWriteOptions.Default with
{
    Compression = TiffCompression.Deflate,          // Deflate (default), LZW, PackBits or None
    Level = TiffCompressionLevel.Fastest,           // of Deflate: faster, files larger
    Layout = TiffLayout.Tiles,                      // 256 x 256 tiles (TileSize)
    PlanarConfiguration = TiffPixelOrder.Planar,    // each channel in its own plane
};
new TiffFile().Add(image).Save(path, options);
```
<!-- /example -->

| Option | Values | Default |
| --- | --- | --- |
| `Compression` | `Deflate`, `Lzw`, `PackBits`, `None` | `Deflate` |
| `Level` | `Fastest` (2 to 2.5 times faster, files 15 to 50 % larger), `Optimal`, `SmallestSize` (much slower, rarely smaller with the zlib of .NET) | `Optimal` |
| `Predictor` | `Auto` (horizontal for integers of 8 to 32 bits, floating point for floating point), `None`, `Horizontal`, `FloatingPoint` | `Auto` |
| `Layout`, `TileSize` | strips, or square tiles of a multiple of 16 | strips |
| `PlanarConfiguration` | interleaved, or one plane per channel | interleaved |
| `BigTiff` | `Auto`, `Never`, `Always` | `Auto` |
| `MaxDegreeOfParallelism` | -1 (every core) or a number of threads | -1 |
| `SkipUnsupportedPages` | leave out the pages the library cannot read, instead of failing | false |

The file does not depend on the number of threads. To save an image as its file stored it:

<!-- example: WritingExamples.AsStored -->
```csharp
using var file = TiffFile.Open(source);
file.Primary.Metadata.Software = "Urania";

// Same compression, predictor, layout and BigTIFF as the original (JPEG becomes Deflate: it is not written).
file.Save(target, TiffWriteOptions.From(file.Primary.Storage!));
```
<!-- /example -->

Every combination the library writes is read back with the same samples by tifffile, and by Pillow (libtiff) for the types it
supports.
