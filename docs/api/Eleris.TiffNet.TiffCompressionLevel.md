# TiffCompressionLevel Enum

**Namespace:** `Eleris.TiffNet`

How hard the compressor of a write works (Deflate): faster, or smaller files.

```csharp
public enum TiffCompressionLevel : int
```

## Members

| Name | Value | Description |
| --- | ---: | --- |
| <a id="eleris-tiffnet-tiffcompressionlevel-fastest"></a>`Fastest` | 1 | The fastest compression (zlib level 1): files 15 to 50 % larger, written 2 to 2.5 times faster with Deflate. |
| <a id="eleris-tiffnet-tiffcompressionlevel-optimal"></a>`Optimal` | 2 | The balance of zlib and libtiff (level 6). |
| <a id="eleris-tiffnet-tiffcompressionlevel-smallestsize"></a>`SmallestSize` | 3 | The smallest files (zlib level 9), slower to write; reading is as fast. |
