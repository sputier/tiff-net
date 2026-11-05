# TiffPredictor Enum

**Namespace:** `Eleris.TiffNet`

The prediction applied to samples before their compression.

```csharp
public enum TiffPredictor : int
```

## Members

| Name | Value | Description |
| --- | ---: | --- |
| <a id="eleris-tiffnet-tiffpredictor-auto"></a>`Auto` | 0 | When writing: horizontal differencing for integers, the floating point predictor for floating point samples. |
| <a id="eleris-tiffnet-tiffpredictor-none"></a>`None` | 1 | No prediction. |
| <a id="eleris-tiffnet-tiffpredictor-horizontal"></a>`Horizontal` | 2 | Horizontal differencing (TIFF 6.0). |
| <a id="eleris-tiffnet-tiffpredictor-floatingpoint"></a>`FloatingPoint` | 3 | Floating point prediction (TIFF Technical Note 3). |
