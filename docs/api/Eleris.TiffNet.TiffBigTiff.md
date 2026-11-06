# TiffBigTiff Enum

**Namespace:** `Eleris.TiffNet`

Whether a file is written as BigTIFF (64-bit offsets, files beyond 4 GB) or as classic TIFF.

```csharp
public enum TiffBigTiff : int
```

## Members

| Name | Value | Description |
| --- | ---: | --- |
| <a id="eleris-tiffnet-tiffbigtiff-auto"></a>`Auto` | 0 | BigTIFF only when the file could exceed what classic TIFF addresses (4 GB); classic otherwise, as most readers expect. |
| <a id="eleris-tiffnet-tiffbigtiff-never"></a>`Never` | 1 | Always classic TIFF; a file that would exceed 4 GB is refused. |
| <a id="eleris-tiffnet-tiffbigtiff-always"></a>`Always` | 2 | Always BigTIFF. |
