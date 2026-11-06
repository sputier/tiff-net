using Eleris.TiffNet.Internal.Codecs;

namespace Eleris.TiffNet.Tests.Writing;

public class PredictorEncoderTests
{
    [Theory]
    [InlineData(1, 1)]
    [InlineData(1, 3)]
    [InlineData(2, 1)]
    [InlineData(2, 4)]
    [InlineData(4, 3)]
    [InlineData(8, 2)]
    public void Horizontal_differencing_is_undone_by_the_reader(int size, int stride)
    {
        byte[] row = new byte[size * stride * 61];
        new Random(size * 7 + stride).NextBytes(row);
        byte[] original = (byte[])row.Clone();

        PredictorEncoder.Horizontal(row, size, stride);
        Predictors.UndoHorizontal(row, size, stride, littleEndian: true);

        Assert.Equal(original, row);
    }

    [Theory]
    [InlineData(2, 1)]
    [InlineData(4, 1)]
    [InlineData(4, 3)]
    [InlineData(8, 2)]
    public void The_floating_point_predictor_is_undone_by_the_reader(int size, int stride)
    {
        byte[] row = new byte[size * stride * 47];
        new Random(size * 5 + stride).NextBytes(row);
        byte[] original = (byte[])row.Clone();
        byte[] scratch = new byte[row.Length];

        PredictorEncoder.FloatingPoint(row, scratch, size, stride);
        Predictors.UndoFloatingPoint(row, scratch, size, stride, littleEndian: true);

        Assert.Equal(original, row);
    }
}
