using Eleris.TiffNet.Internal.Jpeg;

namespace Eleris.TiffNet.Tests.Jpeg;

/// <summary>The scalar and vectorised (AVX2, 128-bit) inverse DCTs give the same samples, so that the pixels of a JPEG do not depend on the machine.</summary>
public class IdctTests
{
    [Theory]
    [InlineData(8)]
    [InlineData(11)]
    [InlineData(256)]
    public void The_vectorised_transform_gives_the_samples_of_the_scalar_one(int stride)
    {
        // Dequantized coefficients within what 8-bit samples give (+-2^11), sparse as in photographs.
        Compare(stride, 100_000, (random, k, density) =>
        {
            ushort q = (ushort)random.Next(1, random.Next(3) == 0 ? 4 : 120);
            int limit = 2047 / q;
            short c = k == 0 || random.Next(64) < density ? (short)random.Next(-limit, limit + 1) : (short)0;
            return (c, q);
        });
    }

    [Fact]
    public void Both_transforms_wrap_alike_on_hostile_blocks()
    {
        // Any coefficient with any 16-bit quantization step: the 32-bit sums overflow, identically.
        Compare(8, 100_000, (random, k, density) =>
        {
            ushort q = (ushort)random.Next(1, 65536);
            short c = k == 0 || random.Next(64) < density ? (short)random.Next(short.MinValue, short.MaxValue + 1) : (short)0;
            return (c, q);
        });
    }

    [Fact]
    public void A_block_of_DC_only_gives_what_the_whole_transform_gives()
    {
        var random = new Random(49);
        short[] block = new short[64];
        ushort[] quantization = new ushort[64];
        byte[] whole = new byte[64], dc = new byte[64];
        for (int n = 0; n < 100_000; n++)
        {
            block[0] = (short)random.Next(short.MinValue, short.MaxValue + 1);
            quantization[0] = (ushort)random.Next(1, n % 2 == 0 ? 256 : 65536);
            JpegIdct.TransformScalar(block, quantization, whole, 8);
            JpegIdct.TransformDcOnly(block[0] * quantization[0], dc, 8);
            Assert.True(whole.AsSpan().SequenceEqual(dc), $"DC {block[0]} x {quantization[0]}");
        }
    }

    private static void Compare(int stride, int blocks, Func<Random, int, int, (short Coefficient, ushort Step)> make)
    {
        if (!JpegIdctAvx2.IsSupported && !JpegIdctVector128.IsSupported)
        {
            return;   // only the scalar transform runs on this machine
        }

        var random = new Random(48 + stride);
        short[] block = new short[64];
        ushort[] quantization = new ushort[64];
        byte[] scalar = new byte[7 * stride + 8], vector = new byte[7 * stride + 8];
        for (int n = 0; n < blocks; n++)
        {
            int density = random.Next(0, 64);
            for (int k = 0; k < 64; k++)
            {
                (block[k], quantization[k]) = make(random, k, density);
            }

            JpegIdct.TransformScalar(block, quantization, scalar, stride);
            if (JpegIdctAvx2.IsSupported)
            {
                JpegIdctAvx2.Transform(block, quantization, vector, stride);
                if (!scalar.AsSpan().SequenceEqual(vector))
                {
                    Assert.Fail($"block {n} differs with AVX2");
                }
            }

            JpegIdctVector128.Transform(block, quantization, vector, stride);
            if (!scalar.AsSpan().SequenceEqual(vector))
            {
                Assert.Fail($"block {n} differs with 128-bit vectors");
            }
        }
    }
}
