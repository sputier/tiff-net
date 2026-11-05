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
| [`Height`](#eleris-tiffnet-tiffimage-height) | The height, in pixels. |
| [`IsReducedResolution`](#eleris-tiffnet-tiffimage-isreducedresolution) | Whether the image is a reduced-resolution version of another (a thumbnail, a level of a pyramid). |
| [`Metadata`](#eleris-tiffnet-tiffimage-metadata) | The tags of the image other than its structure: description (the FITS header of an Astro-TIFF file), ICC profile, resolution, private tags… Read with the image and kept when it is saved again. |
| [`SampleType`](#eleris-tiffnet-tiffimage-sampletype) | The type of the samples the image delivers. |
| [`Storage`](#eleris-tiffnet-tiffimage-storage) | How the image is stored in the file it was read from. |
| [`SubImages`](#eleris-tiffnet-tiffimage-subimages) | The images of the sub-directories (`SubIFDs`) of this one, usually reduced-resolution versions of it. |
| [`UnsupportedReason`](#eleris-tiffnet-tiffimage-unsupportedreason) | Why the library cannot deliver this image, or `null` when it can. |
| [`Width`](#eleris-tiffnet-tiffimage-width) | The width, in pixels. |

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

The height, in pixels.

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

The width, in pixels.

```csharp
public int Width { get; }
```

**Exceptions**

- [`NotSupportedException`](https://learn.microsoft.com/dotnet/api/system.notsupportedexception) — The image is wider than an [`Int32`](https://learn.microsoft.com/dotnet/api/system.int32) can count.
