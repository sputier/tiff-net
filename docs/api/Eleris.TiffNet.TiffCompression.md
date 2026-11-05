# TiffCompression Enum

**Namespace:** `Eleris.TiffNet`

How samples are compressed in a file. The values are the codes of the TIFF `Compression` tag.

```csharp
public enum TiffCompression : int
```

**Remarks**

A file can hold a code this enumeration does not name (a compression the library does not read); it is kept as it is.

## Members

| Name | Value | Description |
| --- | ---: | --- |
| <a id="eleris-tiffnet-tiffcompression-none"></a>`None` | 1 | No compression. |
| <a id="eleris-tiffnet-tiffcompression-lzw"></a>`Lzw` | 5 | LZW (Lempel-Ziv-Welch). |
| <a id="eleris-tiffnet-tiffcompression-jpeg"></a>`Jpeg` | 7 | JPEG, as TIFF Technical Note 2 defines it (read only). |
| <a id="eleris-tiffnet-tiffcompression-deflate"></a>`Deflate` | 8 | Deflate (zlib). |
| <a id="eleris-tiffnet-tiffcompression-packbits"></a>`PackBits` | 32773 | PackBits (run lengths). |
