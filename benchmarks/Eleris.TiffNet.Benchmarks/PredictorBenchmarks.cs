using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using BenchmarkDotNet.Attributes;
using Eleris.TiffNet.Internal.Codecs;

namespace Eleris.TiffNet.Benchmarks;

/// <summary>
/// The predictors of the library against the kernels of the review's prototype (research/review-2026-11/performance/bench/Kernels.cs),
/// on 1024 rows of 4096 samples: horizontal on 16-bit grey, floating point on 32-bit grey.
/// </summary>
[MemoryDiagnoser]
public class PredictorBenchmarks
{
    private const int Width = 4096, Rows = 1024;
    private byte[] _data = [];
    private readonly byte[] _scratch = new byte[Width * 4];

    [GlobalSetup]
    public void Setup()
    {
        _data = new byte[Width * Rows * 4];
        new Random(1).NextBytes(_data);
    }

    [Benchmark(Baseline = true)]
    public void HorizontalPrototype()
    {
        for (int r = 0; r < Rows; r++)
        {
            PrototypeHorAcc16(_data.AsSpan(r * Width * 2, Width * 2), 1);
        }
    }

    [Benchmark]
    public void Horizontal()
    {
        for (int r = 0; r < Rows; r++)
        {
            Predictors.UndoHorizontal(_data.AsSpan(r * Width * 2, Width * 2), 2, 1, littleEndian: true);
        }
    }

    [Benchmark]
    public void FloatingPointPrototype()
    {
        for (int r = 0; r < Rows; r++)
        {
            PrototypeFpAcc32(_data.AsSpan(r * Width * 4, Width * 4), 1, _scratch);
        }
    }

    [Benchmark]
    public void FloatingPoint()
    {
        for (int r = 0; r < Rows; r++)
        {
            Predictors.UndoFloatingPoint(_data.AsSpan(r * Width * 4, Width * 4), _scratch, 4, 1, littleEndian: true);
        }
    }

    private static void PrototypeHorAcc16(Span<byte> row, int stride)
    {
        Span<ushort> s = MemoryMarshal.Cast<byte, ushort>(row);
        ref ushort r = ref MemoryMarshal.GetReference(s);
        int n = s.Length;
        if (stride == 1)
        {
            ushort acc = r;
            for (int i = 1; i < n; i++) { acc += Unsafe.Add(ref r, i); Unsafe.Add(ref r, i) = acc; }
        }
        else
        {
            for (int i = stride; i < n; i++) Unsafe.Add(ref r, i) += Unsafe.Add(ref r, i - stride);
        }
    }

    private static void PrototypeFpAcc32(Span<byte> row, int stride, Span<byte> tmp)
    {
        int count = row.Length;
        ref byte r = ref MemoryMarshal.GetReference(row);
        for (int i = stride; i < count; i++) Unsafe.Add(ref r, i) += Unsafe.Add(ref r, i - stride);
        row.CopyTo(tmp);
        int w = count / 4;
        ref byte p0 = ref MemoryMarshal.GetReference(tmp);
        ref byte p1 = ref Unsafe.Add(ref p0, w);
        ref byte p2 = ref Unsafe.Add(ref p0, 2 * w);
        ref byte p3 = ref Unsafe.Add(ref p0, 3 * w);
        ref uint dst = ref Unsafe.As<byte, uint>(ref r);
        for (int i = 0; i < w; i++)
        {
            Unsafe.Add(ref dst, i) = (uint)Unsafe.Add(ref p0, i) << 24 | (uint)Unsafe.Add(ref p1, i) << 16
                                   | (uint)Unsafe.Add(ref p2, i) << 8 | Unsafe.Add(ref p3, i);
        }
    }
}
