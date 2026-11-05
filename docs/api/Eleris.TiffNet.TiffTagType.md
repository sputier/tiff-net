# TiffTagType Enum

**Namespace:** `Eleris.TiffNet`

The field types of TIFF 6.0 and BigTIFF: how the values of a tag are stored.

```csharp
public enum TiffTagType : int
```

## Members

| Name | Value | Description |
| --- | ---: | --- |
| <a id="eleris-tiffnet-tifftagtype-byte"></a>`Byte` | 1 | 8-bit unsigned integers. |
| <a id="eleris-tiffnet-tifftagtype-ascii"></a>`Ascii` | 2 | 8-bit characters; a string ends with a NUL byte, and a tag may hold several strings. |
| <a id="eleris-tiffnet-tifftagtype-short"></a>`Short` | 3 | 16-bit unsigned integers. |
| <a id="eleris-tiffnet-tifftagtype-long"></a>`Long` | 4 | 32-bit unsigned integers. |
| <a id="eleris-tiffnet-tifftagtype-rational"></a>`Rational` | 5 | Two 32-bit unsigned integers: a numerator and a denominator. |
| <a id="eleris-tiffnet-tifftagtype-sbyte"></a>`SByte` | 6 | 8-bit signed integers. |
| <a id="eleris-tiffnet-tifftagtype-undefined"></a>`Undefined` | 7 | Bytes whose meaning depends on the tag. |
| <a id="eleris-tiffnet-tifftagtype-sshort"></a>`SShort` | 8 | 16-bit signed integers. |
| <a id="eleris-tiffnet-tifftagtype-slong"></a>`SLong` | 9 | 32-bit signed integers. |
| <a id="eleris-tiffnet-tifftagtype-srational"></a>`SRational` | 10 | Two 32-bit signed integers: a numerator and a denominator. |
| <a id="eleris-tiffnet-tifftagtype-float"></a>`Float` | 11 | IEEE single precision floating point numbers. |
| <a id="eleris-tiffnet-tifftagtype-double"></a>`Double` | 12 | IEEE double precision floating point numbers. |
| <a id="eleris-tiffnet-tifftagtype-ifd"></a>`Ifd` | 13 | 32-bit offsets of sub-directories. |
| <a id="eleris-tiffnet-tifftagtype-long8"></a>`Long8` | 16 | 64-bit unsigned integers (BigTIFF). |
| <a id="eleris-tiffnet-tifftagtype-slong8"></a>`SLong8` | 17 | 64-bit signed integers (BigTIFF). |
| <a id="eleris-tiffnet-tifftagtype-ifd8"></a>`Ifd8` | 18 | 64-bit offsets of sub-directories (BigTIFF). |
