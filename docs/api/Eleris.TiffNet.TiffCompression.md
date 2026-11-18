# TiffCompression Enum

**Namespace:** `Eleris.TiffNet`

How samples are compressed in a file. The values are the codes of the TIFF `Compression` tag.

```csharp
public enum TiffCompression : int
```

**Remarks**

A file can hold a code this enumeration does not name (a vendor compression such as 34713, a compression the library does not read): [`TiffStorage.Compression`](Eleris.TiffNet.TiffStorage.md#eleris-tiffnet-tiffstorage-compression) then reads [`TiffCompression.Unknown`](#eleris-tiffnet-tiffcompression-unknown), and [`TiffStorage.RawCompressionCode`](Eleris.TiffNet.TiffStorage.md#eleris-tiffnet-tiffstorage-rawcompressioncode) holds the code. The numeric values of the named members are the TIFF codes; [`TiffCompression.Unknown`](#eleris-tiffnet-tiffcompression-unknown) is -1, which no code of a file (0 to 65535) can be.

## Members

| Name | Value | Description |
| --- | ---: | --- |
| <a id="eleris-tiffnet-tiffcompression-unknown"></a>`Unknown` | -1 | A code of a file that has no name here. |
| <a id="eleris-tiffnet-tiffcompression-none"></a>`None` | 1 | No compression. |
| <a id="eleris-tiffnet-tiffcompression-lzw"></a>`Lzw` | 5 | LZW (Lempel-Ziv-Welch). |
| <a id="eleris-tiffnet-tiffcompression-jpeg"></a>`Jpeg` | 7 | JPEG, as TIFF Technical Note 2 defines it (read only). |
| <a id="eleris-tiffnet-tiffcompression-deflate"></a>`Deflate` | 8 | Deflate (zlib). |
| <a id="eleris-tiffnet-tiffcompression-packbits"></a>`PackBits` | 32773 | PackBits (run lengths). |
