# TiffPhotometric Enum

**Namespace:** `Eleris.TiffNet`

The photometric interpretation of a file: what its samples mean. The values are the codes of the TIFF tag.

```csharp
public enum TiffPhotometric : int
```

**Remarks**

A file can hold a code this enumeration does not name (CMYK, Lab…); it is kept as it is.

## Members

| Name | Value | Description |
| --- | ---: | --- |
| <a id="eleris-tiffnet-tiffphotometric-miniswhite"></a>`MinIsWhite` | 0 | Grey, 0 being white. |
| <a id="eleris-tiffnet-tiffphotometric-minisblack"></a>`MinIsBlack` | 1 | Grey, 0 being black. |
| <a id="eleris-tiffnet-tiffphotometric-rgb"></a>`Rgb` | 2 | Red, green, blue. |
| <a id="eleris-tiffnet-tiffphotometric-palette"></a>`Palette` | 3 | Indices into a colour map. |
| <a id="eleris-tiffnet-tiffphotometric-ycbcr"></a>`YCbCr` | 6 | Luminance and chrominance (with JPEG compression). |
