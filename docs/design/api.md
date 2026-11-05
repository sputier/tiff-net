# Public API 1.0 — design

Status: **validated on 2026-11-05**. This page fixes the public surface before any code is written, because most of these
choices cannot change after 1.0 without breaking callers (review, decision D2). It follows the conventions of Eleris.FitsNet so
that an application using both libraries reads the same way.

Requirements covered: DOM-01 to DOM-20, SEC-05, SEC-09 to SEC-11, PERF-11, PERF-12 of the [review](../review-2026-11-05.md).

## Principles

1. **Same shape as FitsNet.** A file is a list of images (`TiffFile` ↔ `FitsFile`), an image has metadata and pixels
   (`TiffImage` ↔ `ImageHdu`), `Open` reads lazily and `Read` holds everything in memory, `ReadPixels` converts and
   `ReadRawPixels` returns the stored values, `Create(pixels, …)` takes the pixels first, options are records with a `Default`.
2. **Nothing implicit.** No guessed colour model, no implicit normalisation, no silent thumbnail: what the file says is exposed,
   what the caller wants is asked for.
3. **A file survives a round trip.** Reading then saving keeps the metadata and the meaning of the channels. Only what the
   library rewrites by construction (layout, compression, offsets) changes.
4. **Bounded and cancellable.** Every read is bounded by `TiffReadOptions`; long operations have async and cancellable forms.
5. **Pixels are arrays of the sample type** (`ushort[]`, `float[]`…), never `byte[]` reinterpreted: an image read in one
   piece is limited by .NET arrays to 2³¹ − 1 *elements*, not bytes (8 GB of `float`). Larger images are read by region.

## Overview

```csharp
namespace Eleris.TiffNet;

public sealed class TiffFile : IReadOnlyList<TiffImage>, IDisposable { … }   // the pages of a file
public sealed class TiffImage { … }                                          // one image: pixels + metadata
public sealed class TiffImageWriter : IDisposable { … }                      // streams an image too large for one array
public sealed class TiffMetadata : IEnumerable<TiffTag> { … }                // tags other than the structure
public sealed record TiffTag { … }                                           // one tag, typed values
public sealed record TiffReadOptions { … }
public sealed record TiffWriteOptions { … }
public sealed record TiffResolution(double X, double Y, TiffResolutionUnit Unit);
public sealed record TiffExtraChannel(TiffExtraChannelKind Kind);
public sealed record TiffStorage { … }                                       // how an image is stored in its file
public sealed class TiffException : Exception { public string? Limit { get; } }   // name of the TiffReadOptions limit exceeded, if any

public enum TiffSampleType     { UInt8 = 1, Int8 = 2, UInt16 = 3, Int16 = 4, UInt32 = 5, Int32 = 6, UInt64 = 7, Int64 = 8, Float16 = 9, Float32 = 10, Float64 = 11 }
public enum TiffColorModel     { Grey = 1, Rgb = 2 }
public enum TiffExtraChannelKind { Data = 0, AssociatedAlpha = 1, UnassociatedAlpha = 2 }
public enum TiffPixelOrder     { Interleaved = 0, Planar = 1 }
public enum TiffPixelScale     { Stored = 0, Normalized = 1 }
public enum TiffCompression    { None = 1, Lzw = 5, Jpeg = 7, Deflate = 8, PackBits = 32773 }   // TIFF codes
public enum TiffCompressionLevel { Fastest = 1, Optimal = 2, SmallestSize = 3 }
public enum TiffPredictor      { Auto = 0, None = 1, Horizontal = 2, FloatingPoint = 3 }
public enum TiffLayout         { Strips = 0, Tiles = 1 }
public enum TiffBigTiff        { Auto = 0, Never = 1, Always = 2 }
public enum TiffResolutionUnit { None = 1, Inch = 2, Centimeter = 3 }
public enum TiffPhotometric    { MinIsWhite = 0, MinIsBlack = 1, Rgb = 2, Palette = 3, YCbCr = 6 }
public enum TiffTagType        { Byte = 1, Ascii = 2, Short = 3, Long = 4, Rational = 5, SByte = 6, Undefined = 7, SShort = 8,
                                 SLong = 9, SRational = 10, Float = 11, Double = 12, Ifd = 13, Long8 = 16, SLong8 = 17, Ifd8 = 18 }
```

