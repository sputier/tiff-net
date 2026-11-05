# TiffExtraChannelKind Enum

**Namespace:** `Eleris.TiffNet`

What an extra channel (after the colour channels) holds.

```csharp
public enum TiffExtraChannelKind : int
```

## Members

| Name | Value | Description |
| --- | ---: | --- |
| <a id="eleris-tiffnet-tiffextrachannelkind-data"></a>`Data` | 0 | Data with no meaning for the TIFF format: a band, a mask, a weight. |
| <a id="eleris-tiffnet-tiffextrachannelkind-associatedalpha"></a>`AssociatedAlpha` | 1 | Opacity, by which the colour channels are already multiplied (premultiplied alpha). |
| <a id="eleris-tiffnet-tiffextrachannelkind-unassociatedalpha"></a>`UnassociatedAlpha` | 2 | Opacity, independent of the colour channels. |
