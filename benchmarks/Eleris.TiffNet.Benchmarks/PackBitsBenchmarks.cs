using BenchmarkDotNet.Attributes;
using Eleris.TiffNet.Internal.Codecs;

namespace Eleris.TiffNet.Benchmarks;

/// <summary>
/// PackBits alone, row by row, on 8 MB of 16-bit samples (rows of 6 000 bytes): a smooth image with noise (short runs and
/// literals) and a flat one (long runs). One thread.
/// </summary>
public class PackBitsBenchmarks
{
    private const int RowBytes = 6000, Rows = 1400;
    private byte[] _data = [];
    private byte[] _encoded = [];
    private byte[] _decoded = [];
    private int _length;

    [Params("noisy", "flat")]
    public string Image { get; set; } = "noisy";

    [GlobalSetup]
    public void Setup()
    {
        var random = new Random(1);
        _data = new byte[RowBytes * Rows];
        for (int i = 0; i < _data.Length; i += 2)
        {
            int x = i % RowBytes / 2, y = i / RowBytes;
            ushort v = Image == "flat" ? (ushort)(1000 + y / 64) : (ushort)(1000 + x / 4 + y / 8 + random.Next(4));
            _data[i] = (byte)v;
            _data[i + 1] = (byte)(v >> 8);
        }

        _encoded = new byte[PackBitsEncoder.MaxEncoded(RowBytes) * Rows];
        _decoded = new byte[_data.Length];
        _length = Encode();
    }

    [Benchmark]
    public int Encode()
    {
        int written = 0;
        for (int r = 0; r < Rows; r++)
        {
            written += PackBitsEncoder.EncodeRow(_data.AsSpan(r * RowBytes, RowBytes), _encoded.AsSpan(written));
        }

        return written;
    }

    [Benchmark]
    public int Decode() => PackBits.Decode(_encoded.AsSpan(0, _length), _decoded, out _);
}
