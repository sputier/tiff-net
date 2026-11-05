using System.Buffers.Binary;
using Eleris.TiffNet.Internal.Codecs;

namespace Eleris.TiffNet.Tests.Codecs;

public class PredictorTests
{
    /// <summary>Horizontal differencing of a row of samples of <paramref name="size"/> bytes, in the given byte order.</summary>
    private static byte[] Difference(ulong[] samples, int size, int stride, bool littleEndian)
    {
        ulong mask = size == 8 ? ulong.MaxValue : (1UL << (size * 8)) - 1;
        var bytes = new byte[samples.Length * size];
        for (int i = 0; i < samples.Length; i++)
        {
            ulong d = (i < stride ? samples[i] : samples[i] - samples[i - stride]) & mask;
            for (int b = 0; b < size; b++)
            {
                bytes[i * size + (littleEndian ? b : size - 1 - b)] = (byte)(d >> (8 * b));
            }
        }

        return bytes;
    }

    private static ulong[] Read(byte[] bytes, int size, bool littleEndian) => [.. Enumerable.Range(0, bytes.Length / size).Select(i =>
    {
        ulong v = 0;
        for (int b = 0; b < size; b++)
        {
            v |= (ulong)bytes[i * size + (littleEndian ? b : size - 1 - b)] << (8 * b);
        }

        return v;
    })];

    [Theory]
    [InlineData(1, 1, true)]
    [InlineData(2, 1, true)]
    [InlineData(2, 3, false)]
    [InlineData(4, 4, true)]
    [InlineData(4, 1, false)]
    [InlineData(8, 2, true)]
    [InlineData(8, 1, false)]
    public void Horizontal_differencing_is_undone_in_the_byte_order_of_the_file(int size, int stride, bool littleEndian)
    {
        var random = new Random(size * 10 + stride);
        ulong mask = size == 8 ? ulong.MaxValue : (1UL << (size * 8)) - 1;
        ulong[] samples = [.. Enumerable.Range(0, stride * 37).Select(_ => (ulong)random.NextInt64() & mask)];
        byte[] row = Difference(samples, size, stride, littleEndian);

        Predictors.UndoHorizontal(row, size, stride, littleEndian);

        Assert.Equal(samples, Read(row, size, littleEndian));
    }

    /// <summary>The floating point predictor of Technical Note 3: bytes split most significant first, then differenced.</summary>
    private static byte[] FloatingPoint(ulong[] samples, int size, int stride)
    {
        int n = samples.Length;
        var planes = new byte[n * size];
        for (int i = 0; i < n; i++)
        {
            for (int b = 0; b < size; b++)
            {
                planes[b * n + i] = (byte)(samples[i] >> (8 * (size - 1 - b)));
            }
        }

        for (int i = planes.Length - 1; i >= stride; i--)
        {
            planes[i] -= planes[i - stride];
        }

        return planes;
    }

    [Theory]
    [InlineData(2, 1, true)]
    [InlineData(4, 3, true)]
    [InlineData(4, 1, false)]
    [InlineData(8, 2, false)]
    [InlineData(8, 1, true)]
    public void The_floating_point_predictor_is_undone_in_the_byte_order_of_the_file(int size, int stride, bool littleEndian)
    {
        var random = new Random(size * 10 + stride);
        ulong mask = size == 8 ? ulong.MaxValue : (1UL << (size * 8)) - 1;
        ulong[] samples = [.. Enumerable.Range(0, stride * 41).Select(_ => (ulong)random.NextInt64() & mask)];
        byte[] row = FloatingPoint(samples, size, stride);

        Predictors.UndoFloatingPoint(row, new byte[row.Length], size, stride, littleEndian);

        Assert.Equal(samples, Read(row, size, littleEndian));
    }

    [Fact]
    public void A_float_row_gives_its_values()
    {
        float[] values = [1.5f, -2.25f, 1e30f, 0f, float.NaN];
        byte[] row = FloatingPoint([.. values.Select(v => (ulong)BitConverter.SingleToUInt32Bits(v))], 4, 1);

        Predictors.UndoFloatingPoint(row, new byte[row.Length], 4, 1, littleEndian: true);

        Assert.Equal(values, Enumerable.Range(0, 5).Select(i => BinaryPrimitives.ReadSingleLittleEndian(row.AsSpan(i * 4))));
    }
}
