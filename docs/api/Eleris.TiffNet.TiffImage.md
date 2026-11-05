# TiffImage Class

**Namespace:** `Eleris.TiffNet`

**Inheritance:** `object` → `TiffImage`

One image of a TIFF file: its geometry, its samples and, on demand, its pixels.

```csharp
public sealed class TiffImage
```

**Remarks**

An image whose format the library does not handle (CMYK, Lab, a compression it does not read…) is still listed with the pages of its file, with its size and the reason in [`TiffImage.UnsupportedReason`](#eleris-tiffnet-tiffimage-unsupportedreason); its [`TiffImage.SampleType`](#eleris-tiffnet-tiffimage-sampletype), [`TiffImage.ColorModel`](#eleris-tiffnet-tiffimage-colormodel) and pixels throw a [`NotSupportedException`](https://learn.microsoft.com/dotnet/api/system.notsupportedexception) that gives the reason.

## Properties

| Member | Description |
| --- | --- |
| [`Channels`](#eleris-tiffnet-tiffimage-channels) | The number of channels: the colour channels (1 for grey, 3 for RGB), then the extra channels. |
| [`ColorModel`](#eleris-tiffnet-tiffimage-colormodel) | What the colour channels are, as the file says: never inferred from the number of channels. |
| [`ExtraChannels`](#eleris-tiffnet-tiffimage-extrachannels) | The channels after the colour channels, with what they hold. |
| [`Height`](#eleris-tiffnet-tiffimage-height) | The height of the delivered image, in pixels, with its orientation applied. |
| [`IsReducedResolution`](#eleris-tiffnet-tiffimage-isreducedresolution) | Whether the image is a reduced-resolution version of another (a thumbnail, a level of a pyramid). |
| [`Metadata`](#eleris-tiffnet-tiffimage-metadata) | The tags of the image other than its structure: description (the FITS header of an Astro-TIFF file), ICC profile, resolution, private tags… Read with the image and kept when it is saved again. |
| [`SampleType`](#eleris-tiffnet-tiffimage-sampletype) | The type of the samples the image delivers. |
| [`Storage`](#eleris-tiffnet-tiffimage-storage) | How the image is stored in the file it was read from. |
| [`SubImages`](#eleris-tiffnet-tiffimage-subimages) | The images of the sub-directories (`SubIFDs`) of this one, usually reduced-resolution versions of it. |
| [`UnsupportedReason`](#eleris-tiffnet-tiffimage-unsupportedreason) | Why the library cannot deliver this image, or `null` when it can. |
| [`Width`](#eleris-tiffnet-tiffimage-width) | The width of the delivered image, in pixels: with its orientation applied (an image stored on its side, Orientation 5 to 8, has the stored height as its width). |

## Methods

| Member | Description |
| --- | --- |
| [`ReadChannel<T>(int, TiffPixelScale)`](#eleris-tiffnet-tiffimage-readchannel-1-system-int32-eleris-tiffnet-tiffpixelscale) | One channel of the image, converted as by [`TiffImage.ReadPixels`](#eleris-tiffnet-tiffimage-readpixels-1-eleris-tiffnet-tiffpixelscale-eleris-tiffnet-tiffpixelorder). |
| [`ReadPixels<T>(TiffPixelScale, TiffPixelOrder)`](#eleris-tiffnet-tiffimage-readpixels-1-eleris-tiffnet-tiffpixelscale-eleris-tiffnet-tiffpixelorder) | The samples, converted to `T`, as stored or normalised. |
| [`ReadPixels<T>(Span<T>, TiffPixelScale, TiffPixelOrder)`](#eleris-tiffnet-tiffimage-readpixels-1-system-span-0-eleris-tiffnet-tiffpixelscale-eleris-tiffnet-tiffpixelorder) | Reads the samples into a buffer of the caller, on the calling thread, without allocating an array for them. |
| [`ReadPixelsAsync<T>(TiffPixelScale, TiffPixelOrder, CancellationToken)`](#eleris-tiffnet-tiffimage-readpixelsasync-1-eleris-tiffnet-tiffpixelscale-eleris-tiffnet-tiffpixelorder-system-threading-cancellationtoken) | Reads the samples on a thread of the pool, as [`TiffImage.ReadPixels`](#eleris-tiffnet-tiffimage-readpixels-1-eleris-tiffnet-tiffpixelscale-eleris-tiffnet-tiffpixelorder). |
| [`ReadRawPixels<T>(TiffPixelOrder)`](#eleris-tiffnet-tiffimage-readrawpixels-1-eleris-tiffnet-tiffpixelorder) | The samples as stored (orientation applied, palette expanded, MinIsWhite inverted), of exactly their type. |
| [`ReadRawRegion<T>(int, int, int, int, TiffPixelOrder)`](#eleris-tiffnet-tiffimage-readrawregion-1-system-int32-system-int32-system-int32-system-int32-eleris-tiffnet-tiffpixelorder) | The samples of a region of the image, of exactly their stored type. |
| [`ReadRegion<T>(int, int, int, int, TiffPixelScale, TiffPixelOrder)`](#eleris-tiffnet-tiffimage-readregion-1-system-int32-system-int32-system-int32-system-int32-eleris-tiffnet-tiffpixelscale-eleris-tiffnet-tiffpixelorder) | The samples of a region of the image (in the delivered orientation), converted as by [`TiffImage.ReadPixels`](#eleris-tiffnet-tiffimage-readpixels-1-eleris-tiffnet-tiffpixelscale-eleris-tiffnet-tiffpixelorder). |

## Property details

<a id="eleris-tiffnet-tiffimage-channels"></a>
### `Channels`

The number of channels: the colour channels (1 for grey, 3 for RGB), then the extra channels.

```csharp
public int Channels { get; }
```

<a id="eleris-tiffnet-tiffimage-colormodel"></a>
### `ColorModel`

What the colour channels are, as the file says: never inferred from the number of channels.

```csharp
public TiffColorModel ColorModel { get; }
```

**Exceptions**

- [`NotSupportedException`](https://learn.microsoft.com/dotnet/api/system.notsupportedexception) — The library does not handle the format of the image ([`TiffImage.UnsupportedReason`](#eleris-tiffnet-tiffimage-unsupportedreason)).

<a id="eleris-tiffnet-tiffimage-extrachannels"></a>
### `ExtraChannels`

The channels after the colour channels, with what they hold.

```csharp
public IReadOnlyList<TiffExtraChannel> ExtraChannels { get; }
```

<a id="eleris-tiffnet-tiffimage-height"></a>
### `Height`

The height of the delivered image, in pixels, with its orientation applied.

```csharp
public int Height { get; }
```

**Exceptions**

- [`NotSupportedException`](https://learn.microsoft.com/dotnet/api/system.notsupportedexception) — The image is taller than an [`Int32`](https://learn.microsoft.com/dotnet/api/system.int32) can count.

<a id="eleris-tiffnet-tiffimage-isreducedresolution"></a>
### `IsReducedResolution`

Whether the image is a reduced-resolution version of another (a thumbnail, a level of a pyramid).

```csharp
public bool IsReducedResolution { get; }
```

<a id="eleris-tiffnet-tiffimage-metadata"></a>
### `Metadata`

The tags of the image other than its structure: description (the FITS header of an Astro-TIFF file), ICC profile, resolution, private tags… Read with the image and kept when it is saved again.

```csharp
public TiffMetadata Metadata { get; }
```

<a id="eleris-tiffnet-tiffimage-sampletype"></a>
### `SampleType`

The type of the samples the image delivers.

```csharp
public TiffSampleType SampleType { get; }
```

**Exceptions**

- [`NotSupportedException`](https://learn.microsoft.com/dotnet/api/system.notsupportedexception) — The library does not handle the format of the image ([`TiffImage.UnsupportedReason`](#eleris-tiffnet-tiffimage-unsupportedreason)).

<a id="eleris-tiffnet-tiffimage-storage"></a>
### `Storage`

How the image is stored in the file it was read from.

```csharp
public TiffStorage? Storage { get; }
```

<a id="eleris-tiffnet-tiffimage-subimages"></a>
### `SubImages`

The images of the sub-directories (`SubIFDs`) of this one, usually reduced-resolution versions of it.

```csharp
public IReadOnlyList<TiffImage> SubImages { get; }
```

<a id="eleris-tiffnet-tiffimage-unsupportedreason"></a>
### `UnsupportedReason`

Why the library cannot deliver this image, or `null` when it can.

```csharp
public string? UnsupportedReason { get; }
```

<a id="eleris-tiffnet-tiffimage-width"></a>
### `Width`

The width of the delivered image, in pixels: with its orientation applied (an image stored on its side, Orientation 5 to 8, has the stored height as its width).

```csharp
public int Width { get; }
```

**Exceptions**

- [`NotSupportedException`](https://learn.microsoft.com/dotnet/api/system.notsupportedexception) — The image is wider than an [`Int32`](https://learn.microsoft.com/dotnet/api/system.int32) can count.

## Method details

<a id="eleris-tiffnet-tiffimage-readchannel-1-system-int32-eleris-tiffnet-tiffpixelscale"></a>
### `ReadChannel<T>(int, TiffPixelScale)`

One channel of the image, converted as by [`TiffImage.ReadPixels`](#eleris-tiffnet-tiffimage-readpixels-1-eleris-tiffnet-tiffpixelscale-eleris-tiffnet-tiffpixelorder).

```csharp
public T[] ReadChannel<T>(int channel, TiffPixelScale scale = TiffPixelScale.Stored) where T : unmanaged, INumber<T>
```

**Type parameters**

- `T` — As for [`TiffImage.ReadPixels`](#eleris-tiffnet-tiffimage-readpixels-1-eleris-tiffnet-tiffpixelscale-eleris-tiffnet-tiffpixelorder).

**Parameters**

- `channel` — The channel: 0 for grey or red, 1 for green…, then the extra channels.
- `scale` — The stored values, or values normalised to \[0, 1\].

**Returns**

A new array of `Width × Height` samples. For an image stored in planes, only the plane of the channel is read.

**Exceptions**

- [`ArgumentOutOfRangeException`](https://learn.microsoft.com/dotnet/api/system.argumentoutofrangeexception) — There is no such channel.
- [`InvalidOperationException`](https://learn.microsoft.com/dotnet/api/system.invalidoperationexception) — The conversion could lose values.
- [`NotSupportedException`](https://learn.microsoft.com/dotnet/api/system.notsupportedexception) — The library does not handle the format of the image.
- [`TiffException`](Eleris.TiffNet.TiffException.md) — The pixel data is damaged.

<a id="eleris-tiffnet-tiffimage-readpixels-1-eleris-tiffnet-tiffpixelscale-eleris-tiffnet-tiffpixelorder"></a>
### `ReadPixels<T>(TiffPixelScale, TiffPixelOrder)`

The samples, converted to `T`, as stored or normalised.

```csharp
public T[] ReadPixels<T>(TiffPixelScale scale = TiffPixelScale.Stored, TiffPixelOrder order = TiffPixelOrder.Interleaved) where T : unmanaged, INumber<T>
```

**Type parameters**

- `T` — [`Single`](https://learn.microsoft.com/dotnet/api/system.single) or [`Double`](https://learn.microsoft.com/dotnet/api/system.double) always; an integer type when every stored value fits in it (the same or a wider type of the same signedness, or an unsigned type into a wider signed one).

**Parameters**

- `scale` — The stored values, or values normalised to \[0, 1\] ([`Single`](https://learn.microsoft.com/dotnet/api/system.single) and [`Double`](https://learn.microsoft.com/dotnet/api/system.double) only).
- `order` — Interleaved (`[y][x][c]`) or planar (`[c][y][x]`).

**Returns**

A new array of `Width × Height × Channels` samples, rows from the top.

**Exceptions**

- [`InvalidOperationException`](https://learn.microsoft.com/dotnet/api/system.invalidoperationexception) — The conversion could lose values, or the image holds more samples than an array can.
- [`NotSupportedException`](https://learn.microsoft.com/dotnet/api/system.notsupportedexception) — The library does not handle the format of the image ([`TiffImage.UnsupportedReason`](#eleris-tiffnet-tiffimage-unsupportedreason)).
- [`TiffException`](Eleris.TiffNet.TiffException.md) — The pixel data is damaged, or exceeds [`TiffReadOptions.MaxPixelBytes`](Eleris.TiffNet.TiffReadOptions.md#eleris-tiffnet-tiffreadoptions-maxpixelbytes).

<a id="eleris-tiffnet-tiffimage-readpixels-1-system-span-0-eleris-tiffnet-tiffpixelscale-eleris-tiffnet-tiffpixelorder"></a>
### `ReadPixels<T>(Span<T>, TiffPixelScale, TiffPixelOrder)`

Reads the samples into a buffer of the caller, on the calling thread, without allocating an array for them.

```csharp
public void ReadPixels<T>(Span<T> destination, TiffPixelScale scale = TiffPixelScale.Stored, TiffPixelOrder order = TiffPixelOrder.Interleaved) where T : unmanaged, INumber<T>
```

**Type parameters**

- `T` — As for [`TiffImage.ReadPixels`](#eleris-tiffnet-tiffimage-readpixels-1-eleris-tiffnet-tiffpixelscale-eleris-tiffnet-tiffpixelorder).

**Parameters**

- `destination` — Exactly `Width × Height × Channels` samples.
- `scale` — The stored values, or values normalised to \[0, 1\].
- `order` — Interleaved or planar.

**Exceptions**

- [`ArgumentException`](https://learn.microsoft.com/dotnet/api/system.argumentexception) — `destination` is not of the size of the image.
- [`InvalidOperationException`](https://learn.microsoft.com/dotnet/api/system.invalidoperationexception) — The conversion could lose values.
- [`NotSupportedException`](https://learn.microsoft.com/dotnet/api/system.notsupportedexception) — The library does not handle the format of the image.
- [`TiffException`](Eleris.TiffNet.TiffException.md) — The pixel data is damaged, or the decoding buffers exceed [`TiffReadOptions.MaxPixelBytes`](Eleris.TiffNet.TiffReadOptions.md#eleris-tiffnet-tiffreadoptions-maxpixelbytes).
- [`ObjectDisposedException`](https://learn.microsoft.com/dotnet/api/system.objectdisposedexception) — The file is disposed.

<a id="eleris-tiffnet-tiffimage-readpixelsasync-1-eleris-tiffnet-tiffpixelscale-eleris-tiffnet-tiffpixelorder-system-threading-cancellationtoken"></a>
### `ReadPixelsAsync<T>(TiffPixelScale, TiffPixelOrder, CancellationToken)`

Reads the samples on a thread of the pool, as [`TiffImage.ReadPixels`](#eleris-tiffnet-tiffimage-readpixels-1-eleris-tiffnet-tiffpixelscale-eleris-tiffnet-tiffpixelorder).

```csharp
public Task<T[]> ReadPixelsAsync<T>(TiffPixelScale scale = TiffPixelScale.Stored, TiffPixelOrder order = TiffPixelOrder.Interleaved, CancellationToken cancellationToken = default) where T : unmanaged, INumber<T>
```

**Type parameters**

- `T` — As for [`TiffImage.ReadPixels`](#eleris-tiffnet-tiffimage-readpixels-1-eleris-tiffnet-tiffpixelscale-eleris-tiffnet-tiffpixelorder).

**Parameters**

- `scale` — The stored values, or values normalised to \[0, 1\].
- `order` — Interleaved or planar.
- `cancellationToken` — Stops the read between strips or tiles.

**Returns**

A new array of `Width × Height × Channels` samples. Every failure, including a type that cannot be read, is reported by the task, never thrown by the call.

**Exceptions**

- [`InvalidOperationException`](https://learn.microsoft.com/dotnet/api/system.invalidoperationexception) — The conversion could lose values, or the image holds more samples than an array can.
- [`NotSupportedException`](https://learn.microsoft.com/dotnet/api/system.notsupportedexception) — The library does not handle the format of the image ([`TiffImage.UnsupportedReason`](#eleris-tiffnet-tiffimage-unsupportedreason)).
- [`TiffException`](Eleris.TiffNet.TiffException.md) — The pixel data is damaged, or exceeds [`TiffReadOptions.MaxPixelBytes`](Eleris.TiffNet.TiffReadOptions.md#eleris-tiffnet-tiffreadoptions-maxpixelbytes).
- [`ObjectDisposedException`](https://learn.microsoft.com/dotnet/api/system.objectdisposedexception) — The file is disposed.
- [`OperationCanceledException`](https://learn.microsoft.com/dotnet/api/system.operationcanceledexception) — `cancellationToken` was cancelled.

<a id="eleris-tiffnet-tiffimage-readrawpixels-1-eleris-tiffnet-tiffpixelorder"></a>
### `ReadRawPixels<T>(TiffPixelOrder)`

The samples as stored (orientation applied, palette expanded, MinIsWhite inverted), of exactly their type.

```csharp
public T[] ReadRawPixels<T>(TiffPixelOrder order = TiffPixelOrder.Interleaved) where T : unmanaged, INumber<T>
```

**Type parameters**

- `T` — The type of [`TiffImage.SampleType`](#eleris-tiffnet-tiffimage-sampletype): [`Byte`](https://learn.microsoft.com/dotnet/api/system.byte) for [`TiffSampleType.UInt8`](Eleris.TiffNet.TiffSampleType.md#eleris-tiffnet-tiffsampletype-uint8), [`Half`](https://learn.microsoft.com/dotnet/api/system.half) for [`TiffSampleType.Float16`](Eleris.TiffNet.TiffSampleType.md#eleris-tiffnet-tiffsampletype-float16)…

**Parameters**

- `order` — Interleaved (`[y][x][c]`) or planar (`[c][y][x]`, the order of a FITS cube).

**Returns**

A new array of `Width × Height × Channels` samples, rows from the top.

**Exceptions**

- [`InvalidOperationException`](https://learn.microsoft.com/dotnet/api/system.invalidoperationexception) — `T` is not the type of the samples, or the image holds more samples than an array can (read it by region).
- [`NotSupportedException`](https://learn.microsoft.com/dotnet/api/system.notsupportedexception) — The library does not handle the format of the image ([`TiffImage.UnsupportedReason`](#eleris-tiffnet-tiffimage-unsupportedreason)).
- [`TiffException`](Eleris.TiffNet.TiffException.md) — The pixel data is damaged, or exceeds [`TiffReadOptions.MaxPixelBytes`](Eleris.TiffNet.TiffReadOptions.md#eleris-tiffnet-tiffreadoptions-maxpixelbytes).

<a id="eleris-tiffnet-tiffimage-readrawregion-1-system-int32-system-int32-system-int32-system-int32-eleris-tiffnet-tiffpixelorder"></a>
### `ReadRawRegion<T>(int, int, int, int, TiffPixelOrder)`

The samples of a region of the image, of exactly their stored type.

```csharp
public T[] ReadRawRegion<T>(int x, int y, int width, int height, TiffPixelOrder order = TiffPixelOrder.Interleaved) where T : unmanaged, INumber<T>
```

**Type parameters**

- `T` — The type of [`TiffImage.SampleType`](#eleris-tiffnet-tiffimage-sampletype).

**Parameters**

- `x` — The first column.
- `y` — The first row, from the top.
- `width` — The number of columns.
- `height` — The number of rows.
- `order` — Interleaved or planar.

**Returns**

A new array of `width × height × Channels` samples.

**Exceptions**

- [`ArgumentOutOfRangeException`](https://learn.microsoft.com/dotnet/api/system.argumentoutofrangeexception) — The region is empty or not within the image.
- [`InvalidOperationException`](https://learn.microsoft.com/dotnet/api/system.invalidoperationexception) — `T` is not the type of the samples.
- [`NotSupportedException`](https://learn.microsoft.com/dotnet/api/system.notsupportedexception) — The library does not handle the format of the image.
- [`TiffException`](Eleris.TiffNet.TiffException.md) — The pixel data is damaged.

<a id="eleris-tiffnet-tiffimage-readregion-1-system-int32-system-int32-system-int32-system-int32-eleris-tiffnet-tiffpixelscale-eleris-tiffnet-tiffpixelorder"></a>
### `ReadRegion<T>(int, int, int, int, TiffPixelScale, TiffPixelOrder)`

The samples of a region of the image (in the delivered orientation), converted as by [`TiffImage.ReadPixels`](#eleris-tiffnet-tiffimage-readpixels-1-eleris-tiffnet-tiffpixelscale-eleris-tiffnet-tiffpixelorder).

```csharp
public T[] ReadRegion<T>(int x, int y, int width, int height, TiffPixelScale scale = TiffPixelScale.Stored, TiffPixelOrder order = TiffPixelOrder.Interleaved) where T : unmanaged, INumber<T>
```

**Type parameters**

- `T` — As for [`TiffImage.ReadPixels`](#eleris-tiffnet-tiffimage-readpixels-1-eleris-tiffnet-tiffpixelscale-eleris-tiffnet-tiffpixelorder).

**Parameters**

- `x` — The first column.
- `y` — The first row, from the top.
- `width` — The number of columns.
- `height` — The number of rows.
- `scale` — The stored values, or values normalised to \[0, 1\].
- `order` — Interleaved or planar.

**Returns**

A new array of `width × height × Channels` samples. Only the strips or tiles under the region are read.

**Exceptions**

- [`ArgumentOutOfRangeException`](https://learn.microsoft.com/dotnet/api/system.argumentoutofrangeexception) — The region is empty or not within the image.
- [`InvalidOperationException`](https://learn.microsoft.com/dotnet/api/system.invalidoperationexception) — The conversion could lose values.
- [`NotSupportedException`](https://learn.microsoft.com/dotnet/api/system.notsupportedexception) — The library does not handle the format of the image.
- [`TiffException`](Eleris.TiffNet.TiffException.md) — The pixel data is damaged.
