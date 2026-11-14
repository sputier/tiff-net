using Eleris.TiffNet.Internal.Jpeg;

namespace Eleris.TiffNet.Tests.Jpeg;

/// <summary>The colour conversion and the fancy upsampling with vectors give the samples of the scalar code, bit for bit.</summary>
public class ColourVectorTests
{
    private static readonly byte[] Limit = [.. Enumerable.Range(-256, 768).Select(v => (byte)Math.Clamp(v, 0, 255))];

    [Fact]
    public void YCbCr_converts_as_the_tables_of_libjpeg_at_every_width()
    {
        var random = new Random(122);
        foreach (int width in Enumerable.Range(1, 70).Concat([255, 256, 4001]))
        {
            foreach (int kind in new[] { 0, 1, 2 })
            {
                // Random samples, the extremes (where the range limit clamps), and a grey ramp.
                byte[] Make() => [.. Enumerable.Range(0, width).Select(i => kind switch
                {
                    0 => (byte)random.Next(256),
                    1 => (byte)(random.Next(2) * 255),
                    _ => (byte)(i * 7),
                })];
                byte[] luma = Make(), blue = Make(), red = Make();
                byte[] vector = new byte[3 * width + 5], scalar = new byte[3 * width + 5];
                Array.Fill(vector, (byte)0xEE);
                Array.Fill(scalar, (byte)0xEE);

                JpegColour.Convert(luma, blue, red, vector, width, Limit);
                JpegColour.ConvertScalar(luma, blue, red, scalar, width, Limit, 0);

                Assert.True(scalar.AsSpan().SequenceEqual(vector), $"width {width}, kind {kind}");
            }
        }

        // Every pair of Cb and Cr with a few lumas: the whole table.
        byte[] all = [.. Enumerable.Range(0, 65536).Select(i => (byte)(i & 255))];
        byte[] high = [.. Enumerable.Range(0, 65536).Select(i => (byte)(i >> 8))];
        foreach (byte y in new byte[] { 0, 1, 127, 128, 254, 255 })
        {
            byte[] lumas = [.. Enumerable.Repeat(y, 65536)];
            byte[] vector = new byte[3 * 65536], scalar = new byte[3 * 65536];
            JpegColour.Convert(lumas, all, high, vector, 65536, Limit);
            JpegColour.ConvertScalar(lumas, all, high, scalar, 65536, Limit, 0);
            Assert.True(scalar.AsSpan().SequenceEqual(vector), $"luma {y}");
        }
    }

    [Fact]
    public void The_fancy_filters_upsample_as_the_loop_at_every_width()
    {
        var random = new Random(123);
        foreach (int w in Enumerable.Range(3, 70).Concat([255, 256, 2001]))
        {
            byte[] near = new byte[w], far = new byte[w];
            random.NextBytes(near);
            random.NextBytes(far);
            if (w % 3 == 0)
            {
                Array.Fill(near, (byte)255);   // the largest sums
                Array.Fill(far, (byte)255);
            }

            byte[] vector = new byte[2 * w + 3], scalar = new byte[2 * w + 3];
            JpegColour.FancyH2V1(near, vector);
            JpegColour.FancyH2V1(near, scalar, vectors: false);
            Assert.True(scalar.AsSpan().SequenceEqual(vector), $"h2v1, width {w}");

            foreach (int bias in new[] { 1, 2 })
            {
                JpegColour.FancyH1V2(near, far, bias, vector.AsSpan(0, w));
                JpegColour.FancyH1V2(near, far, bias, scalar.AsSpan(0, w), vectors: false);
                Assert.True(scalar.AsSpan().SequenceEqual(vector), $"h1v2, width {w}, bias {bias}");
            }

            JpegColour.FancyH2V2(near, far, vector);
            JpegColour.FancyH2V2(near, far, scalar, vectors: false);
            Assert.True(scalar.AsSpan().SequenceEqual(vector), $"h2v2, width {w}");
        }
    }
}
