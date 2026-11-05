# API reference

Every public type of the library, by namespace. The pages are generated from the XML documentation comments of the code.

## Namespace `Eleris.TiffNet`

| Type | Kind | Description |
| --- | --- | --- |
| [`TiffColorModel`](Eleris.TiffNet.TiffColorModel.md) | enum | What the colour channels of an image are. |
| [`TiffCompression`](Eleris.TiffNet.TiffCompression.md) | enum | How samples are compressed in a file. |
| [`TiffException`](Eleris.TiffNet.TiffException.md) | class | Data that is not valid TIFF: a bad structure, truncated or damaged data, or a limit of the reading options exceeded. |
| [`TiffExtraChannel`](Eleris.TiffNet.TiffExtraChannel.md) | record | An extra channel of an image, after its colour channels. |
| [`TiffExtraChannelKind`](Eleris.TiffNet.TiffExtraChannelKind.md) | enum | What an extra channel (after the colour channels) holds. |
| [`TiffFile`](Eleris.TiffNet.TiffFile.md) | class | A TIFF file: its pages, in the order of the file. |
| [`TiffImage`](Eleris.TiffNet.TiffImage.md) | class | One image of a TIFF file: its geometry, its samples and, on demand, its pixels. |
| [`TiffLayout`](Eleris.TiffNet.TiffLayout.md) | enum | How the samples of an image are divided in a file. |
| [`TiffPhotometric`](Eleris.TiffNet.TiffPhotometric.md) | enum | The photometric interpretation of a file: what its samples mean. |
| [`TiffPixelOrder`](Eleris.TiffNet.TiffPixelOrder.md) | enum | The order of the samples in an array of pixels. |
| [`TiffPredictor`](Eleris.TiffNet.TiffPredictor.md) | enum | The prediction applied to samples before their compression. |
| [`TiffReadOptions`](Eleris.TiffNet.TiffReadOptions.md) | record | How a TIFF file is read: tolerance of defects, and the limits that keep a damaged or hostile file from exhausting the memory or the time of the application. |
| [`TiffSampleType`](Eleris.TiffNet.TiffSampleType.md) | enum | The type of the samples an image delivers. |
| [`TiffTagType`](Eleris.TiffNet.TiffTagType.md) | enum | The field types of TIFF 6.0 and BigTIFF: how the values of a tag are stored. |
