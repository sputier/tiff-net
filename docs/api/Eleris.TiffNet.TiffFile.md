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

## Constructors

| Member | Description |
| --- | --- |
| [`TiffFile()`](#eleris-tiffnet-tifffile-ctor) | Creates an empty file, to build with [`TiffFile.Add`](#eleris-tiffnet-tifffile-add-eleris-tiffnet-tiffimage) and write with `Save`. |

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
| [`Add(TiffImage)`](#eleris-tiffnet-tifffile-add-eleris-tiffnet-tiffimage) | Adds a page at the end of the file. |
| [`Dispose()`](#eleris-tiffnet-tifffile-dispose) | Closes the file opened by [`TiffFile.Open`](#eleris-tiffnet-tifffile-open-system-string-eleris-tiffnet-tiffreadoptions); its pixels can no longer be read. |
| [`GetEnumerator()`](#eleris-tiffnet-tifffile-getenumerator) | Returns an enumerator over the pages. |
| [`Insert(int, TiffImage)`](#eleris-tiffnet-tifffile-insert-system-int32-eleris-tiffnet-tiffimage) | Inserts a page. |
| [`Open(string, TiffReadOptions)`](#eleris-tiffnet-tifffile-open-system-string-eleris-tiffnet-tiffreadoptions) | Opens a file and reads its directories; the pixels are read on demand, until the file is disposed. |
| [`Read(Stream, TiffReadOptions)`](#eleris-tiffnet-tifffile-read-system-io-stream-eleris-tiffnet-tiffreadoptions) | Reads a TIFF stream into memory, from its current position: the stream can be closed afterwards, and need not be able to seek. |
| [`ReadAsync(Stream, TiffReadOptions, CancellationToken)`](#eleris-tiffnet-tifffile-readasync-system-io-stream-eleris-tiffnet-tiffreadoptions-system-threading-cancellationtoken) | Reads a TIFF stream into memory asynchronously, from its current position. |
| [`Remove(TiffImage)`](#eleris-tiffnet-tifffile-remove-eleris-tiffnet-tiffimage) | Removes a page. |
| [`RemoveAt(int)`](#eleris-tiffnet-tifffile-removeat-system-int32) | Removes a page. |
| [`Save(Stream, TiffWriteOptions)`](#eleris-tiffnet-tifffile-save-system-io-stream-eleris-tiffnet-tiffwriteoptions) | Writes the pages into a stream, from its current position; the stream is left open. |
| [`Save(string, TiffWriteOptions)`](#eleris-tiffnet-tifffile-save-system-string-eleris-tiffnet-tiffwriteoptions) | Writes the pages into a file. |
| [`SaveAsync(Stream, TiffWriteOptions, CancellationToken)`](#eleris-tiffnet-tifffile-saveasync-system-io-stream-eleris-tiffnet-tiffwriteoptions-system-threading-cancellationtoken) | Writes the pages into a stream on a thread of the pool (encoding and writing), as [`TiffFile.Save`](#eleris-tiffnet-tifffile-save-system-io-stream-eleris-tiffnet-tiffwriteoptions). |

## Constructor details

<a id="eleris-tiffnet-tifffile-ctor"></a>
### `TiffFile()`

Creates an empty file, to build with [`TiffFile.Add`](#eleris-tiffnet-tifffile-add-eleris-tiffnet-tiffimage) and write with `Save`.

```csharp
public TiffFile()
```

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

**Exceptions**

- [`InvalidOperationException`](https://learn.microsoft.com/dotnet/api/system.invalidoperationexception) — The file has no page.

<a id="eleris-tiffnet-tifffile-item-system-int32"></a>
### `this[int]`

A page, by its zero-based index in the file.

```csharp
public TiffImage this[int index] { get; }
```

**Parameters**

- `index` — The index of the page.

## Method details

<a id="eleris-tiffnet-tifffile-add-eleris-tiffnet-tiffimage"></a>
### `Add(TiffImage)`

Adds a page at the end of the file.

```csharp
public TiffFile Add(TiffImage image)
```

**Parameters**

- `image` — The page: created in memory, or read from this file or another one (which must stay open until saved).

**Returns**

This file, to chain the calls.

**Exceptions**

- [`ArgumentNullException`](https://learn.microsoft.com/dotnet/api/system.argumentnullexception) — `image` is null.

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

<a id="eleris-tiffnet-tifffile-insert-system-int32-eleris-tiffnet-tiffimage"></a>
### `Insert(int, TiffImage)`

Inserts a page.

```csharp
public TiffFile Insert(int index, TiffImage image)
```

**Parameters**

- `index` — Where the page goes, 0 to [`TiffFile.Count`](#eleris-tiffnet-tifffile-count).
- `image` — The page.

**Returns**

This file, to chain the calls.

**Exceptions**

- [`ArgumentNullException`](https://learn.microsoft.com/dotnet/api/system.argumentnullexception) — `image` is null.
- [`ArgumentOutOfRangeException`](https://learn.microsoft.com/dotnet/api/system.argumentoutofrangeexception) — `index` is not within 0 to [`TiffFile.Count`](#eleris-tiffnet-tifffile-count).

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

<a id="eleris-tiffnet-tifffile-remove-eleris-tiffnet-tiffimage"></a>
### `Remove(TiffImage)`

Removes a page.

```csharp
public bool Remove(TiffImage image)
```

**Parameters**

- `image` — The page.

**Returns**

Whether the page was in the file.

<a id="eleris-tiffnet-tifffile-removeat-system-int32"></a>
### `RemoveAt(int)`

Removes a page.

```csharp
public void RemoveAt(int index)
```

**Parameters**

- `index` — The index of the page.

**Exceptions**

- [`ArgumentOutOfRangeException`](https://learn.microsoft.com/dotnet/api/system.argumentoutofrangeexception) — `index` is not the index of a page.

<a id="eleris-tiffnet-tifffile-save-system-io-stream-eleris-tiffnet-tiffwriteoptions"></a>
### `Save(Stream, TiffWriteOptions)`

Writes the pages into a stream, from its current position; the stream is left open. A stream that cannot seek is written through a temporary file, copied in a bounded buffer.

```csharp
public void Save(Stream stream, TiffWriteOptions? options = null)
```

**Parameters**

- `stream` — The stream.
- `options` — How to write; [`TiffWriteOptions.Default`](Eleris.TiffNet.TiffWriteOptions.md#eleris-tiffnet-tiffwriteoptions-default) when `null`.

**Exceptions**

- [`InvalidOperationException`](https://learn.microsoft.com/dotnet/api/system.invalidoperationexception) — The file has no page, or a classic file would exceed 4 GB.
- [`NotSupportedException`](https://learn.microsoft.com/dotnet/api/system.notsupportedexception) — A page cannot be read.
- [`TiffException`](Eleris.TiffNet.TiffException.md) — The pixels of a page read from a file are damaged.
- [`IOException`](https://learn.microsoft.com/dotnet/api/system.io.ioexception) — The stream cannot be written.

<a id="eleris-tiffnet-tifffile-save-system-string-eleris-tiffnet-tiffwriteoptions"></a>
### `Save(string, TiffWriteOptions)`

Writes the pages into a file. A temporary file is written next to it, then replaces it: a failure leaves an existing file as it was; the attributes and permissions of a replaced file are kept where the platform allows. The file this one was read from can be the target.

```csharp
public void Save(string path, TiffWriteOptions? options = null)
```

**Parameters**

- `path` — The path of the file.
- `options` — How to write; [`TiffWriteOptions.Default`](Eleris.TiffNet.TiffWriteOptions.md#eleris-tiffnet-tiffwriteoptions-default) when `null`.

**Exceptions**

- [`InvalidOperationException`](https://learn.microsoft.com/dotnet/api/system.invalidoperationexception) — The file has no page, or a classic file ([`TiffBigTiff.Never`](Eleris.TiffNet.TiffBigTiff.md#eleris-tiffnet-tiffbigtiff-never)) would exceed 4 GB.
- [`NotSupportedException`](https://learn.microsoft.com/dotnet/api/system.notsupportedexception) — A page cannot be read ([`TiffImage.UnsupportedReason`](Eleris.TiffNet.TiffImage.md#eleris-tiffnet-tiffimage-unsupportedreason)).
- [`TiffException`](Eleris.TiffNet.TiffException.md) — The pixels of a page read from a file are damaged.
- [`IOException`](https://learn.microsoft.com/dotnet/api/system.io.ioexception) — The file cannot be written.

<a id="eleris-tiffnet-tifffile-saveasync-system-io-stream-eleris-tiffnet-tiffwriteoptions-system-threading-cancellationtoken"></a>
### `SaveAsync(Stream, TiffWriteOptions, CancellationToken)`

Writes the pages into a stream on a thread of the pool (encoding and writing), as [`TiffFile.Save`](#eleris-tiffnet-tifffile-save-system-io-stream-eleris-tiffnet-tiffwriteoptions). Every failure is reported by the task.

```csharp
public Task SaveAsync(Stream stream, TiffWriteOptions? options = null, CancellationToken cancellationToken = default)
```

**Parameters**

- `stream` — The stream, left open.
- `options` — How to write; [`TiffWriteOptions.Default`](Eleris.TiffNet.TiffWriteOptions.md#eleris-tiffnet-tiffwriteoptions-default) when `null`.
- `cancellationToken` — Stops the write between strips or tiles; what is written of the stream is then incomplete.

**Returns**

The task of the write.

**Exceptions**

- [`OperationCanceledException`](https://learn.microsoft.com/dotnet/api/system.operationcanceledexception) — `cancellationToken` was cancelled.
