# TiffSampleType Enum

**Namespace:** `Eleris.TiffNet`

The type of the samples an image delivers.

```csharp
public enum TiffSampleType : int
```

**Remarks**

Samples of 1 to 7 bits are delivered as [`TiffSampleType.UInt8`](#eleris-tiffnet-tiffsampletype-uint8), of 9 to 15 bits as [`TiffSampleType.UInt16`](#eleris-tiffnet-tiffsampletype-uint16), values unchanged.

## Members

| Name | Value | Description |
| --- | ---: | --- |
| <a id="eleris-tiffnet-tiffsampletype-uint8"></a>`UInt8` | 1 | Unsigned 8-bit integers ([`Byte`](https://learn.microsoft.com/dotnet/api/system.byte)). |
| <a id="eleris-tiffnet-tiffsampletype-int8"></a>`Int8` | 2 | Signed 8-bit integers ([`SByte`](https://learn.microsoft.com/dotnet/api/system.sbyte)). |
| <a id="eleris-tiffnet-tiffsampletype-uint16"></a>`UInt16` | 3 | Unsigned 16-bit integers ([`UInt16`](https://learn.microsoft.com/dotnet/api/system.uint16)), the usual type of camera frames. |
| <a id="eleris-tiffnet-tiffsampletype-int16"></a>`Int16` | 4 | Signed 16-bit integers ([`Int16`](https://learn.microsoft.com/dotnet/api/system.int16)). |
| <a id="eleris-tiffnet-tiffsampletype-uint32"></a>`UInt32` | 5 | Unsigned 32-bit integers ([`UInt32`](https://learn.microsoft.com/dotnet/api/system.uint32)). |
| <a id="eleris-tiffnet-tiffsampletype-int32"></a>`Int32` | 6 | Signed 32-bit integers ([`Int32`](https://learn.microsoft.com/dotnet/api/system.int32)). |
| <a id="eleris-tiffnet-tiffsampletype-uint64"></a>`UInt64` | 7 | Unsigned 64-bit integers ([`UInt64`](https://learn.microsoft.com/dotnet/api/system.uint64)). |
| <a id="eleris-tiffnet-tiffsampletype-int64"></a>`Int64` | 8 | Signed 64-bit integers ([`Int64`](https://learn.microsoft.com/dotnet/api/system.int64)). |
| <a id="eleris-tiffnet-tiffsampletype-float16"></a>`Float16` | 9 | IEEE half precision floating point numbers ([`Half`](https://learn.microsoft.com/dotnet/api/system.half)). |
| <a id="eleris-tiffnet-tiffsampletype-float32"></a>`Float32` | 10 | IEEE single precision floating point numbers ([`Single`](https://learn.microsoft.com/dotnet/api/system.single)), the usual type of stacked images. |
| <a id="eleris-tiffnet-tiffsampletype-float64"></a>`Float64` | 11 | IEEE double precision floating point numbers ([`Double`](https://learn.microsoft.com/dotnet/api/system.double)). |
