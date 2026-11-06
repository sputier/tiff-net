# Large images

An image of more samples than an array holds is read by region (`ReadRegion`), and written piece by piece with
`TiffImageWriter`, without holding it in memory: rows in order (strips) or tiles in any order, as a mosaic computes its panels.
The writer encodes on the cores as pieces come, at most 2 × `MaxDegreeOfParallelism` at once; its memory stays bounded whatever
the size of the image.

<!-- example: LargeImageExamples.Mosaic -->
```csharp
// 1 024 x 768 f32 grey, written tile by tile in any order, without the image in memory (up to 2 147 483 647 pixels a
// side; BigTIFF when the file could exceed 4 GB).
var options = TiffWriteOptions.Default with { Layout = TiffLayout.Tiles, TileSize = 256 };
using (var writer = TiffImageWriter.Create(path, 1024, 768, TiffSampleType.Float32, options: options))
{
    var tile = new float[256 * 256];
    for (int ty = 2; ty >= 0; ty--)
    {
        for (int tx = 0; tx < 4; tx++)
        {
            Array.Fill(tile, tx + 10 * ty);                // the panel computed for this tile
            writer.WriteTile<float>(tx, ty, tile);
        }
    }

    writer.Complete();                                     // refuses missing tiles; without it, nothing is kept
}

using var file = TiffFile.Open(path);
Console.WriteLine($"{file.Primary.ReadRegion<float>(600, 300, 1, 1)[0]}");   // tile (2, 1)
```
<!-- /example -->

- An image is up to 2 147 483 647 pixels a side (TIFF allows 4 294 967 295; the library reads and writes up to 2³¹ − 1). The file is BigTIFF when it could exceed 4 GB (`TiffBigTiff.Auto` decides
  from the geometry, before the first byte is written); a continuous-integration job writes and reads back a file of 4.4 GB.
- `Complete` refuses missing rows or tiles and writes the directory; disposing the writer without it abandons the output (for a
  path, the temporary file is removed and the target left as it was).
