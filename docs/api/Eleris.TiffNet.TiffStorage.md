# TiffStorage Record

**Namespace:** `Eleris.TiffNet`

**Inheritance:** `object` → `TiffStorage`

How an image is stored in the file it was read from: compression, layout, byte order… The pixels an image delivers do not depend on it (they are always top-down, in the byte order of the machine); it tells what the file holds, and lets an application write a file the same way.

```csharp
public sealed record TiffStorage : IEquatable<TiffStorage>
```

## Properties

| Member | Description |
| --- | --- |
| [`BigEndian`](#eleris-tiffnet-tiffstorage-bigendian) | Whether the file is big endian ("MM"). |
| [`BigTiff`](#eleris-tiffnet-tiffstorage-bigtiff) | Whether the file is BigTIFF (64-bit offsets). |
| [`BitsPerSample`](#eleris-tiffnet-tiffstorage-bitspersample) | The number of bits of a stored sample, 1 to 64 (a 12-bit sample is delivered in 16 bits; this says it had 12). |
| [`Compression`](#eleris-tiffnet-tiffstorage-compression) | The compression of the samples; [`TiffCompression.Unknown`](Eleris.TiffNet.TiffCompression.md#eleris-tiffnet-tiffcompression-unknown) for a code the enumeration does not name (the code is in [`TiffStorage.RawCompressionCode`](#eleris-tiffnet-tiffstorage-rawcompressioncode); see also [`TiffImage.UnsupportedReason`](Eleris.TiffNet.TiffImage.md#eleris-tiffnet-tiffimage-unsupportedreason)). |
| [`Layout`](#eleris-tiffnet-tiffstorage-layout) | Strips or tiles. |
| [`Orientation`](#eleris-tiffnet-tiffstorage-orientation) | The Orientation tag, 1 to 8, as stored. |
| [`Photometric`](#eleris-tiffnet-tiffstorage-photometric) | What the stored samples mean (a palette or MinIsWhite image is delivered as RGB or grey, this says what it was); [`TiffPhotometric.Unknown`](Eleris.TiffNet.TiffPhotometric.md#eleris-tiffnet-tiffphotometric-unknown) for a code the enumeration does not name (the code is in [`TiffStorage.RawPhotometricCode`](#eleris-tiffnet-tiffstorage-rawphotometriccode)). |
| [`PlanarConfiguration`](#eleris-tiffnet-tiffstorage-planarconfiguration) | Whether the channels of a pixel are stored together or each in its own plane. |
| [`Predictor`](#eleris-tiffnet-tiffstorage-predictor) | The prediction applied before compression. |
| [`RawCompressionCode`](#eleris-tiffnet-tiffstorage-rawcompressioncode) | The Compression tag exactly as stored: 34713 for a Nikon NEF, for example. |
| [`RawPhotometricCode`](#eleris-tiffnet-tiffstorage-rawphotometriccode) | The PhotometricInterpretation tag exactly as stored: 32803 for a CFA, for example. |
| [`RowsPerStrip`](#eleris-tiffnet-tiffstorage-rowsperstrip) | The number of rows of a strip (the last one may have fewer), or 0 for tiles. |
| [`TileHeight`](#eleris-tiffnet-tiffstorage-tileheight) | The height of a tile, or 0 for strips. |
| [`TileWidth`](#eleris-tiffnet-tiffstorage-tilewidth) | The width of a tile, or 0 for strips. |

## Property details

<a id="eleris-tiffnet-tiffstorage-bigendian"></a>
### `BigEndian`

Whether the file is big endian ("MM").

```csharp
public bool BigEndian { get; init; }
```

<a id="eleris-tiffnet-tiffstorage-bigtiff"></a>
### `BigTiff`

Whether the file is BigTIFF (64-bit offsets).

```csharp
public bool BigTiff { get; init; }
```

<a id="eleris-tiffnet-tiffstorage-bitspersample"></a>
### `BitsPerSample`

The number of bits of a stored sample, 1 to 64 (a 12-bit sample is delivered in 16 bits; this says it had 12).

```csharp
public int BitsPerSample { get; init; }
```

<a id="eleris-tiffnet-tiffstorage-compression"></a>
### `Compression`

The compression of the samples; [`TiffCompression.Unknown`](Eleris.TiffNet.TiffCompression.md#eleris-tiffnet-tiffcompression-unknown) for a code the enumeration does not name (the code is in [`TiffStorage.RawCompressionCode`](#eleris-tiffnet-tiffstorage-rawcompressioncode); see also [`TiffImage.UnsupportedReason`](Eleris.TiffNet.TiffImage.md#eleris-tiffnet-tiffimage-unsupportedreason)).

```csharp
public TiffCompression Compression { get; init; }
```

<a id="eleris-tiffnet-tiffstorage-layout"></a>
### `Layout`

Strips or tiles.

```csharp
public TiffLayout Layout { get; init; }
```

<a id="eleris-tiffnet-tiffstorage-orientation"></a>
### `Orientation`

The Orientation tag, 1 to 8, as stored. The delivered pixels always have the orientation applied.

```csharp
public int Orientation { get; init; }
```

<a id="eleris-tiffnet-tiffstorage-photometric"></a>
### `Photometric`

What the stored samples mean (a palette or MinIsWhite image is delivered as RGB or grey, this says what it was); [`TiffPhotometric.Unknown`](Eleris.TiffNet.TiffPhotometric.md#eleris-tiffnet-tiffphotometric-unknown) for a code the enumeration does not name (the code is in [`TiffStorage.RawPhotometricCode`](#eleris-tiffnet-tiffstorage-rawphotometriccode)).

```csharp
public TiffPhotometric Photometric { get; init; }
```

<a id="eleris-tiffnet-tiffstorage-planarconfiguration"></a>
### `PlanarConfiguration`

Whether the channels of a pixel are stored together or each in its own plane.

```csharp
public TiffPixelOrder PlanarConfiguration { get; init; }
```

<a id="eleris-tiffnet-tiffstorage-predictor"></a>
### `Predictor`

The prediction applied before compression.

```csharp
public TiffPredictor Predictor { get; init; }
```

<a id="eleris-tiffnet-tiffstorage-rawcompressioncode"></a>
### `RawCompressionCode`

The Compression tag exactly as stored: 34713 for a Nikon NEF, for example. For a named compression it is the numeric value of [`TiffStorage.Compression`](#eleris-tiffnet-tiffstorage-compression), except 32946, the first code of Deflate, which reads as [`TiffCompression.Deflate`](Eleris.TiffNet.TiffCompression.md#eleris-tiffnet-tiffcompression-deflate).

```csharp
public int RawCompressionCode { get; init; }
```

<a id="eleris-tiffnet-tiffstorage-rawphotometriccode"></a>
### `RawPhotometricCode`

The PhotometricInterpretation tag exactly as stored: 32803 for a CFA, for example. For a named value it is the numeric value of [`TiffStorage.Photometric`](#eleris-tiffnet-tiffstorage-photometric).

```csharp
public int RawPhotometricCode { get; init; }
```

<a id="eleris-tiffnet-tiffstorage-rowsperstrip"></a>
### `RowsPerStrip`

The number of rows of a strip (the last one may have fewer), or 0 for tiles.

```csharp
public int RowsPerStrip { get; init; }
```

<a id="eleris-tiffnet-tiffstorage-tileheight"></a>
### `TileHeight`

The height of a tile, or 0 for strips.

```csharp
public int TileHeight { get; init; }
```

<a id="eleris-tiffnet-tiffstorage-tilewidth"></a>
### `TileWidth`

The width of a tile, or 0 for strips.

```csharp
public int TileWidth { get; init; }
```
