# TiffPixelScale Enum

**Namespace:** `Eleris.TiffNet`

How [`TiffImage.ReadPixels`](Eleris.TiffNet.TiffImage.md#eleris-tiffnet-tiffimage-readpixels-1-eleris-tiffnet-tiffpixelscale-eleris-tiffnet-tiffpixelorder) scales the values it returns.

```csharp
public enum TiffPixelScale : int
```

## Members

| Name | Value | Description |
| --- | ---: | --- |
| <a id="eleris-tiffnet-tiffpixelscale-stored"></a>`Stored` | 0 | The values as stored, converted to the requested type. |
| <a id="eleris-tiffnet-tiffpixelscale-normalized"></a>`Normalized` | 1 | Floating point values in \[0, 1\]: unsigned integers divided by the largest value of their stored depth (255, 4095 for 12 bits, 65535…), signed integers by the largest value of their type (to \[-1, 1\]); floating point values unchanged. |
