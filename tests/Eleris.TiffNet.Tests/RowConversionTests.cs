using System.Numerics;
using Eleris.TiffNet.Internal.Pixels;

namespace Eleris.TiffNet.Tests;

/// <summary>The conversion of rows in bulk gives the samples of the conversion one by one, bit for bit.</summary>
public class RowConversionTests
{
    [Fact]
    public void Every_16_bit_value_converts_as_one_by_one()
    {
        ushort[] all = [.. Enumerable.Range(0, 65536).Select(v => (ushort)v)];
        short[] signed = [.. all.Select(v => unchecked((short)v))];
        byte[] bytes = [.. Enumerable.Range(0, 256).Select(v => (byte)v)];

        foreach (double maximum in new[] { double.NaN, 65535, 4095, 1 })
        {
            Same<ushort, float>(all, maximum);
            Same<ushort, double>(all, maximum);
            Same<short, float>(signed, maximum == 65535 ? 32767 : maximum);
            Same<short, double>(signed, maximum == 65535 ? 32767 : maximum);
            Same<byte, float>(bytes, maximum == 65535 ? 255 : maximum);
            Same<byte, double>(bytes, maximum == 65535 ? 255 : maximum);
        }
    }

    [Fact]
    public void Floats_and_every_other_type_convert_as_one_by_one()
    {
        var random = new Random(79);
        float[] floats = [.. Enumerable.Range(0, 10_000).Select(_ => (float)(random.NextDouble() * 2e6 - 1e6)), float.NaN, float.PositiveInfinity,
            float.NegativeInfinity, -0f, float.Epsilon, float.MaxValue];
        Same<float, double>(floats, double.NaN);
        Same<float, double>(floats, 1);
        Same<float, float>(floats, 1);

        // 32-bit integers, with those around the precision of float (2^24) and the extremes, where rounding shows.
        int[] ints = [.. Enumerable.Range(0, 10_000).Select(_ => random.Next(int.MinValue, int.MaxValue)), int.MinValue, int.MaxValue,
            0, -1, 1, (1 << 24) + 1, (1 << 24) + 3, -(1 << 24) - 1, int.MaxValue - 64, int.MinValue + 64, (1 << 30) + 65];
        uint[] uints = [.. ints.Select(v => unchecked((uint)v)), uint.MaxValue, uint.MaxValue - 128, 1u << 31, (1u << 31) + 129];
        foreach (double maximum in new[] { double.NaN, int.MaxValue, 4095, 1 })
        {
            Same<int, double>(ints, maximum);
            Same<int, float>(ints, maximum);
        }

        foreach (double maximum in new[] { double.NaN, uint.MaxValue, 4095, 1 })
        {
            Same<uint, double>(uints, maximum);
            Same<uint, float>(uints, maximum);
        }

        Same<ushort, int>([0, 1, 65535], double.NaN);
    }

    [Theory]
    [InlineData(1, 0, 1, 0)]
    [InlineData(3, 1, 1, 0)]
    [InlineData(1, 0, 3, 2)]
    [InlineData(4, 3, 4, 3)]
    public void Strided_runs_touch_only_their_samples(int sourceStride, int sourceStart, int destinationStride, int destinationStart)
    {
        ushort[] source = [.. Enumerable.Range(0, 400).Select(v => (ushort)(v * 97))];
        int count = (source.Length - sourceStart - 1) / sourceStride + 1;
        float[] destination = new float[destinationStart + (count - 1) * destinationStride + 1 + 8];
        Array.Fill(destination, -1f);

        RowConversion<ushort, float>.Strided(source, sourceStart, sourceStride, destination, destinationStart, destinationStride, count,
            identity: false, 65535);

        for (int i = 0; i < destination.Length; i++)
        {
            int k = (i - destinationStart) / destinationStride;
            bool written = i >= destinationStart && (i - destinationStart) % destinationStride == 0 && k < count;
            float expected = written ? (float)(source[sourceStart + k * sourceStride] / 65535.0) : -1f;
            Assert.Equal(expected, destination[i]);
        }
    }

