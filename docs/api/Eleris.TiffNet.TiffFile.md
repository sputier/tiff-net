# TiffFile Class

**Namespace:** `Eleris.TiffNet`

**Inheritance:** `object` → `TiffFile`

A TIFF file: its pages, in the order of the file. A page is a [`TiffImage`](Eleris.TiffNet.TiffImage.md); reduced-resolution versions stored as sub-directories are reached through [`TiffImage.SubImages`](Eleris.TiffNet.TiffImage.md#eleris-tiffnet-tiffimage-subimages).

```csharp
public sealed class TiffFile : IDisposable, IReadOnlyList<TiffImage>
```

**Remarks**

[`TiffFile.Open`](#eleris-tiffnet-tifffile-open-system-string-eleris-tiffnet-tiffreadoptions) reads the directories of a file, not its pixels, and keeps the file open until [`TiffFile.Dispose`](#eleris-tiffnet-tifffile-dispose): the pixels are read when they are asked for. [`TiffFile.Read`](#eleris-tiffnet-tifffile-read-system-io-stream-eleris-tiffnet-tiffreadoptions) holds a whole stream in memory.

A damaged or hostile file fails with a [`TiffException`](Eleris.TiffNet.TiffException.md), within the limits of [`TiffReadOptions`](Eleris.TiffNet.TiffReadOptions.md).

## Properties

| Member | Description |
| --- | --- |
| [`Count`](#eleris-tiffnet-tifffile-count) | The number of pages. |
| [`Options`](#eleris-tiffnet-tifffile-options) | The options the file was read with. |
| [`Primary`](#eleris-tiffnet-tifffile-primary) | The main image: the first page that is neither a reduced-resolution version of another (a thumbnail) nor a transparency mask, as `NewSubfileType` says; the first page when every page is one of those. |
| [`this[int]`](#eleris-tiffnet-tifffile-item-system-int32) | A page, by its zero-based index in the file. |

## Methods

| Member | Description |
| --- | --- |
| [`Dispose()`](#eleris-tiffnet-tifffile-dispose) | Closes the file opened by [`TiffFile.Open`](#eleris-tiffnet-tifffile-open-system-string-eleris-tiffnet-tiffreadoptions); its pixels can no longer be read. |
| [`GetEnumerator()`](#eleris-tiffnet-tifffile-getenumerator) | Returns an enumerator over the pages. |
| [`Open(string, TiffReadOptions)`](#eleris-tiffnet-tifffile-open-system-string-eleris-tiffnet-tiffreadoptions) | Opens a file and reads its directories; the pixels are read on demand, until the file is disposed. |
| [`Read(Stream, TiffReadOptions)`](#eleris-tiffnet-tifffile-read-system-io-stream-eleris-tiffnet-tiffreadoptions) | Reads a TIFF stream into memory, from its current position: the stream can be closed afterwards, and need not be able to seek. |
| [`ReadAsync(Stream, TiffReadOptions, CancellationToken)`](#eleris-tiffnet-tifffile-readasync-system-io-stream-eleris-tiffnet-tiffreadoptions-system-threading-cancellationtoken) | Reads a TIFF stream into memory asynchronously, from its current position. |

## Property details

<a id="eleris-tiffnet-tifffile-count"></a>
### `Count`

The number of pages.

```csharp
public int Count { get; }
```

<a id="eleris-tiffnet-tifffile-options"></a>
### `Options`

The options the file was read with.

```csharp
public TiffReadOptions Options { get; }
```

<a id="eleris-tiffnet-tifffile-primary"></a>
### `Primary`

The main image: the first page that is neither a reduced-resolution version of another (a thumbnail) nor a transparency mask, as `NewSubfileType` says; the first page when every page is one of those.

```csharp
public TiffImage Primary { get; }
```

<a id="eleris-tiffnet-tifffile-item-system-int32"></a>
### `this[int]`

A page, by its zero-based index in the file.

```csharp
public TiffImage this[int index] { get; }
```

**Parameters**

- `index` — The index of the page.

## Method details

<a id="eleris-tiffnet-tifffile-dispose"></a>
### `Dispose()`

Closes the file opened by [`TiffFile.Open`](#eleris-tiffnet-tifffile-open-system-string-eleris-tiffnet-tiffreadoptions); its pixels can no longer be read. The metadata stays available.

```csharp
public void Dispose()
```

<a id="eleris-tiffnet-tifffile-getenumerator"></a>
### `GetEnumerator()`

Returns an enumerator over the pages.

```csharp
public IEnumerator<TiffImage> GetEnumerator()
```

<a id="eleris-tiffnet-tifffile-open-system-string-eleris-tiffnet-tiffreadoptions"></a>
### `Open(string, TiffReadOptions)`

Opens a file and reads its directories; the pixels are read on demand, until the file is disposed.

```csharp
public static TiffFile Open(string path, TiffReadOptions? options = null)
```

**Parameters**

- `path` — The path of the file.
- `options` — The tolerance and the limits of the read; [`TiffReadOptions.Default`](Eleris.TiffNet.TiffReadOptions.md#eleris-tiffnet-tiffreadoptions-default) when `null`.

**Returns**

The file, to dispose when its pixels are no longer needed.

**Exceptions**

- [`TiffException`](Eleris.TiffNet.TiffException.md) — The file is not TIFF, is damaged, or exceeds a limit of `options`.
- [`IOException`](https://learn.microsoft.com/dotnet/api/system.io.ioexception) — The file cannot be read.

<a id="eleris-tiffnet-tifffile-read-system-io-stream-eleris-tiffnet-tiffreadoptions"></a>
### `Read(Stream, TiffReadOptions)`

Reads a TIFF stream into memory, from its current position: the stream can be closed afterwards, and need not be able to seek.

```csharp
public static TiffFile Read(Stream stream, TiffReadOptions? options = null)
```

**Parameters**

- `stream` — The stream; at most [`TiffReadOptions.MaxInputBytes`](Eleris.TiffNet.TiffReadOptions.md#eleris-tiffnet-tiffreadoptions-maxinputbytes) are taken from it.
- `options` — The tolerance and the limits of the read; [`TiffReadOptions.Default`](Eleris.TiffNet.TiffReadOptions.md#eleris-tiffnet-tiffreadoptions-default) when `null`.

**Returns**

The file.

**Exceptions**

- [`TiffException`](Eleris.TiffNet.TiffException.md) — The data is not TIFF, is damaged, or exceeds a limit of `options`.

<a id="eleris-tiffnet-tifffile-readasync-system-io-stream-eleris-tiffnet-tiffreadoptions-system-threading-cancellationtoken"></a>
### `ReadAsync(Stream, TiffReadOptions, CancellationToken)`

Reads a TIFF stream into memory asynchronously, from its current position.

```csharp
public static Task<TiffFile> ReadAsync(Stream stream, TiffReadOptions? options = null, CancellationToken cancellationToken = default)
```

**Parameters**

- `stream` — The stream; at most [`TiffReadOptions.MaxInputBytes`](Eleris.TiffNet.TiffReadOptions.md#eleris-tiffnet-tiffreadoptions-maxinputbytes) are taken from it.
- `options` — The tolerance and the limits of the read; [`TiffReadOptions.Default`](Eleris.TiffNet.TiffReadOptions.md#eleris-tiffnet-tiffreadoptions-default) when `null`.
- `cancellationToken` — Stops the read.

**Returns**

The file.

**Exceptions**

- [`TiffException`](Eleris.TiffNet.TiffException.md) — The data is not TIFF, is damaged, or exceeds a limit of `options`.
