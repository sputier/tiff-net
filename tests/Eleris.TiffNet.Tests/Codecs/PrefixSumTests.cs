using System.Numerics;
using Eleris.TiffNet.Internal.Codecs;

namespace Eleris.TiffNet.Tests.Codecs;

/// <summary>The running sums with vectors give those of the loop, for every element type, stride and length.</summary>
public class PrefixSumTests
{
    [Fact]
    public void The_running_sums_with_vectors_are_those_of_the_loop()
    {
        var random = new Random(82);
        foreach (int stride in Enumerable.Range(1, 19))
        {
            foreach (int length in new[] { 0, 1, 5, 15, 16, 17, 31, 32, 33, 47, 100, 257, 6000 })
            {
                Same<byte>(random, stride, length);
                Same<ushort>(random, stride, length);
                Same<uint>(random, stride, length);
            }
        }
    }

    private static void Same<T>(Random random, int stride, int length) where T : unmanaged, IBinaryInteger<T>
    {
        var values = new T[length];
        for (int i = 0; i < length; i++)
        {
            values[i] = T.CreateTruncating(random.NextInt64());
        }

        T[] expected = [.. values];
        for (int i = stride; i < length; i++)
        {
            expected[i] += expected[i - stride];
        }

        PrefixSum.Run<T>(values, stride);
        if (!expected.AsSpan().SequenceEqual(values))
        {
            Assert.Fail($"{typeof(T).Name}, stride {stride}, {length} values: the sums differ (vectors: {PrefixSum.Vectorised<T>(stride)})");
        }
    }

    [Fact]
    public void The_floating_point_predictor_is_undone_at_every_length_and_stride()
    {
        var random = new Random(3);
        foreach (int bytes in new[] { 2, 4, 8 })
        {
            foreach (int stride in new[] { 1, 2, 3, 4 })
            {
                foreach (int pixels in new[] { 1, 5, 16, 17, 33, 64, 101, 6000 })
                {
                    byte[] row = new byte[pixels * stride * bytes];
                    random.NextBytes(row);
                    byte[] original = [.. row];
                    byte[] scratch = new byte[row.Length];

                    PredictorEncoder.FloatingPoint(row, scratch, bytes, stride);
                    Predictors.UndoFloatingPoint(row, scratch, bytes, stride, littleEndian: true);

                    Assert.True(original.AsSpan().SequenceEqual(row), $"{bytes} bytes, stride {stride}, {pixels} pixels");
                }
            }
        }
    }
}