Enum values are explicit and never renumbered. `TiffCompression` uses the TIFF codes so that a value read from a file maps
directly; `Jpeg` is readable but not writable (see [coverage](coverage.md)).

## `TiffFile`

```csharp
public sealed class TiffFile : IReadOnlyList<TiffImage>, IDisposable
{
    public TiffFile();                                     // empty, to build a file

    // Reading
    public static TiffFile Open(string path, TiffReadOptions? options = null);
    public static TiffFile Read(Stream stream, TiffReadOptions? options = null);
    public static Task<TiffFile> ReadAsync(Stream stream, TiffReadOptions? options = null, CancellationToken cancellationToken = default);

    // The pages
    public int Count { get; }
    public TiffImage this[int index] { get; }
    public TiffImage Primary { get; }                      // first full-resolution image (see below)
    public TiffFile Add(TiffImage image);
    public TiffFile Insert(int index, TiffImage image);
    public void RemoveAt(int index);
    public bool Remove(TiffImage image);

    // Writing
    public void Save(string path, TiffWriteOptions? options = null);
    public void Save(Stream stream, TiffWriteOptions? options = null);
    public Task SaveAsync(Stream stream, TiffWriteOptions? options = null, CancellationToken cancellationToken = default);

    public void Dispose();
}
```

- **`Open`** reads the header and every directory (bounded by the options), not the pixels. The file stays open until
  `Dispose`; pixels are read on demand, a region reads only the strips or tiles it covers. Opening costs memory proportional to
  the directories, not to the file.
- **`Read`** loads the whole stream in memory (bounded by `MaxInputBytes`) and works on that copy: the stream can be
  closed afterwards and need not be seekable. `ReadAsync` reads the stream asynchronously; decoding stays synchronous and
  on demand.
- **`Primary`** is the first image whose `NewSubfileType` does not mark it as a reduced-resolution version (bit 0) or a
  transparency mask (bit 2). If every image is so marked, it is the first one. A thumbnail in the first directory (DNG-like
  layouts) is therefore never returned by mistake (DOM-04).
- **Pages** are the images of the main chain of directories, in file order. Sub-IFDs (`SubIFDs`, tag 330) are not pages: they
  are reached through `TiffImage.SubImages` (read-only in 1.0).
- **`Save`** writes all the pages. `Save(path)` writes a temporary file in the same directory then replaces the target, so a
  failure leaves an existing file as it was; the permissions of a replaced file are kept where the platform allows (SEC-12).
  `Save(Stream)` writes from the current position and leaves the stream open; a stream that cannot seek is written through a
  bounded buffer.

## `TiffImage`