    [Theory]
    [InlineData(TiffPixelOrder.Interleaved)]
    [InlineData(TiffPixelOrder.Planar)]
    public void Images_of_either_layout_read_the_same_in_every_order_scale_and_channel(TiffPixelOrder stored)
    {
        // An odd size: every vector loop ends with a scalar tail.
        const int width = 37, height = 23;
        var random = new Random(78);
        ushort[] samples = [.. Enumerable.Range(0, width * height * 3).Select(_ => (ushort)random.Next(65536))];
        using var stream = new MemoryStream();
        new TiffFile().Add(TiffImage.Create<ushort>(samples, width, height, 3, TiffColorModel.Rgb))
            .Save(stream, TiffWriteOptions.Default with { PlanarConfiguration = stored, Compression = TiffCompression.None });
        using var file = TiffFile.Read(new MemoryStream(stream.ToArray()));
        var image = file.Primary;

        float Expected(int i, bool normalized) => normalized ? (float)(samples[i] / 65535.0) : samples[i];
        foreach (bool normalized in new[] { false, true })
        {
            var scale = normalized ? TiffPixelScale.Normalized : TiffPixelScale.Stored;
            float[] interleaved = image.ReadPixels<float>(scale);
            float[] planar = image.ReadPixels<float>(scale, TiffPixelOrder.Planar);
            double[] doubles = image.ReadPixels<double>(scale, TiffPixelOrder.Planar);
            for (int p = 0; p < width * height; p++)
            {
                for (int c = 0; c < 3; c++)
                {
                    Assert.Equal(Expected(p * 3 + c, normalized), interleaved[p * 3 + c]);
                    Assert.Equal(Expected(p * 3 + c, normalized), planar[c * width * height + p]);
                    Assert.Equal(normalized ? samples[p * 3 + c] / 65535.0 : samples[p * 3 + c], doubles[c * width * height + p]);
                }
            }

            for (int c = 0; c < 3; c++)
            {
                float[] channel = image.ReadChannel<float>(c, scale);
                Assert.Equal(Enumerable.Range(0, width * height).Select(p => Expected(p * 3 + c, normalized)), channel);
            }

            float[] region = image.ReadRegion<float>(5, 3, 20, 11, scale, TiffPixelOrder.Planar);
            for (int c = 0; c < 3; c++)
            {
                for (int y = 0; y < 11; y++)
                {
                    for (int x = 0; x < 20; x++)
                    {
                        Assert.Equal(Expected(((y + 3) * width + x + 5) * 3 + c, normalized), region[(c * 11 + y) * 20 + x]);
                    }
                }
            }
        }

        Assert.Equal(samples, image.ReadRawPixels<ushort>());
    }

    /// <summary>Every length up to a few vectors, so that every tail is checked.</summary>
    private static void Same<TD, TOut>(TD[] values, double maximum)
        where TD : unmanaged, INumber<TD>
        where TOut : unmanaged, INumber<TOut>
    {
        foreach (int length in new[] { values.Length, 0, 1, 7, 8, 15, 16, 17, 31, 33, 63, 65 }.Where(l => l <= values.Length))
        {
            var source = values.AsSpan(0, length);
            var bulk = new TOut[length];
            RowConversion<TD, TOut>.Run(source, bulk, identity: false, maximum);
            for (int i = 0; i < length; i++)
            {
                TOut expected = double.IsNaN(maximum)
                    ? TOut.CreateTruncating(source[i])
                    : TOut.CreateTruncating(double.CreateTruncating(source[i]) / maximum);
                // Bit for bit: the sign of a zero counts, any NaN is one.
                bool same = System.Runtime.InteropServices.MemoryMarshal.AsBytes(new[] { expected }.AsSpan())
                    .SequenceEqual(System.Runtime.InteropServices.MemoryMarshal.AsBytes(new[] { bulk[i] }.AsSpan()));
                if (!same && !(TOut.IsNaN(expected) && TOut.IsNaN(bulk[i])))
                {
                    Assert.Fail($"{typeof(TD).Name} {source[i]} / {maximum} gives {bulk[i]} instead of {expected} as {typeof(TOut).Name}");
                }
            }
        }
    }
}
