# Images the library cannot decode

A file can hold an image the library does not decode: a camera raw (photometric interpretation 32803 CFA or 34892 LinearRaw), a
vendor compression (34713 of a Nikon NEF, for example), CMYK, Lab, a compression such as JPEG 2000 or ZSTD, channels of different
depths. Opening the file does not fail: the image is listed, `UnsupportedReason` says why the library cannot deliver its pixels,
and `IsSupported` is `false`. Tools that inspect, convert or build on TIFF (the layer under a raw format, for example) need to know
what such an image still tells; this page is that contract, and each row of the table is covered by a test.

## What stays usable

These members never throw for an image the file can describe, in strict mode or not, whatever the image holds.

| Member | What it gives |
| --- | --- |
| `Width`, `Height` | the size, with the orientation applied |
| `Channels`, `ExtraChannels` | the samples per pixel the file declares: one colour channel for an interpretation the library does not name, then the extra channels |
| `IsSupported`, `UnsupportedReason` | `false` and the reason; `true` and `null` when the pixels can be read |
| `IsReducedResolution`, `SubImages` | what `NewSubfileType` and `SubIFDs` say, each sub-image being described the same way |
| `Metadata` | every tag that is not structural, private ones included: `CFARepeatPatternDim` (33421), `CFAPattern` (33422), `ActiveArea`, `DefaultCropOrigin`, `DefaultCropSize`, `BlackLevel`, `WhiteLevel`, the ICC profile, the description... read with `Metadata[code]` |
| `Storage.Compression` | the compression, or `TiffCompression.Unknown` when the enumeration has no name for the code |
| `Storage.RawCompressionCode` | the code of the file: 34713, for example |
| `Storage.Photometric` | the interpretation, or `TiffPhotometric.Unknown` when the enumeration has no name for the code |
| `Storage.RawPhotometricCode` | the code of the file: 32803, for example |
| `Storage.Layout`, `Storage.RowsPerStrip`, `Storage.TileWidth`, `Storage.TileHeight` | strips or tiles, and their size |
| `Storage.BigEndian`, `Storage.BigTiff`, `Storage.Orientation` | the byte order, the kind of TIFF, the stored orientation |
| `Storage.BitsPerSample` | the stored depth of the first sample: 12 and 14 are values of the file, not errors (a file whose samples have different depths gives the depth of the first) |
| `Storage.Predictor`, `Storage.PlanarConfiguration` | as stored |
| `Blocks`, `ReadBlock`, `TryGetBlockMemory` | the strips or tiles as stored, still compressed, to hand to a decoder of your own |

## What does not

| Member | What happens |
| --- | --- |
| `SampleType`, `ColorModel` | throw a `NotSupportedException` whose message is the reason |
| `ReadPixels`, `ReadRawPixels`, `ReadRegion`, `ReadRawRegion`, `ReadChannel`, `ReadIndices`, and `ReadPixelsAsync`, `ReadRegionAsync`, `ReadChannelAsync` | throw a `NotSupportedException` whose message is the reason, never another exception |

`Width` and `Height` are the only members that can fail on an image: an image wider or taller than `int.MaxValue` has a size an `int`
cannot hold, and they throw a `NotSupportedException` as well. A damaged file (a missing size, strips beyond the end of the file) is
still a `TiffException` when the file is opened: the contract is about images the file describes correctly.

## Codes without a name

`TiffCompression` and `TiffPhotometric` name the codes the library knows. For any other code the property of `Storage` reads
`Unknown`, and the code is in `RawCompressionCode` or `RawPhotometricCode`. For a named code the raw property equals the numeric
value of the member; the only difference is 32946, the first code of Deflate, which reads as `Deflate` and keeps its own code.
`Unknown` is the value -1, which no code of a file can be.

When a tag is missing (read without strict mode), the raw code is the default the library applied: 1 for the compression, grey or RGB for the photometric interpretation. `SetPixels` on such an image makes it supported again (`IsSupported` is `true`, `Blocks` is empty), while `Storage` keeps describing the file. `Storage.Predictor` has no raw code: an unnamed predictor keeps its value as an unnamed cast.

```csharp
using var file = TiffFile.Open(path);
var image = file.Primary;
if (!image.IsSupported)
{
    var storage = image.Storage!;
    Console.WriteLine($"{image.Width} x {image.Height}, {storage.BitsPerSample} bits, " +
        $"compression {storage.RawCompressionCode}, photometric {storage.RawPhotometricCode}: {image.UnsupportedReason}");
    byte[] strip = new byte[image.Blocks[0].ByteCount];
    image.ReadBlock(0, strip);   // the bytes as stored, for a decoder of your own
}
```

`Unknown` is read only: a `TiffWriteOptions` with `Compression = TiffCompression.Unknown` throws an `ArgumentException`
that names the property, before any file is created or changed. `TiffWriteOptions.From(storage)` writes an unnamed compression as
Deflate, like the compressions the library does not write.

Code that compared `Storage.Compression` or `Storage.Photometric` with a numeric cast (`(TiffCompression)34713`) must compare
`RawCompressionCode` or `RawPhotometricCode` instead.
