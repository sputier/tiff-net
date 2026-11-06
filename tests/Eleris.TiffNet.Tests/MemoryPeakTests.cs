using Eleris.TiffNet.Tests.Fixtures;
using Eleris.TiffNet.Tests.Writing;

namespace Eleris.TiffNet.Tests;

/// <summary>
/// What reads and writes cost in memory, measured as the bytes allocated (an upper bound of the managed peak): a whole image read
/// costs its pixels and the buffers of its threads; a piecewise write costs a bounded window, whatever the size of the image.
/// </summary>
[Collection(nameof(Measurement))]
public class MemoryPeakTests
{
    private static string Write(TiffImage image, TiffWriteOptions options)
    {
        string path = Path.Combine(Path.GetTempPath(), $"tiffnet-memory-{Guid.NewGuid():N}.tif");
        new TiffFile().Add(image).Save(path, options);
        return path;
    }

    public static TheoryData<TiffCompression, TiffLayout> Cases() => new()
    {
        { TiffCompression.Deflate, TiffLayout.Strips },
        { TiffCompression.Lzw, TiffLayout.Tiles },
        { TiffCompression.None, TiffLayout.Strips },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void A_whole_image_read_costs_at_most_1_1_times_its_pixels(TiffCompression compression, TiffLayout layout)
    {
        // 4 000 x 3 000 f32 RGB: 144 MB of pixels, read on 4 threads. Each thread holds the buffers of one block (a compressed
        // and a decoded tile, about 1.6 MB here): a constant, which the 10 % allows from about 64 MB of pixels.
        var image = DirectoryWriterTests.Sample<float>(4000, 3000, 3, 49);
        string path = Write(image, TiffWriteOptions.Default with { Compression = compression, Layout = layout });
        try
        {
            using var file = TiffFile.Open(path, TiffReadOptions.Default with { MaxDegreeOfParallelism = 4 });
            long pixels = 4000L * 3000 * 3 * 4;

            GC.Collect();
            long before = GC.GetTotalAllocatedBytes(precise: true);
            float[] samples = file.Primary.ReadRawPixels<float>();
            long allocated = GC.GetTotalAllocatedBytes(precise: true) - before;

            Assert.Equal(pixels / 4, samples.Length);
            Assert.True(allocated <= pixels * 11 / 10, $"{allocated / 1e6:F1} MB allocated for {pixels / 1e6:F1} MB of pixels ({compression}, {layout})");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void A_piecewise_write_costs_a_bounded_window_whatever_the_size_of_the_image()
    {
        long Cost(int height)
        {
            const int width = 4000;
            var rows = new float[width * 16];
            for (int i = 0; i < rows.Length; i++)
            {
                rows[i] = i % 977;
            }

            string path = Path.Combine(Path.GetTempPath(), $"tiffnet-memory-{Guid.NewGuid():N}.tif");
            try
            {
                GC.Collect();
                long before = GC.GetTotalAllocatedBytes(precise: true);
                using (var writer = TiffImageWriter.Create(path, width, height, TiffSampleType.Float32,
                    options: TiffWriteOptions.Default with { MaxDegreeOfParallelism = 4 }))
                {
                    for (int y = 0; y < height; y += 16)
                    {
                        writer.WriteRows<float>(rows, 16);
                    }

                    writer.Complete();
                }

                return GC.GetTotalAllocatedBytes(precise: true) - before;
            }
            finally
            {
                File.Delete(path);
            }
        }

        long small = Cost(256), large = Cost(4096);   // 4 MB and 64 MB of pixels

        Assert.True(large < 16L << 20, $"{large / 1e6:F1} MB allocated for 64 MB of pixels");
        Assert.True(large < small * 4 + (4L << 20), $"{small / 1e6:F1} MB for 4 MB, {large / 1e6:F1} MB for 64 MB");
    }
}
