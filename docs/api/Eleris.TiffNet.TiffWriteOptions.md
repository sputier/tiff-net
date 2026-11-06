# TiffWriteOptions Record

**Namespace:** `Eleris.TiffNet`

**Inheritance:** `object` → `TiffWriteOptions`

How a TIFF file is written: compression, prediction, layout, BigTIFF, threads.

```csharp
public sealed record TiffWriteOptions : IEquatable<TiffWriteOptions>
```

**Remarks**

The defaults ([`TiffWriteOptions.Default`](#eleris-tiffnet-tiffwriteoptions-default)) write Deflate with the predictor that suits the samples, in strips, as classic TIFF unless the file needs BigTIFF: lossless, compact, and readable by every TIFF reader. JPEG is never written: it is lossy, and a TIFF meant for processing should not be.

Change an option with a `with` expression: `TiffWriteOptions.Default with { Layout = TiffLayout.Tiles }`.

## Constructors

| Member | Description |
| --- | --- |
| [`TiffWriteOptions()`](#eleris-tiffnet-tiffwriteoptions-ctor) | Creates the default options; set the ones to change with an object initializer. |

## Properties

| Member | Description |
| --- | --- |
| [`BigTiff`](#eleris-tiffnet-tiffwriteoptions-bigtiff) | Classic TIFF or BigTIFF: [`TiffBigTiff.Auto`](Eleris.TiffNet.TiffBigTiff.md#eleris-tiffnet-tiffbigtiff-auto) (the default) writes BigTIFF only when the file could exceed 4 GB. |
| [`Compression`](#eleris-tiffnet-tiffwriteoptions-compression) | The compression: [`TiffCompression.Deflate`](Eleris.TiffNet.TiffCompression.md#eleris-tiffnet-tiffcompression-deflate) by default; None, LZW and PackBits too. |
| [`Default`](#eleris-tiffnet-tiffwriteoptions-default) | The default options: Deflate, the predictor that suits the samples, strips, classic TIFF unless BigTIFF is needed. |
| [`Layout`](#eleris-tiffnet-tiffwriteoptions-layout) | Strips of whole rows (about 256 KB each, the default) or square tiles of [`TiffWriteOptions.TileSize`](#eleris-tiffnet-tiffwriteoptions-tilesize). |
| [`Level`](#eleris-tiffnet-tiffwriteoptions-level) | How hard Deflate works: [`TiffCompressionLevel.Optimal`](Eleris.TiffNet.TiffCompressionLevel.md#eleris-tiffnet-tiffcompressionlevel-optimal) by default. |
| [`MaxDegreeOfParallelism`](#eleris-tiffnet-tiffwriteoptions-maxdegreeofparallelism) | The threads that encode strips or tiles at once: -1 (the default) for every core, 1 to write on the calling thread only. |
| [`PlanarConfiguration`](#eleris-tiffnet-tiffwriteoptions-planarconfiguration) | The samples of a pixel together ([`TiffPixelOrder.Interleaved`](Eleris.TiffNet.TiffPixelOrder.md#eleris-tiffnet-tiffpixelorder-interleaved), the default) or each channel in its own plane. |
| [`Predictor`](#eleris-tiffnet-tiffwriteoptions-predictor) | The predictor, with LZW and Deflate: [`TiffPredictor.Auto`](Eleris.TiffNet.TiffPredictor.md#eleris-tiffnet-tiffpredictor-auto) (the default) chooses horizontal differencing for integers of 8 to 32 bits, the floating point predictor for floating point samples, and none for 64-bit integers (where it gains nothing). |
| [`TileSize`](#eleris-tiffnet-tiffwriteoptions-tilesize) | The width and height of the tiles, a multiple of 16 (TIFF 6.0): 256 by default. |

## Constructor details

<a id="eleris-tiffnet-tiffwriteoptions-ctor"></a>
### `TiffWriteOptions()`

Creates the default options; set the ones to change with an object initializer.

```csharp
public TiffWriteOptions()
```

## Property details

<a id="eleris-tiffnet-tiffwriteoptions-bigtiff"></a>
### `BigTiff`

Classic TIFF or BigTIFF: [`TiffBigTiff.Auto`](Eleris.TiffNet.TiffBigTiff.md#eleris-tiffnet-tiffbigtiff-auto) (the default) writes BigTIFF only when the file could exceed 4 GB.

```csharp
public TiffBigTiff BigTiff { get; init; }
```

**Exceptions**

- [`ArgumentOutOfRangeException`](https://learn.microsoft.com/dotnet/api/system.argumentoutofrangeexception) — The value is not a choice of the enumeration.

<a id="eleris-tiffnet-tiffwriteoptions-compression"></a>
### `Compression`

The compression: [`TiffCompression.Deflate`](Eleris.TiffNet.TiffCompression.md#eleris-tiffnet-tiffcompression-deflate) by default; None, LZW and PackBits too.

```csharp
public TiffCompression Compression { get; init; }
```

**Exceptions**

- [`NotSupportedException`](https://learn.microsoft.com/dotnet/api/system.notsupportedexception) — The value is [`TiffCompression.Jpeg`](Eleris.TiffNet.TiffCompression.md#eleris-tiffnet-tiffcompression-jpeg): JPEG is read, not written.
- [`ArgumentOutOfRangeException`](https://learn.microsoft.com/dotnet/api/system.argumentoutofrangeexception) — The value is not a compression of the enumeration.

<a id="eleris-tiffnet-tiffwriteoptions-default"></a>
### `Default`

The default options: Deflate, the predictor that suits the samples, strips, classic TIFF unless BigTIFF is needed.

```csharp
public static TiffWriteOptions Default { get; }
```

<a id="eleris-tiffnet-tiffwriteoptions-layout"></a>
### `Layout`

Strips of whole rows (about 256 KB each, the default) or square tiles of [`TiffWriteOptions.TileSize`](#eleris-tiffnet-tiffwriteoptions-tilesize).

```csharp
public TiffLayout Layout { get; init; }
```

**Exceptions**

- [`ArgumentOutOfRangeException`](https://learn.microsoft.com/dotnet/api/system.argumentoutofrangeexception) — The value is not a layout of the enumeration.

<a id="eleris-tiffnet-tiffwriteoptions-level"></a>
### `Level`

How hard Deflate works: [`TiffCompressionLevel.Optimal`](Eleris.TiffNet.TiffCompressionLevel.md#eleris-tiffnet-tiffcompressionlevel-optimal) by default.

```csharp
public TiffCompressionLevel Level { get; init; }
```

**Exceptions**

- [`ArgumentOutOfRangeException`](https://learn.microsoft.com/dotnet/api/system.argumentoutofrangeexception) — The value is not a level of the enumeration.

<a id="eleris-tiffnet-tiffwriteoptions-maxdegreeofparallelism"></a>
### `MaxDegreeOfParallelism`

The threads that encode strips or tiles at once: -1 (the default) for every core, 1 to write on the calling thread only.

```csharp
public int MaxDegreeOfParallelism { get; init; }
```

**Exceptions**

- [`ArgumentOutOfRangeException`](https://learn.microsoft.com/dotnet/api/system.argumentoutofrangeexception) — The value is neither -1 nor positive.

<a id="eleris-tiffnet-tiffwriteoptions-planarconfiguration"></a>
### `PlanarConfiguration`

The samples of a pixel together ([`TiffPixelOrder.Interleaved`](Eleris.TiffNet.TiffPixelOrder.md#eleris-tiffnet-tiffpixelorder-interleaved), the default) or each channel in its own plane.

```csharp
public TiffPixelOrder PlanarConfiguration { get; init; }
```

**Exceptions**

- [`ArgumentOutOfRangeException`](https://learn.microsoft.com/dotnet/api/system.argumentoutofrangeexception) — The value is not an order of the enumeration.

<a id="eleris-tiffnet-tiffwriteoptions-predictor"></a>
### `Predictor`

The predictor, with LZW and Deflate: [`TiffPredictor.Auto`](Eleris.TiffNet.TiffPredictor.md#eleris-tiffnet-tiffpredictor-auto) (the default) chooses horizontal differencing for integers of 8 to 32 bits, the floating point predictor for floating point samples, and none for 64-bit integers (where it gains nothing). With no compression or PackBits, no predictor is written.

```csharp
public TiffPredictor Predictor { get; init; }
```

**Exceptions**

- [`ArgumentOutOfRangeException`](https://learn.microsoft.com/dotnet/api/system.argumentoutofrangeexception) — The value is not a predictor of the enumeration.

<a id="eleris-tiffnet-tiffwriteoptions-tilesize"></a>
### `TileSize`

The width and height of the tiles, a multiple of 16 (TIFF 6.0): 256 by default.

```csharp
public int TileSize { get; init; }
```

**Exceptions**

- [`ArgumentOutOfRangeException`](https://learn.microsoft.com/dotnet/api/system.argumentoutofrangeexception) — The value is not a positive multiple of 16.
