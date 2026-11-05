# TiffReadOptions Record

**Namespace:** `Eleris.TiffNet`

**Inheritance:** `object` → `TiffReadOptions`

How a TIFF file is read: tolerance of defects, and the limits that keep a damaged or hostile file from exhausting the memory or the time of the application.

```csharp
public sealed record TiffReadOptions : IEquatable<TiffReadOptions>
```

**Remarks**

The defaults suit files that you trust. For files you do not control, set the limits to what your application needs: a file that would exceed one fails with a [`TiffException`](Eleris.TiffNet.TiffException.md) whose [`TiffException.Limit`](Eleris.TiffNet.TiffException.md#eleris-tiffnet-tiffexception-limit) names it, before the memory is allocated. On a phone, a [`TiffReadOptions.MaxPixelBytes`](#eleris-tiffnet-tiffreadoptions-maxpixelbytes) of 256 MB is a reasonable start.

Change a limit with a `with` expression: `TiffReadOptions.Default with { MaxPixelBytes = 256L << 20 }`.

## Constructors

| Member | Description |
| --- | --- |
| [`TiffReadOptions()`](#eleris-tiffnet-tiffreadoptions-ctor) | Creates options with the default limits; set the ones to change with an object initializer. |

## Properties

| Member | Description |
| --- | --- |
| [`Default`](#eleris-tiffnet-tiffreadoptions-default) | The default options: tolerant reading with the default limits. |
| [`MaxDegreeOfParallelism`](#eleris-tiffnet-tiffreadoptions-maxdegreeofparallelism) | The number of threads that decode strips and tiles at once: -1 (the default) for all the cores, 1 to decode on the calling thread only. |
| [`MaxDirectories`](#eleris-tiffnet-tiffreadoptions-maxdirectories) | The number of directories read from a file: pages and sub-directories together. 10 000 by default. |
| [`MaxInputBytes`](#eleris-tiffnet-tiffreadoptions-maxinputbytes) | The number of bytes that `TiffFile.Read` takes from a stream, which it holds in memory. 2 147 483 591 (the largest .NET array) by default. |
| [`MaxJpegScans`](#eleris-tiffnet-tiffreadoptions-maxjpegscans) | The number of scans of a progressive JPEG image. 100 by default; a normal image has about 10. |
| [`MaxPixelBytes`](#eleris-tiffnet-tiffreadoptions-maxpixelbytes) | The memory that decoding an image or a region may use for the data the file asks for: the pixels and the intermediate buffers of the codecs (for example the coefficients of a progressive JPEG). 2 147 483 591 bytes by default. |
| [`MaxTagBytes`](#eleris-tiffnet-tiffreadoptions-maxtagbytes) | The size of the values of one tag. 64 MB by default, which ICC profiles and XMP packets fit in. |
| [`MaxTagsPerDirectory`](#eleris-tiffnet-tiffreadoptions-maxtagsperdirectory) | The number of entries of one directory. 4 096 by default; real files have a few dozen. |
| [`Strict`](#eleris-tiffnet-tiffreadoptions-strict) | Refuses the defects that are tolerated by default (directory entries out of order, duplicate tags, tiles whose size is not a multiple of 16, a tag whose values do not fit in the file) with a [`TiffException`](Eleris.TiffNet.TiffException.md). |

## Constructor details

<a id="eleris-tiffnet-tiffreadoptions-ctor"></a>
### `TiffReadOptions()`

Creates options with the default limits; set the ones to change with an object initializer.

```csharp
public TiffReadOptions()
```

## Property details

<a id="eleris-tiffnet-tiffreadoptions-default"></a>
### `Default`

The default options: tolerant reading with the default limits.

```csharp
public static TiffReadOptions Default { get; }
```

<a id="eleris-tiffnet-tiffreadoptions-maxdegreeofparallelism"></a>
### `MaxDegreeOfParallelism`

The number of threads that decode strips and tiles at once: -1 (the default) for all the cores, 1 to decode on the calling thread only.

```csharp
public int MaxDegreeOfParallelism { get; init; }
```

**Exceptions**

- [`ArgumentOutOfRangeException`](https://learn.microsoft.com/dotnet/api/system.argumentoutofrangeexception) — The value is 0 or less than -1.

<a id="eleris-tiffnet-tiffreadoptions-maxdirectories"></a>
### `MaxDirectories`

The number of directories read from a file: pages and sub-directories together. 10 000 by default.

```csharp
public int MaxDirectories { get; init; }
```

**Exceptions**

- [`ArgumentOutOfRangeException`](https://learn.microsoft.com/dotnet/api/system.argumentoutofrangeexception) — The value is not positive.

<a id="eleris-tiffnet-tiffreadoptions-maxinputbytes"></a>
### `MaxInputBytes`

The number of bytes that `TiffFile.Read` takes from a stream, which it holds in memory. 2 147 483 591 (the largest .NET array) by default.

```csharp
public long MaxInputBytes { get; init; }
```

**Exceptions**

- [`ArgumentOutOfRangeException`](https://learn.microsoft.com/dotnet/api/system.argumentoutofrangeexception) — The value is not positive.

<a id="eleris-tiffnet-tiffreadoptions-maxjpegscans"></a>
### `MaxJpegScans`

The number of scans of a progressive JPEG image. 100 by default; a normal image has about 10.

```csharp
public int MaxJpegScans { get; init; }
```

**Exceptions**

- [`ArgumentOutOfRangeException`](https://learn.microsoft.com/dotnet/api/system.argumentoutofrangeexception) — The value is not positive.

<a id="eleris-tiffnet-tiffreadoptions-maxpixelbytes"></a>
### `MaxPixelBytes`

The memory that decoding an image or a region may use for the data the file asks for: the pixels and the intermediate buffers of the codecs (for example the coefficients of a progressive JPEG). 2 147 483 591 bytes by default.

```csharp
public long MaxPixelBytes { get; init; }
```

**Exceptions**

- [`ArgumentOutOfRangeException`](https://learn.microsoft.com/dotnet/api/system.argumentoutofrangeexception) — The value is not positive.

<a id="eleris-tiffnet-tiffreadoptions-maxtagbytes"></a>
### `MaxTagBytes`

The size of the values of one tag. 64 MB by default, which ICC profiles and XMP packets fit in.

```csharp
public long MaxTagBytes { get; init; }
```

**Exceptions**

- [`ArgumentOutOfRangeException`](https://learn.microsoft.com/dotnet/api/system.argumentoutofrangeexception) — The value is not positive.

<a id="eleris-tiffnet-tiffreadoptions-maxtagsperdirectory"></a>
### `MaxTagsPerDirectory`

The number of entries of one directory. 4 096 by default; real files have a few dozen.

```csharp
public int MaxTagsPerDirectory { get; init; }
```

**Exceptions**

- [`ArgumentOutOfRangeException`](https://learn.microsoft.com/dotnet/api/system.argumentoutofrangeexception) — The value is not positive.

<a id="eleris-tiffnet-tiffreadoptions-strict"></a>
### `Strict`

Refuses the defects that are tolerated by default (directory entries out of order, duplicate tags, tiles whose size is not a multiple of 16, a tag whose values do not fit in the file) with a [`TiffException`](Eleris.TiffNet.TiffException.md).

```csharp
public bool Strict { get; init; }
```
