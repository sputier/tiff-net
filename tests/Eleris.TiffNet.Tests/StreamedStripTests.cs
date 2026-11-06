using System.IO.Compression;
using System.Runtime.InteropServices;
using Eleris.TiffNet.Internal.Codecs;
using Eleris.TiffNet.Internal.Pixels;
using Eleris.TiffNet.Tests.Fixtures;

namespace Eleris.TiffNet.Tests;

/// <summary>
/// A large Deflate strip is inflated as a stream, a few rows at a time, straight to the destination: the same samples as a
/// strip decoded whole, and about the memory of the pixels alone.
/// </summary>
[Collection(nameof(Measurement))]
public class StreamedStripTests
{
    private const int Width = 1500, Height = 1200;

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void A_single_strip_reads_as_the_same_image_in_strips(int predictor)
    {
        // 1 500 x 1 200 RGB u16 (or f32 for the floating point predictor): 10.8 or 21.6 MB in one strip, beyond the 4 MB streamed.
        var (file, expected) = SingleStrip(predictor);
        using (file)
        {
            Assert.True((long)Width * Height * 3 * (predictor == 3 ? 4 : 2) > PixelPlan.StreamBytes);
            var image = file.Primary;
            if (predictor == 3)
            {
                float[] values = MemoryMarshal.Cast<byte, float>(expected).ToArray();
                Assert.Equal(values, image.ReadRawPixels<float>());
                Assert.Equal(Region(values, 700, 900, 50, 40), image.ReadRawRegion<float>(700, 900, 50, 40));
                return;
            }

            ushort[] samples = MemoryMarshal.Cast<byte, ushort>(expected).ToArray();
            Assert.Equal(samples, image.ReadRawPixels<ushort>());
            Assert.Equal(Region(samples, 700, 900, 50, 40), image.ReadRawRegion<ushort>(700, 900, 50, 40));
            Assert.Equal(samples.Where((_, i) => i % 3 == 1), image.ReadChannel<ushort>(1));
            float[] planar = image.ReadPixels<float>(TiffPixelScale.Normalized, TiffPixelOrder.Planar);
            Assert.Equal(samples[3 * (5 * Width + 7) + 2] / 65535f, planar[2 * Width * Height + 5 * Width + 7]);
        }
    }

    [Fact]
    public void A_truncated_or_damaged_single_strip_is_a_damaged_file()
    {
        byte[] data = new byte[Width * Height * 6];
        new Random(84).NextBytes(data);
        byte[] compressed = Compress(data);

        TiffException Read(byte[] strip)
        {
            using var file = TiffFile.Read(new MemoryStream(Build(strip, 1)));
            return Assert.Throws<TiffException>(() => file.Primary.ReadRawPixels<ushort>());
        }

        Assert.Contains("needed", Read(compressed[..(compressed.Length / 2)]).Message, StringComparison.Ordinal);
        byte[] damaged = [.. compressed];
        damaged[1000] ^= 0xFF;
        damaged[1001] ^= 0xFF;
        Assert.NotNull(Read(damaged));
    }

    [Fact]
    public void A_single_strip_costs_about_its_pixels()
    {
        var (file, _) = SingleStrip(2);
        using (file)
        {
            long pixels = (long)Width * Height * 3 * 2;
            GC.Collect();
            long before = GC.GetTotalAllocatedBytes(precise: true);
            ushort[] samples = file.Primary.ReadRawPixels<ushort>();
            long allocated = GC.GetTotalAllocatedBytes(precise: true) - before;

            Assert.Equal(pixels / 2, samples.Length);
            Assert.True(allocated < pixels + (4 << 20), $"{allocated / 1e6:F1} MB allocated for {pixels / 1e6:F1} MB of pixels");
        }
    }

    private static T[] Region<T>(T[] samples, int x, int y, int width, int height) =>
        [.. Enumerable.Range(0, height).SelectMany(r => samples.Skip(((y + r) * Width + x) * 3).Take(width * 3))];

    /// <summary>A file of one Deflate strip: no predictor (1), horizontal (2) on u16, floating point (3) on f32.</summary>
    private static (TiffFile File, byte[] Expected) SingleStrip(int predictor)
    {
        int bytes = predictor == 3 ? 4 : 2;
        var random = new Random(predictor);
        byte[] expected = new byte[Width * Height * 3 * bytes];
        if (predictor == 3)
        {
            var values = MemoryMarshal.Cast<byte, float>(expected.AsSpan());
            for (int i = 0; i < values.Length; i++)
            {
                values[i] = (float)(Math.Sin(i / 300.0) + random.NextDouble() / 100);
            }
        }
        else
        {
            var values = MemoryMarshal.Cast<byte, ushort>(expected.AsSpan());
            for (int i = 0; i < values.Length; i++)
            {
                values[i] = (ushort)(30000 + 10000 * Math.Sin(i / 300.0) + random.Next(50));
            }
        }

        byte[] predicted = [.. expected];
        int rowBytes = Width * 3 * bytes;
        byte[] scratch = new byte[rowBytes];
        for (int r = 0; r < Height && predictor > 1; r++)
        {
            var row = predicted.AsSpan(r * rowBytes, rowBytes);
            if (predictor == 2)
            {
                PredictorEncoder.Horizontal(row, 2, 3);
            }
            else
            {
                PredictorEncoder.FloatingPoint(row, scratch, 4, 3);
            }
        }

        return (TiffFile.Read(new MemoryStream(Build(Compress(predicted), predictor))), expected);
    }

    private static byte[] Build(byte[] strip, int predictor)
    {
        var builder = new TiffBuilder { Data = strip };
        builder.AddDirectory().Image(Width, Height, bits: (ushort)(predictor == 3 ? 32 : 16), samples: 3, photometric: 2)
            .Remove(259).Add(259, TiffTagType.Short, 8).Remove(279).Add(279, TiffTagType.Long, strip.Length)
            .Add(317, TiffTagType.Short, predictor).Add(339, TiffTagType.Short, predictor == 3 ? 3 : 1, predictor == 3 ? 3 : 1, predictor == 3 ? 3 : 1)
            .Sorted();
        return builder.Build();
    }

    private static byte[] Compress(byte[] data)
    {
        using var output = new MemoryStream();
        using (var zlib = new ZLibStream(output, CompressionLevel.Fastest))
        {
            zlib.Write(data);
        }

        return output.ToArray();
    }
}
