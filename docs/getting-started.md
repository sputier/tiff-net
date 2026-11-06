# Getting started

Eleris.TiffNet reads and writes TIFF images in pure managed code: grey and RGB images of 8 to 64-bit integer and floating
point samples, with their metadata, on Windows, Linux and .NET MAUI (net10.0). It has no dependency.

```text
dotnet add package Eleris.TiffNet
```

A file is a list of pages (`TiffFile`), each an image (`TiffImage`) with its pixels and its metadata. Opening a file reads its
directories; the pixels are read when they are asked for. Writing takes images, from a file or made in memory.

<!-- example: GettingStartedExamples.FirstImage -->
```csharp
// 3 x 2 grey pixels of 16 bits, rows from the top.
ushort[] pixels = [0, 1000, 2000, 3000, 4000, 65535];
var image = TiffImage.Create<ushort>(pixels, width: 3, height: 2);
new TiffFile().Add(image).Save(path);   // Deflate with a predictor, by default

using var file = TiffFile.Open(path);
float[] values = file.Primary.ReadPixels<float>();
Console.WriteLine($"{file.Primary.Width} x {file.Primary.Height}, {file.Primary.SampleType}: {string.Join(", ", values)}");
```
<!-- /example -->

The next pages go through each part: [files and pages](files.md), [reading pixels](pixels.md),
[metadata and Astro-TIFF](metadata.md), [writing](writing.md), [large images](large-images.md),
[threads and memory](threads.md), and [reliability and limits](reliability.md) for files that come from anywhere.
