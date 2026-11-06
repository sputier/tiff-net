using BenchmarkDotNet.Attributes;
using Eleris.TiffNet.Internal.Codecs;

namespace Eleris.TiffNet.Benchmarks;

/// <summary>
/// The predictors alone, on the rows of a 24 MP image (6 000 pixels a row, 4 000 rows): what undoing them costs next to the
/// decompression that precedes them (<see cref="TargetBenchmarks"/>). One thread.
/// </summary>
public class PredictorBenchmarks
{
    private const int Width = 6000, Rows = 4000;
    private byte[] _row = [];
    private byte[] _scratch = [];

    /// <summary>The samples: u16 grey or RGB (horizontal), f32 grey or RGB (floating point).</summary>
    [Params("u16-grey", "u16-rgb", "f32-grey", "f32-rgb")]
    public string Samples { get; set; } = "u16-grey";

    private int Channels => Samples.EndsWith("rgb", StringComparison.Ordinal) ? 3 : 1;

    private int Bytes => Samples.StartsWith("u16", StringComparison.Ordinal) ? 2 : 4;

    [GlobalSetup]
    public void Setup()
    {
        _row = new byte[Width * Channels * Bytes];
        _scratch = new byte[_row.Length];
        new Random(1).NextBytes(_row);
    }

    [Benchmark]
    public int Undo()
    {
        for (int r = 0; r < Rows; r++)
        {
            if (Bytes == 2)
            {
                Predictors.UndoHorizontal(_row, 2, Channels, littleEndian: true);
            }
            else
            {
                Predictors.UndoFloatingPoint(_row, _scratch, 4, Channels, littleEndian: true);
            }
        }

        return _row[0];
    }
}
