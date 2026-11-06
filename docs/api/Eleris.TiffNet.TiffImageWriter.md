# TiffImageWriter Class

**Namespace:** `Eleris.TiffNet`

**Inheritance:** `object` → `TiffImageWriter`

Writes one image piece by piece, for mosaics and other images that do not fit in one array, without holding the image in memory: rows in order ([`TiffLayout.Strips`](Eleris.TiffNet.TiffLayout.md#eleris-tiffnet-tifflayout-strips)) or tiles in any order ([`TiffLayout.Tiles`](Eleris.TiffNet.TiffLayout.md#eleris-tiffnet-tifflayout-tiles)), then [`TiffImageWriter.Complete`](#eleris-tiffnet-tiffimagewriter-complete).

```csharp
public sealed class TiffImageWriter : IDisposable
```

**Remarks**

The image is up to 2 147 483 647 pixels wide and high (what the library reads back); the file is BigTIFF when its size could exceed 4 GB ([`TiffBigTiff.Auto`](Eleris.TiffNet.TiffBigTiff.md#eleris-tiffnet-tiffbigtiff-auto) decides from the geometry, before the first byte is written). Strips or tiles are encoded on the cores as they come, at most 2 × [`TiffWriteOptions.MaxDegreeOfParallelism`](Eleris.TiffNet.TiffWriteOptions.md#eleris-tiffnet-tiffwriteoptions-maxdegreeofparallelism) at once, which bounds the memory.

The path overload writes a temporary file that replaces the target on [`TiffImageWriter.Complete`](#eleris-tiffnet-tiffimagewriter-complete), as [`TiffFile.Save`](Eleris.TiffNet.TiffFile.md#eleris-tiffnet-tifffile-save-system-string-eleris-tiffnet-tiffwriteoptions) does; [`TiffImageWriter.Dispose`](#eleris-tiffnet-tiffimagewriter-dispose) without [`TiffImageWriter.Complete`](#eleris-tiffnet-tiffimagewriter-complete) abandons it. A writer is used from one thread.

## Properties

| Member | Description |
| --- | --- |
| [`Height`](#eleris-tiffnet-tiffimagewriter-height) | The height of the image, in pixels. |
| [`Width`](#eleris-tiffnet-tiffimagewriter-width) | The width of the image, in pixels. |

## Methods

| Member | Description |
| --- | --- |
| [`Complete()`](#eleris-tiffnet-tiffimagewriter-complete) | Writes what is left and the directory; for a path, puts the file in place of the target. |
| [`Create(Stream, long, long, TiffSampleType, int, TiffColorModel?, TiffMetadata, TiffWriteOptions)`](#eleris-tiffnet-tiffimagewriter-create-system-io-stream-system-int64-system-int64-eleris-tiffnet-tiffsampletype-system-int32-system-nullable-eleris-tiffnet-tiffcolormodel-eleris-tiffnet-tiffmetadata-eleris-tiffnet-tiffwriteoptions) | Starts writing an image into a stream, from its current position; the stream must seek and is left open. |
| [`Create(string, long, long, TiffSampleType, int, TiffColorModel?, TiffMetadata, TiffWriteOptions)`](#eleris-tiffnet-tiffimagewriter-create-system-string-system-int64-system-int64-eleris-tiffnet-tiffsampletype-system-int32-system-nullable-eleris-tiffnet-tiffcolormodel-eleris-tiffnet-tiffmetadata-eleris-tiffnet-tiffwriteoptions) | Starts writing an image into a file, through a temporary file that replaces it on [`TiffImageWriter.Complete`](#eleris-tiffnet-tiffimagewriter-complete). |
| [`Dispose()`](#eleris-tiffnet-tiffimagewriter-dispose) | Ends the writer; without [`TiffImageWriter.Complete`](#eleris-tiffnet-tiffimagewriter-complete), the output is abandoned (the temporary file of a path is removed). |
| [`WriteRows<T>(ReadOnlySpan<T>, int)`](#eleris-tiffnet-tiffimagewriter-writerows-1-system-readonlyspan-0-system-int32) | Writes the next rows of the image, top to bottom ([`TiffLayout.Strips`](Eleris.TiffNet.TiffLayout.md#eleris-tiffnet-tifflayout-strips)). |
| [`WriteTile<T>(long, long, ReadOnlySpan<T>)`](#eleris-tiffnet-tiffimagewriter-writetile-1-system-int64-system-int64-system-readonlyspan-0) | Writes a tile ([`TiffLayout.Tiles`](Eleris.TiffNet.TiffLayout.md#eleris-tiffnet-tifflayout-tiles)), in any order, each once. |

## Property details

<a id="eleris-tiffnet-tiffimagewriter-height"></a>
### `Height`

The height of the image, in pixels.

```csharp
public long Height { get; }
```

<a id="eleris-tiffnet-tiffimagewriter-width"></a>
### `Width`

The width of the image, in pixels.

```csharp
public long Width { get; }
```

## Method details

<a id="eleris-tiffnet-tiffimagewriter-complete"></a>
### `Complete()`

Writes what is left and the directory; for a path, puts the file in place of the target.

```csharp
public void Complete()
```

**Exceptions**

- [`InvalidOperationException`](https://learn.microsoft.com/dotnet/api/system.invalidoperationexception) — Rows or tiles are missing, or an earlier write failed.

<a id="eleris-tiffnet-tiffimagewriter-create-system-io-stream-system-int64-system-int64-eleris-tiffnet-tiffsampletype-system-int32-system-nullable-eleris-tiffnet-tiffcolormodel-eleris-tiffnet-tiffmetadata-eleris-tiffnet-tiffwriteoptions"></a>
### `Create(Stream, long, long, TiffSampleType, int, TiffColorModel?, TiffMetadata, TiffWriteOptions)`

Starts writing an image into a stream, from its current position; the stream must seek and is left open.

```csharp
public static TiffImageWriter Create(Stream stream, long width, long height, TiffSampleType sampleType, int channels = 1, TiffColorModel? colorModel = null, TiffMetadata? metadata = null, TiffWriteOptions? options = null)
```

**Parameters**

- `stream` — The stream.
- `width` — The width, in pixels (up to 2 147 483 647).
- `height` — The height, in pixels (up to 2 147 483 647).
- `sampleType` — The type of the samples.
- `channels` — The channels per pixel.
- `colorModel` — What the colour channels are; required with 3 channels or more.
- `metadata` — The tags to write with the image, or none.
- `options` — How to write; [`TiffWriteOptions.Default`](Eleris.TiffNet.TiffWriteOptions.md#eleris-tiffnet-tiffwriteoptions-default) when `null`.

**Returns**

The writer, to dispose.

**Exceptions**

- [`ArgumentException`](https://learn.microsoft.com/dotnet/api/system.argumentexception) — The stream cannot seek or be written, or the sizes do not fit the image.

<a id="eleris-tiffnet-tiffimagewriter-create-system-string-system-int64-system-int64-eleris-tiffnet-tiffsampletype-system-int32-system-nullable-eleris-tiffnet-tiffcolormodel-eleris-tiffnet-tiffmetadata-eleris-tiffnet-tiffwriteoptions"></a>
### `Create(string, long, long, TiffSampleType, int, TiffColorModel?, TiffMetadata, TiffWriteOptions)`

Starts writing an image into a file, through a temporary file that replaces it on [`TiffImageWriter.Complete`](#eleris-tiffnet-tiffimagewriter-complete).

```csharp
public static TiffImageWriter Create(string path, long width, long height, TiffSampleType sampleType, int channels = 1, TiffColorModel? colorModel = null, TiffMetadata? metadata = null, TiffWriteOptions? options = null)
```

**Parameters**

- `path` — The path of the file.
- `width` — The width, in pixels (up to 2 147 483 647).
- `height` — The height, in pixels (up to 2 147 483 647).
- `sampleType` — The type of the samples given to [`TiffImageWriter.WriteRows`](#eleris-tiffnet-tiffimagewriter-writerows-1-system-readonlyspan-0-system-int32) or [`TiffImageWriter.WriteTile`](#eleris-tiffnet-tiffimagewriter-writetile-1-system-int64-system-int64-system-readonlyspan-0).
- `channels` — The channels per pixel: the colour channels (1 for grey, 3 for RGB), then extra channels (of kind Data).
- `colorModel` — What the colour channels are; required with 3 channels or more.
- `metadata` — The tags to write with the image (description, ICC profile…), or none.
- `options` — How to write; [`TiffWriteOptions.Default`](Eleris.TiffNet.TiffWriteOptions.md#eleris-tiffnet-tiffwriteoptions-default) when `null`. The layout chooses between rows and tiles.

**Returns**

The writer, to dispose.

**Exceptions**

- [`ArgumentException`](https://learn.microsoft.com/dotnet/api/system.argumentexception) — The sizes, the colour model or the layout do not fit the image.
- [`IOException`](https://learn.microsoft.com/dotnet/api/system.io.ioexception) — The file cannot be written.

<a id="eleris-tiffnet-tiffimagewriter-dispose"></a>
### `Dispose()`

Ends the writer; without [`TiffImageWriter.Complete`](#eleris-tiffnet-tiffimagewriter-complete), the output is abandoned (the temporary file of a path is removed).

```csharp
public void Dispose()
```

<a id="eleris-tiffnet-tiffimagewriter-writerows-1-system-readonlyspan-0-system-int32"></a>
### `WriteRows<T>(ReadOnlySpan<T>, int)`

Writes the next rows of the image, top to bottom ([`TiffLayout.Strips`](Eleris.TiffNet.TiffLayout.md#eleris-tiffnet-tifflayout-strips)).

```csharp
public void WriteRows<T>(ReadOnlySpan<T> pixels, int rowCount) where T : unmanaged, INumber<T>
```

**Type parameters**

- `T` — The type of the samples, as declared.

**Parameters**

- `pixels` — Exactly `rowCount × Width × channels` samples, interleaved.
- `rowCount` — The number of rows.

**Exceptions**

- [`InvalidOperationException`](https://learn.microsoft.com/dotnet/api/system.invalidoperationexception) — The writer writes tiles, the rows go beyond the image, or it is completed.
- [`ArgumentException`](https://learn.microsoft.com/dotnet/api/system.argumentexception) — The samples are not of the declared type, or not as many as the rows hold.

<a id="eleris-tiffnet-tiffimagewriter-writetile-1-system-int64-system-int64-system-readonlyspan-0"></a>
### `WriteTile<T>(long, long, ReadOnlySpan<T>)`

Writes a tile ([`TiffLayout.Tiles`](Eleris.TiffNet.TiffLayout.md#eleris-tiffnet-tifflayout-tiles)), in any order, each once.

```csharp
public void WriteTile<T>(long tileX, long tileY, ReadOnlySpan<T> pixels) where T : unmanaged, INumber<T>
```

**Type parameters**

- `T` — The type of the samples, as declared.

**Parameters**

- `tileX` — The column of the tile (0 for the first [`TiffWriteOptions.TileSize`](Eleris.TiffNet.TiffWriteOptions.md#eleris-tiffnet-tiffwriteoptions-tilesize) pixels).
- `tileY` — The row of the tile.
- `pixels` — The samples of the tile, interleaved: a whole tile, or at the right and the bottom edges only the part within the image.

**Exceptions**

- [`InvalidOperationException`](https://learn.microsoft.com/dotnet/api/system.invalidoperationexception) — The writer writes rows, the tile is written already, or it is completed.
- [`ArgumentOutOfRangeException`](https://learn.microsoft.com/dotnet/api/system.argumentoutofrangeexception) — There is no such tile.
- [`ArgumentException`](https://learn.microsoft.com/dotnet/api/system.argumentexception) — The samples are not of the declared type, or not as many as the tile holds.
