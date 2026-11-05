# TiffMetadata Class

**Namespace:** `Eleris.TiffNet`

**Inheritance:** `object` → `TiffMetadata`

The tags of an image other than its structure: description, software, date, resolution, ICC profile, XMP, private tags… They are kept as they are when an image is read and saved again.

```csharp
public sealed class TiffMetadata : IEnumerable<TiffTag>
```

**Remarks**

The tags of the structure are not here: the library computes them when it writes (size, samples, photometric interpretation, compression, predictor, strips and tiles, planar configuration, orientation, extra samples, sub-file type, sub-directories, colour map, JPEG and YCbCr tables). Tags that hold offsets into the file (`Ifd` types, `FreeOffsets`) are not kept either: they would point nowhere in another file. The EXIF and GPS sub-directories are read into [`TiffMetadata.Exif`](#eleris-tiffnet-tiffmetadata-exif) and [`TiffMetadata.Gps`](#eleris-tiffnet-tiffmetadata-gps).

Text is read as UTF-8 when it is valid UTF-8 (ASCII is), as Latin-1 otherwise; it is written as UTF-8.

Reading the properties of metadata from several threads is safe; changing it while another thread reads it is not, as for the .NET collections. The byte array properties return a copy on each call.

## Constructors

| Member | Description |
| --- | --- |
| [`TiffMetadata()`](#eleris-tiffnet-tiffmetadata-ctor) | Creates empty metadata. |

## Properties

| Member | Description |
| --- | --- |
| [`Artist`](#eleris-tiffnet-tiffmetadata-artist) | The person who made the image (`Artist`, 315). |
| [`Copyright`](#eleris-tiffnet-tiffmetadata-copyright) | The copyright notice (`Copyright`, 33432). |
| [`Count`](#eleris-tiffnet-tiffmetadata-count) | The number of tags. |
| [`DateTime`](#eleris-tiffnet-tiffmetadata-datetime) | When the image was made (`DateTime`, 306), a local time without zone as TIFF defines it; `null` when absent or not in the format of the specification (the tag itself is still there). |
| [`Description`](#eleris-tiffnet-tiffmetadata-description) | The text that describes the image (`ImageDescription`, 270). |
| [`DocumentName`](#eleris-tiffnet-tiffmetadata-documentname) | The name of the document the image was scanned from (`DocumentName`, 269). |
| [`Exif`](#eleris-tiffnet-tiffmetadata-exif) | The tags of the EXIF sub-directory (34665), read only. |
| [`Gps`](#eleris-tiffnet-tiffmetadata-gps) | The tags of the GPS sub-directory (34853), read only. |
| [`HostComputer`](#eleris-tiffnet-tiffmetadata-hostcomputer) | The computer the image was made on (`HostComputer`, 316). |
| [`IccProfile`](#eleris-tiffnet-tiffmetadata-iccprofile) | The ICC colour profile of the image (34675), as it is. |
| [`Iptc`](#eleris-tiffnet-tiffmetadata-iptc) | The IPTC metadata of the image (33723), as it is. |
| [`PageName`](#eleris-tiffnet-tiffmetadata-pagename) | The name of the page (`PageName`, 285). |
| [`Resolution`](#eleris-tiffnet-tiffmetadata-resolution) | The resolution (`XResolution`, `YResolution`, `ResolutionUnit`; 282, 283, 296); the unit is inches when the file does not say, as TIFF specifies. |
| [`SampleRange`](#eleris-tiffnet-tiffmetadata-samplerange) | The smallest and largest sample values the image holds (`SMinSampleValue`, `SMaxSampleValue`; 340, 341), when the writer recorded them: for example 0 and 1 for normalised floating point data. |
| [`Software`](#eleris-tiffnet-tiffmetadata-software) | The software that wrote the image (`Software`, 305). |
| [`Xmp`](#eleris-tiffnet-tiffmetadata-xmp) | The XMP packet of the image (700), as it is. |
| [`this[int]`](#eleris-tiffnet-tiffmetadata-item-system-int32) | A tag by its code, or `null`. |

## Methods

| Member | Description |
| --- | --- |
| [`GetEnumerator()`](#eleris-tiffnet-tiffmetadata-getenumerator) | The tags, by code. |
| [`Remove(int)`](#eleris-tiffnet-tiffmetadata-remove-system-int32) | Removes a tag. |
| [`Set(TiffTag)`](#eleris-tiffnet-tiffmetadata-set-eleris-tiffnet-tifftag) | Adds a tag, or replaces the tag of the same code. |

## Constructor details

<a id="eleris-tiffnet-tiffmetadata-ctor"></a>
### `TiffMetadata()`

Creates empty metadata.

```csharp
public TiffMetadata()
```

## Property details

<a id="eleris-tiffnet-tiffmetadata-artist"></a>
### `Artist`

The person who made the image (`Artist`, 315).

```csharp
public string? Artist { get; set; }
```

<a id="eleris-tiffnet-tiffmetadata-copyright"></a>
### `Copyright`

The copyright notice (`Copyright`, 33432).

```csharp
public string? Copyright { get; set; }
```

<a id="eleris-tiffnet-tiffmetadata-count"></a>
### `Count`

The number of tags.

```csharp
public int Count { get; }
```

<a id="eleris-tiffnet-tiffmetadata-datetime"></a>
### `DateTime`

When the image was made (`DateTime`, 306), a local time without zone as TIFF defines it; `null` when absent or not in the format of the specification (the tag itself is still there).

```csharp
public DateTime? DateTime { get; set; }
```

<a id="eleris-tiffnet-tiffmetadata-description"></a>
### `Description`

The text that describes the image (`ImageDescription`, 270). Astro-TIFF stores the FITS header here.

```csharp
public string? Description { get; set; }
```

<a id="eleris-tiffnet-tiffmetadata-documentname"></a>
### `DocumentName`

The name of the document the image was scanned from (`DocumentName`, 269).

```csharp
public string? DocumentName { get; set; }
```

<a id="eleris-tiffnet-tiffmetadata-exif"></a>
### `Exif`

The tags of the EXIF sub-directory (34665), read only.

```csharp
public IReadOnlyList<TiffTag> Exif { get; }
```

<a id="eleris-tiffnet-tiffmetadata-gps"></a>
### `Gps`

The tags of the GPS sub-directory (34853), read only.

```csharp
public IReadOnlyList<TiffTag> Gps { get; }
```

<a id="eleris-tiffnet-tiffmetadata-hostcomputer"></a>
### `HostComputer`

The computer the image was made on (`HostComputer`, 316).

```csharp
public string? HostComputer { get; set; }
```

<a id="eleris-tiffnet-tiffmetadata-iccprofile"></a>
### `IccProfile`

The ICC colour profile of the image (34675), as it is.

```csharp
public byte[]? IccProfile { get; set; }
```

<a id="eleris-tiffnet-tiffmetadata-iptc"></a>
### `Iptc`

The IPTC metadata of the image (33723), as it is.

```csharp
public byte[]? Iptc { get; set; }
```

<a id="eleris-tiffnet-tiffmetadata-pagename"></a>
### `PageName`

The name of the page (`PageName`, 285).

```csharp
public string? PageName { get; set; }
```

<a id="eleris-tiffnet-tiffmetadata-resolution"></a>
### `Resolution`

The resolution (`XResolution`, `YResolution`, `ResolutionUnit`; 282, 283, 296); the unit is inches when the file does not say, as TIFF specifies.

```csharp
public TiffResolution? Resolution { get; set; }
```

<a id="eleris-tiffnet-tiffmetadata-samplerange"></a>
### `SampleRange`

The smallest and largest sample values the image holds (`SMinSampleValue`, `SMaxSampleValue`; 340, 341), when the writer recorded them: for example 0 and 1 for normalised floating point data. When the tags give one value per channel, those of the first channel; the tags themselves are in `this[340]` and `this[341]`.

```csharp
public (double, double)? SampleRange { get; set; }
```

<a id="eleris-tiffnet-tiffmetadata-software"></a>
### `Software`

The software that wrote the image (`Software`, 305).

```csharp
public string? Software { get; set; }
```

<a id="eleris-tiffnet-tiffmetadata-xmp"></a>
### `Xmp`

The XMP packet of the image (700), as it is.

```csharp
public byte[]? Xmp { get; set; }
```

<a id="eleris-tiffnet-tiffmetadata-item-system-int32"></a>
### `this[int]`

A tag by its code, or `null`.

```csharp
public TiffTag? this[int code] { get; }
```

**Parameters**

- `code` — The code of the tag.

## Method details

<a id="eleris-tiffnet-tiffmetadata-getenumerator"></a>
### `GetEnumerator()`

The tags, by code.

```csharp
public IEnumerator<TiffTag> GetEnumerator()
```

<a id="eleris-tiffnet-tiffmetadata-remove-system-int32"></a>
### `Remove(int)`

Removes a tag.

```csharp
public bool Remove(int code)
```

**Parameters**

- `code` — The code of the tag.

**Returns**

Whether there was such a tag.

<a id="eleris-tiffnet-tiffmetadata-set-eleris-tiffnet-tifftag"></a>
### `Set(TiffTag)`

Adds a tag, or replaces the tag of the same code.

```csharp
public void Set(TiffTag tag)
```

**Parameters**

- `tag` — The tag.

**Exceptions**

- [`ArgumentException`](https://learn.microsoft.com/dotnet/api/system.argumentexception) — The tag is one the library computes, or holds offsets (see the remarks of the class).
