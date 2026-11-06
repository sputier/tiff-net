using System.Diagnostics;

namespace Eleris.TiffNet.Tests.Writing;

/// <summary>
/// A file beyond 4 GB, written with TiffImageWriter and read back by region: BigTIFF from the geometry, offsets beyond 32 bits.
/// It takes minutes and 4.3 GB of disk: it runs when <c>TIFFNET_HUGE=1</c>, in a dedicated CI job (huge.yml).
/// </summary>
[Trait("Category", "Huge")]
public class HugeFileTests(Xunit.Abstractions.ITestOutputHelper output)
{
    public const string Variable = "TIFFNET_HUGE";

    /// <summary>46 400 x 46 400 u16 samples: 4.31 GB of pixels, written uncompressed in tiles of 1024.</summary>
    private const int Side = 46_400, Tile = 1024;

    /// <summary>The sample at (x, y): a function of the position, so that any region can be checked without the image.</summary>
    private static ushort Sample(long x, long y) => (ushort)((x * 2654435761L) ^ (y * 40503L) ^ (x >> 7) ^ (y << 3));

    [SkippableFact]
    public void A_file_beyond_4_GB_is_written_and_read_back_by_region()
    {
        Skip.If(Environment.GetEnvironmentVariable(Variable) != "1", $"Writes 4.4 GB: only with {Variable}=1 (the job beyond-4-gb).");

        string path = Path.Combine(Path.GetTempPath(), $"tiffnet-huge-{Guid.NewGuid():N}.tif");
        try
        {
            var options = TiffWriteOptions.Default with { Layout = TiffLayout.Tiles, TileSize = Tile, Compression = TiffCompression.None };
            var clock = Stopwatch.StartNew();
            using (var writer = TiffImageWriter.Create(path, Side, Side, TiffSampleType.UInt16, options: options))
            {
                int tiles = (Side + Tile - 1) / Tile;
                var tile = new ushort[Tile * Tile];
                for (int ty = 0; ty < tiles; ty++)
                {
                    for (int tx = 0; tx < tiles; tx++)
                    {
                        for (int r = 0; r < Tile; r++)
                        {
                            for (int c = 0; c < Tile; c++)
                            {
                                tile[r * Tile + c] = Sample((long)tx * Tile + c, (long)ty * Tile + r);
                            }
                        }

                        writer.WriteTile<ushort>(tx, ty, tile);
                    }
                }

                writer.Complete();
            }

            output.WriteLine($"written {new FileInfo(path).Length / 1e9:F2} GB in {clock.Elapsed.TotalSeconds:F0} s");
            Assert.True(new FileInfo(path).Length > 4L << 30);

            using var file = TiffFile.Open(path);
            var image = file.Primary;
            Assert.True(image.Storage!.BigTiff);
            Assert.Equal((Side, Side), (image.Width, image.Height));

            // Regions spread over the file, the last ones beyond 4 GB of offsets, each checked sample by sample.
            var random = new Random(47);
            foreach (var (x, y) in new[] { (0, 0), (Side - 300, Side - 200) }.Concat(Enumerable.Range(0, 30).Select(_ => (random.Next(Side - 300), random.Next(Side - 200)))))
            {
                var region = image.ReadRawRegion<ushort>(x, y, 300, 200);
                var expected = new ushort[300 * 200];
                for (int r = 0; r < 200; r++)
                {
                    for (int c = 0; c < 300; c++)
                    {
                        expected[r * 300 + c] = Sample(x + c, y + r);
                    }
                }

                Assert.Equal(expected, region);
            }
        }
        finally
        {
            File.Delete(path);
        }
    }
}
