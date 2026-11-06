# Eleris.TiffNet

A pure managed .NET library to read and write TIFF images, made for astronomy and scientific data: 8 to 64-bit integer and
floating point samples, grey or RGB, the metadata astronomy software stores in TIFF (Astro-TIFF FITS headers, ICC profiles),
multi-core encoding and decoding, and bounded resources on hostile files. net10.0 on Windows, Linux, NativeAOT and .NET MAUI;
no dependency. The sister library of Eleris.FitsNet.

```csharp
using Eleris.TiffNet;

// Write 16-bit grey pixels (Deflate with a predictor, by default), then read them back as floating point.
ushort[] pixels = [0, 1000, 2000, 3000, 4000, 65535];
new TiffFile().Add(TiffImage.Create<ushort>(pixels, width: 3, height: 2)).Save("image.tif");

using var file = TiffFile.Open("image.tif");
float[] values = file.Primary.ReadPixels<float>();
```

- **Reads** classic TIFF and BigTIFF, strips and tiles, interleaved and planar, none, PackBits, LZW, Deflate and JPEG
  (baseline and progressive), samples of 1 to 64 bits, regions and channels, with every orientation applied.
- **Writes** none, PackBits, LZW and Deflate with predictors, strips or tiles, BigTIFF when needed, and images larger than an
  array piece by piece (`TiffImageWriter`).
- **Safe on untrusted files**: every allocation a file asks for is bounded by `TiffReadOptions`.

The guide, the API reference and the coverage of the standard are in the
[repository](https://github.com/sputier/tiff-net/tree/main/docs).