```csharp
public sealed class TiffImage
{
    // Geometry and samples
    public int Width { get; }
    public int Height { get; }
    public int Channels { get; }                           // colour channels + extra channels
    public TiffSampleType SampleType { get; }
    public TiffColorModel ColorModel { get; }              // Grey: 1 colour channel, Rgb: 3
    public IReadOnlyList<TiffExtraChannel> ExtraChannels { get; }
    public bool IsReducedResolution { get; }               // NewSubfileType bit 0
    public string? UnsupportedReason { get; }              // why the library cannot deliver this image, or null
    public IReadOnlyList<TiffImage> SubImages { get; }     // SubIFDs (read only)
    public void SetExtraChannels(params TiffExtraChannelKind[] kinds);   // one kind per extra channel

    // How it is stored in the file it was read from (null for an image created in memory)
    public TiffStorage? Storage { get; }

    // Tags other than the structure: description, ICC profile, resolution, unknown tags…
    public TiffMetadata Metadata { get; }

    // Creating
    public static TiffImage Create<T>(ReadOnlySpan<T> pixels, int width, int height, int channels = 1,
        TiffColorModel? colorModel = null, TiffPixelOrder order = TiffPixelOrder.Interleaved) where T : unmanaged;
    public void SetPixels<T>(ReadOnlySpan<T> pixels, int width, int height, int channels = 1,
        TiffColorModel? colorModel = null, TiffPixelOrder order = TiffPixelOrder.Interleaved) where T : unmanaged;

    // Reading pixels
    public T[] ReadRawPixels<T>(TiffPixelOrder order = TiffPixelOrder.Interleaved) where T : unmanaged;
    public T[] ReadPixels<T>(TiffPixelScale scale = TiffPixelScale.Stored, TiffPixelOrder order = TiffPixelOrder.Interleaved) where T : unmanaged;
    public void ReadPixels<T>(Span<T> destination, TiffPixelScale scale = TiffPixelScale.Stored, TiffPixelOrder order = TiffPixelOrder.Interleaved) where T : unmanaged;
    public T[] ReadRegion<T>(int x, int y, int width, int height, TiffPixelScale scale = TiffPixelScale.Stored, TiffPixelOrder order = TiffPixelOrder.Interleaved) where T : unmanaged;
    public T[] ReadRawRegion<T>(int x, int y, int width, int height, TiffPixelOrder order = TiffPixelOrder.Interleaved) where T : unmanaged;
    public T[] ReadChannel<T>(int channel, TiffPixelScale scale = TiffPixelScale.Stored) where T : unmanaged;
    public Task<T[]> ReadPixelsAsync<T>(TiffPixelScale scale = TiffPixelScale.Stored, TiffPixelOrder order = TiffPixelOrder.Interleaved, CancellationToken cancellationToken = default) where T : unmanaged;
}
```

### Images the library does not handle

A page whose format the library does not handle (CMYK, Lab, a compression it does not read…) does not make the whole file fail:
it is listed with the others, with its size, its metadata, its `Storage` and `UnsupportedReason`. Its `SampleType`, `ColorModel`
and pixels throw a `NotSupportedException` that gives the reason. A thumbnail in an unusual format therefore does not hide the
main image of its file.

### Channels and colour model (DOM-02, DOM-03)

- `ColorModel` is what the file says (`PhotometricInterpretation`), never inferred from the number of channels. A grey file with
  three samples is `Grey` with two extra channels, and is written back as grey.
- `Create` takes the colour model explicitly. When it is omitted: 1 or 2 channels are `Grey`, **and 3 or more are refused with
  an `ArgumentException`** asking for the model: an L/Hα/OIII stack must not become RGB by accident.
- Extra channels carry their kind (`ExtraSamples`): `Data`, `AssociatedAlpha` (premultiplied) or `UnassociatedAlpha`. They are
  read and written as such. `Create` makes them `Data`; `SetExtraChannels` (on `TiffImage`) changes the kinds.

### Pixel values (DOM-05, DOM-13)

| Method | Returns | Allowed `T` |
| --- | --- | --- |
| `ReadRawPixels<T>` | the stored values, unchanged | exactly the type of `SampleType` (`Half` for `Float16`), otherwise `InvalidOperationException` |
| `ReadPixels<T>(Stored)` | the stored values, converted to `T` | `float` and `double` always; an integer type when every stored value fits (same or wider type of the same signedness, or unsigned into a wider signed); otherwise `InvalidOperationException` |
| `ReadPixels<T>(Normalized)` | `float` or `double` only: unsigned integers divided by the maximum of their type (→ [0, 1]), signed integers divided by the maximum of their type (→ [−1, 1], the minimum giving slightly below −1), floating point values unchanged | `float`, `double` |

- The library never guesses whether floating point data is in [0, 1] (PixInsight, Siril) or in ADU (other tools): it returns
  what is stored. `TiffMetadata.SampleRange` exposes `SMinSampleValue` / `SMaxSampleValue` when the file has them.
- NaN, infinities and the sign of zero are preserved by every path.
- Pixels are **top-down, left to right**: the `Orientation` tag is applied on reading (values 1 to 8) and `Orientation = 1` is
  written. `TiffStorage.Orientation` tells what the file said.
