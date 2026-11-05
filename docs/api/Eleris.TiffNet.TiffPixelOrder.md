# TiffPixelOrder Enum

**Namespace:** `Eleris.TiffNet`

The order of the samples in an array of pixels.

```csharp
public enum TiffPixelOrder : int
```

## Members

| Name | Value | Description |
| --- | ---: | --- |
| <a id="eleris-tiffnet-tiffpixelorder-interleaved"></a>`Interleaved` | 0 | The channels of a pixel together: `[y][x][c]`. |
| <a id="eleris-tiffnet-tiffpixelorder-planar"></a>`Planar` | 1 | One plane per channel: `[c][y][x]`, the order of a FITS cube. |
