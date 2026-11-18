# TiffPhotometric Enum

**Namespace:** `Eleris.TiffNet`

The photometric interpretation of a file: what its samples mean. The values are the codes of the TIFF tag.

```csharp
public enum TiffPhotometric : int
```

**Remarks**

A file can hold a code this enumeration does not name (CMYK, Lab, 32803 CFA…): [`TiffStorage.Photometric`](Eleris.TiffNet.TiffStorage.md#eleris-tiffnet-tiffstorage-photometric) then reads [`TiffPhotometric.Unknown`](#eleris-tiffnet-tiffphotometric-unknown), and [`TiffStorage.RawPhotometricCode`](Eleris.TiffNet.TiffStorage.md#eleris-tiffnet-tiffstorage-rawphotometriccode) holds the code. The numeric values of the named members are the TIFF codes; [`TiffPhotometric.Unknown`](#eleris-tiffnet-tiffphotometric-unknown) is -1, which no code of a file (0 to 65535) can be.

## Members

| Name | Value | Description |
| --- | ---: | --- |
| <a id="eleris-tiffnet-tiffphotometric-unknown"></a>`Unknown` | -1 | A code of a file that has no name here (the code is in [`TiffStorage.RawPhotometricCode`](Eleris.TiffNet.TiffStorage.md#eleris-tiffnet-tiffstorage-rawphotometriccode)). |
| <a id="eleris-tiffnet-tiffphotometric-miniswhite"></a>`MinIsWhite` | 0 | Grey, 0 being white. |
| <a id="eleris-tiffnet-tiffphotometric-minisblack"></a>`MinIsBlack` | 1 | Grey, 0 being black. |
| <a id="eleris-tiffnet-tiffphotometric-rgb"></a>`Rgb` | 2 | Red, green, blue. |
| <a id="eleris-tiffnet-tiffphotometric-palette"></a>`Palette` | 3 | Indices into a colour map. |
| <a id="eleris-tiffnet-tiffphotometric-ycbcr"></a>`YCbCr` | 6 | Luminance and chrominance (with JPEG compression). |