- `TiffPixelOrder.Interleaved` is `[y][x][c]`; `Planar` is `[c][y][x]`, the order of a FITS cube (`NAXIS1 = width`,
  `NAXIS2 = height`, `NAXIS3 = channels`), so that `ImageHdu.Create(tiff.ReadPixels<float>(order: Planar), w, h, c)` works
  (rows still need flipping for FITS, which counts rows from the bottom — that is the bridge's job, not this library's).

### Memory (PERF-11)

- Every `Read*` returns a new array, as in FitsNet; `ReadPixels(Span<T>)` fills a caller's buffer, on the calling thread, and
  allocates nothing beyond the decoding state. The pixel methods take `T : unmanaged, INumber<T>`. An image read from a file does not keep its pixels: reading twice decodes twice.
- `Create` and `SetPixels` copy the caller's pixels once, so that the image does not change if the caller's buffer does.
- An image of more than 2³¹ − 1 samples cannot be read in one array: `ReadPixels` throws an `InvalidOperationException` that
  points to `ReadRegion`. Such an image is written with [`TiffImageWriter`](#tiffimagewriter).

### Low bit depths and palettes

- Samples of 1 to 16 bits other than 8 and 16 (bilevel masks, 2 and 4-bit images, 12 and 14-bit packed camera data) are
  delivered unpacked as `UInt8` (up to 8 bits) or `UInt16` (9 to 16 bits), with their values unchanged (a 12-bit sample stays
  in 0–4095). `TiffStorage.BitsPerSample` gives the stored depth, and `ReadPixels(Normalized)` divides by `2^bits − 1`, not by
  the maximum of the delivered type. Only unsigned samples are supported at these depths.
- Palette images are delivered as `Rgb` `UInt16` samples taken from the `ColorMap`; `TiffStorage.Photometric` says the file
  was a palette. The indices themselves are not exposed in 1.0.
- These images are read only: they are written back with their delivered type (an `UInt8` mask, an `Rgb` `UInt16` image).

## `TiffMetadata` and `TiffTag` (DOM-01)

```csharp
public sealed class TiffMetadata : IEnumerable<TiffTag>
{
    // Typed access to the common tags (null when absent; setting null removes the tag)
    public string? Description { get; set; }               // 270 ImageDescription — Astro-TIFF FITS header
    public string? Software { get; set; }                  // 305
    public DateTime? DateTime { get; set; }                // 306, local time without zone as TIFF defines it
    public string? Artist { get; set; }                    // 315
    public string? Copyright { get; set; }                 // 33432
    public string? HostComputer { get; set; }              // 316
    public string? DocumentName { get; set; }              // 269
    public string? PageName { get; set; }                  // 285
    public TiffResolution? Resolution { get; set; }        // 282, 283, 296
    public (double Min, double Max)? SampleRange { get; set; }   // 340, 341
    public byte[]? IccProfile { get; set; }                // 34675
    public byte[]? Xmp { get; set; }                       // 700
    public byte[]? Iptc { get; set; }                      // 33723
    public IReadOnlyList<TiffTag> Exif { get; }            // 34665 sub-directory, read only in 1.0
    public IReadOnlyList<TiffTag> Gps { get; }             // 34853 sub-directory, read only in 1.0

    // Generic access, for any other tag
    public TiffTag? this[int code] { get; }
    public void Set(TiffTag tag);
    public bool Remove(int code);
    public int Count { get; }
}

public sealed class TiffTag : IEquatable<TiffTag>     // equal when code, type and values are
{
    public int Code { get; }
    public TiffTagType Type { get; }                       // Byte, Ascii, Short, Long, Rational, …, Long8, Ifd8
    public int Count { get; }
    public static TiffTag FromString(int code, string value);
    public static TiffTag FromBytes(int code, ReadOnlySpan<byte> value, TiffTagType type = TiffTagType.Undefined);
    public static TiffTag FromNumbers<T>(int code, ReadOnlySpan<T> values) where T : unmanaged;
    public string AsString();
    public T[] AsNumbers<T>() where T : unmanaged;
    public ReadOnlyMemory<byte> AsBytes();
}
```

- `Metadata` holds every tag of the directory **except the structural ones**, which the library computes on writing: geometry,
  samples, photometric, compression, predictor, strips/tiles and their offsets, planar configuration, orientation, extra samples,
  `NewSubfileType`, `SubIFDs`, `JPEGTables`.
- Unknown tags are kept and written back, **except tags that hold offsets into the file**, which would point nowhere after a
  rewrite: types `IFD`/`IFD8` and the known offset tags (EXIF, GPS, Interoperability, `SubIFDs`, `FreeOffsets`…). They are
  dropped on writing and listed in the documentation.
- Values are bounded on reading (`TiffReadOptions.MaxTagBytes`, the size of the file): a tag cannot make the reader allocate
  more than the file holds.
- Astro-TIFF: the FITS header is `Description`, untouched. Parsing it into FITS cards, ignoring the structural keywords and
  flipping rows belong to the bridge between this library and FitsNet (DOM-14, DOM-15), outside this package.

## `TiffStorage`

```csharp
public sealed record TiffStorage
{
    public TiffCompression Compression { get; }
    public TiffPredictor Predictor { get; }
    public TiffLayout Layout { get; }
    public int TileWidth { get; } public int TileHeight { get; }   // or RowsPerStrip for strips
    public int RowsPerStrip { get; }
    public TiffPixelOrder PlanarConfiguration { get; }
    public TiffPhotometric Photometric { get; }                    // as stored (MinIsWhite, Palette, YCbCr…)
    public int BitsPerSample { get; }                              // as stored (1 to 64)
    public bool BigEndian { get; }
    public bool BigTiff { get; }
    public int Orientation { get; }                                // as stored; pixels are always delivered top-left
    // JpegQuality (estimated from the quantization tables) comes with the JPEG decoder.
}
```

Read only. It describes the file an image came from, so that an application can save "as it was" with
`TiffWriteOptions.From(image.Storage)`.

## `TiffImageWriter`

Writes one image piece by piece, for mosaics and other images that do not fit in one array, without holding the image in memory.

```csharp
public sealed class TiffImageWriter : IDisposable
{
    public static TiffImageWriter Create(string path, long width, long height, TiffSampleType sampleType, int channels = 1,
        TiffColorModel? colorModel = null, TiffMetadata? metadata = null, TiffWriteOptions? options = null);
    public static TiffImageWriter Create(Stream stream, long width, long height, TiffSampleType sampleType, int channels = 1,
        TiffColorModel? colorModel = null, TiffMetadata? metadata = null, TiffWriteOptions? options = null);   // seekable, left open

    public long Width { get; }
    public long Height { get; }
    public void WriteRows<T>(ReadOnlySpan<T> pixels, int rowCount) where T : unmanaged;          // strips: rows in order, top to bottom
    public void WriteTile<T>(long tileX, long tileY, ReadOnlySpan<T> pixels) where T : unmanaged; // tiles: any order, each once
    public void Complete();                                                                       // writes the directory
    public void Dispose();                                                                        // without Complete: output abandoned
}
```

- Width and height go up to the TIFF limit (2³² − 1); the file is BigTIFF when its size requires it (`TiffBigTiff.Auto`
  decides from the declared geometry, before the first byte is written).
- `Layout = Strips` takes `WriteRows`; `Layout = Tiles` takes `WriteTile`, in any order, so that a mosaic can be written as its
  panels are computed. `Complete` refuses missing rows or tiles.
- Encoding uses the cores as `TiffFile.Save` does, and memory stays bounded by `MaxDegreeOfParallelism` strips or tiles.
- The path overload has the semantics of `Save(path)`: a temporary file replaces the target on `Complete`.

## Options

```csharp
public sealed record TiffReadOptions
{
    public static TiffReadOptions Default { get; }
    public bool Strict { get; init; }                       // refuse tolerated defects (see reliability)
    public long MaxInputBytes { get; init; }                // Read/ReadAsync: bytes taken from the stream. Default 2 147 483 591
    public long MaxPixelBytes { get; init; }                // per decoded image or region. Default 2 147 483 591; 256 MB recommended on mobile
    public int MaxDirectories { get; init; }                // pages + sub-directories. Default 10 000
    public int MaxTagsPerDirectory { get; init; }           // Default 4 096
    public long MaxTagBytes { get; init; }                  // per tag. Default 64 MB (ICC profiles and XMP fit)
    public long MaxMetadataBytes { get; init; }             // all that opening reads: entries, tag values, tables. Default 256 MB
    public int MaxJpegScans { get; init; }                  // progressive JPEG. Default 100
    public int MaxDegreeOfParallelism { get; init; }        // Default -1 (all cores); 1 for single-threaded
}

public sealed record TiffWriteOptions
{
    public static TiffWriteOptions Default { get; }
    public TiffCompression Compression { get; init; } = TiffCompression.Deflate;
    public TiffCompressionLevel Level { get; init; } = TiffCompressionLevel.Optimal;
    public TiffPredictor Predictor { get; init; } = TiffPredictor.Auto;   // Horizontal for integers, FloatingPoint for floats
    public TiffLayout Layout { get; init; } = TiffLayout.Strips;
    public int TileSize { get; init; } = 256;                              // multiple of 16
    public TiffPixelOrder PlanarConfiguration { get; init; } = TiffPixelOrder.Interleaved;
    public TiffBigTiff BigTiff { get; init; } = TiffBigTiff.Auto;          // BigTIFF only when classic offsets overflow
    public int MaxDegreeOfParallelism { get; init; } = -1;
    public static TiffWriteOptions From(TiffStorage storage);              // same compression, predictor, layout (JPEG → Deflate)
}
```

- The output does not depend on `MaxDegreeOfParallelism`: same bytes on one thread or many, as in FitsNet.
- `Compression = Jpeg` on writing is refused with `NotSupportedException` (lossy compression has no place in a science
  pipeline; see [coverage](coverage.md)).

## Exceptions

Same table as FitsNet:

| Situation | Exception |
| --- | --- |
| Data that is not valid TIFF: bad structure, truncated or damaged data, a limit of `TiffReadOptions` exceeded | `TiffException` (with `Limit` set when a limit was exceeded) |
| A valid TIFF feature the library does not handle (CMYK, old-style JPEG, CCITT compression…) | `NotSupportedException` |
| A wrong argument (region outside the image, wrong array length, missing colour model) | `ArgumentException`, `ArgumentOutOfRangeException` |
| A call that does not fit the state (reading `float` raw from a `UInt16` image) | `InvalidOperationException` |
| The caller's file or stream fails | its own exception (`IOException`…), never relabelled |
| Cancellation | `OperationCanceledException` |
| Use after `Dispose` | `ObjectDisposedException` |

A damaged or hostile file fails with a `TiffException` or a `NotSupportedException`, **within the limits of the options**: never
an `OutOfMemoryException`, an `IndexOutOfRangeException` or an `OverflowException`, and never after allocating more than the
limits allow. Messages name the file by its file name, never by its full path (SEC-09).

## Threads

- Reading the pixels of images of the same file from several threads is allowed (each read uses its own handle offset;
  `RandomAccess` on a file, the shared buffer for `Read`).
- Modifying a `TiffFile`, a `TiffImage` or a `TiffMetadata` while another thread uses it is not, as for the .NET collections.
- Decoding and encoding use several cores internally (`MaxDegreeOfParallelism`).

## Typical call sites (Urania)

```csharp
// 1. Display a stacked image, whatever its type
using var file = TiffFile.Open(path);
TiffImage image = file.Primary;
float[] lum = await image.ReadPixelsAsync<float>(TiffPixelScale.Normalized, cancellationToken: ct);

// 2. Save a 16-bit result with its Astro-TIFF header and ICC profile
var result = TiffImage.Create<ushort>(pixels, width, height, channels: 3, TiffColorModel.Rgb);
result.Metadata.Description = fitsHeaderText;
result.Metadata.IccProfile = source.Metadata.IccProfile;
result.Metadata.Software = "Urania 1.0";
new TiffFile().Add(result).Save("result.tif");          // Deflate + predictor by default

// 3. Untrusted file on a phone
var options = TiffReadOptions.Default with { MaxPixelBytes = 256L << 20 };
using var picked = TiffFile.Read(stream, options);       // works on a non-seekable picker stream
```

## Open questions

All the questions of the design are decided: see [architecture](architecture.md#decisions).
