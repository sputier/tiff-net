using System.IO.Compression;
using BenchmarkDotNet.Attributes;
using BitMiracle.LibTiff.Classic;
using ComponentAce.Compression.Libs.zlib;
using Eleris.TiffNet;

namespace Bench;

/// <summary>End-to-end facade reads (BDN). Throughput = decoded pixel bytes / Mean.</summary>
[Config(typeof(BigConfig))]
public class ReadBenchmarks
{
    [Params("u16_24_none.tif", "u16_24_lzw_p2.tif", "u16_24_zip_p2.tif", "u16_24_packbits.tif", "u16_24_tiled_zip_p2.tif",
        "f32rgb_24_none.tif", "f32rgb_24_zip_p3.tif", "f32rgb_24_lzw_p3.tif", "f32rgb_24_planar_none.tif", "f32rgb_24_tiled_zip_p3.tif",
        "u16_61_none.tif", "u16_61_zip_p2.tif")]
    public string File_ { get; set; } = "";

    [Benchmark(Baseline = true)]
    public byte[] FileReadAllBytes() => File.ReadAllBytes(Data.P(File_));

    [Benchmark]
    public TiffImage TiffImageRead() => TiffImage.Read(Data.P(File_));
}

[Config(typeof(BigConfig))]
public class WriteBenchmarks
{
    [Params("u16_24", "f32rgb_24")]
    public string Image { get; set; } = "";

    [Params(TiffCompression.None, TiffCompression.Lzw, TiffCompression.Deflate, TiffCompression.PackBits)]
    public TiffCompression Compression { get; set; }

    TiffImage _img = null!;
    string _out = "";

    [GlobalSetup]
    public void Setup()
    {
        _img = Image == "u16_24" ? Data.U16_24 : Data.F32Rgb_24;
        _out = Data.P($"bdn_out_{Image}_{Compression}.tif");
    }

    [GlobalCleanup]
    public void Cleanup() => File.Delete(_out);

    [Benchmark]
    public void Save() => _img.Save(_out, new TiffWriteOptions { Compression = Compression });
}

/// <summary>Codec kernels on one real strip: original LibTiff.Net code vs Span/zlib-ng replacements.</summary>
[Config(typeof(MicroConfig))]
public class KernelBenchmarks
{
    byte[] _u16Strip = [];   // 21 rows x 6000 x u16 (252 KB) of horizontally-differenced data
    byte[] _f32Strip = [];   // 4 rows x 6000 x 3 x f32 (288 KB)
    byte[] _work = [];
    byte[] _tmp = [];
    byte[] _plane = [];      // one row of one plane (6000 floats)
    byte[] _rowOut = [];
    const int U16Row = 6000 * 2, F32Row = 6000 * 3 * 4;

    [GlobalSetup]
    public void Setup()
    {
        var u = Data.U16_24.Data;
        _u16Strip = u.AsSpan(0, 21 * U16Row).ToArray();
        _f32Strip = Data.F32Rgb_24.Data.AsSpan(0, 4 * F32Row).ToArray();
        _work = new byte[Math.Max(_u16Strip.Length, _f32Strip.Length)];
        _tmp = new byte[_work.Length];
        _plane = Data.F32Rgb_24.Data.AsSpan(0, 6000 * 4).ToArray();
        _rowOut = new byte[F32Row];
    }

    // --- horizontal predictor, 16-bit, decode (accumulate) per row over a strip
    [Benchmark] public void HorAcc16_Original() { for (int r = 0; r < 21; r++) Original.HorAcc16(_u16Strip, r * U16Row, U16Row, 1); }
    [Benchmark] public void HorAcc16_Span() { for (int r = 0; r < 21; r++) Fast.HorAcc16(_u16Strip.AsSpan(r * U16Row, U16Row), 1); }
    // encode (difference) - original works in place on a copy; the proposed one writes to a separate buffer (no copy needed)
    [Benchmark] public void HorDiff16_Original() { for (int r = 0; r < 21; r++) Original.HorDiff16(_u16Strip, r * U16Row, U16Row, 1); }
    [Benchmark] public void HorDiff16_Vector() { for (int r = 0; r < 21; r++) Fast.HorDiff16(_u16Strip.AsSpan(r * U16Row, U16Row), _work.AsSpan(r * U16Row, U16Row), 1); }

    // --- floating point predictor, 32-bit RGB
    [Benchmark] public void FpAcc32_Original() { for (int r = 0; r < 4; r++) Original.FpAcc(_f32Strip, r * F32Row, F32Row, 3, 4); }
    [Benchmark] public void FpAcc32_Span() { for (int r = 0; r < 4; r++) Fast.FpAcc32(_f32Strip.AsSpan(r * F32Row, F32Row), 3, _tmp); }
    [Benchmark] public void FpDiff32_Original() { for (int r = 0; r < 4; r++) Original.FpDiff(_f32Strip, r * F32Row, F32Row, 3, 4); }
    [Benchmark] public void FpDiff32_Span() { for (int r = 0; r < 4; r++) Fast.FpDiff32(_f32Strip.AsSpan(r * F32Row, F32Row), _work.AsSpan(r * F32Row, F32Row), 3); }

    // --- byte swapping of a big-endian 16-bit strip (postDecode)
    [Benchmark] public void Swab16_Original() => Original.Swab16(_u16Strip, 0, _u16Strip.Length);
    [Benchmark] public void Swab16_ReverseEndianness() => Fast.Swab16(_u16Strip);

    // --- planar float RGB: scatter one plane row into the interleaved row
    [Benchmark] public void Scatter32_Original() => Original.Scatter(_plane, 0, 6000, _rowOut, 4, 4, 12);
    [Benchmark] public void Scatter32_Typed() => Fast.Scatter32(_plane, _rowOut, 1, 3);
}

/// <summary>Whole-image codec comparison on the real strips of a file: ComponentAce zlib (in LibTiff.Net) vs System.IO.Compression.</summary>
[Config(typeof(BigConfig))]
public class ZlibBenchmarks
{
    [Params("u16_24_zip_p2.tif", "f32rgb_24_zip_p3.tif")]
    public string File_ { get; set; } = "";

    byte[][] _raw = [];
    int _stripBytes;
    byte[] _out = [];
    byte[][] _plain = [];   // predicted (pre-compression) strips for the encoders

    [GlobalSetup]
    public void Setup()
    {
        using var t = Tiff.Open(Data.P(File_), "rc");
        int n = t.NumberOfStrips();
        _stripBytes = t.StripSize();
        _raw = new byte[n][];
        _plain = new byte[n][];
        for (int s = 0; s < n; s++)
        {
            int size = (int)t.RawStripSize(s);
            _raw[s] = new byte[size];
            t.ReadRawStrip(s, _raw[s], 0, size);
            _plain[s] = new byte[_stripBytes];
            int got = Zlib2.InflateComponentAce(_raw[s], _plain[s]);
        }
        _out = new byte[_stripBytes + _stripBytes / 10 + 1024];
    }

    [Benchmark(Baseline = true)]
    public void Inflate_ComponentAce() { foreach (var r in _raw) Zlib2.InflateComponentAce(r, _out); }

    [Benchmark]
    public void Inflate_ZLibStream() { foreach (var r in _raw) Zlib.Inflate(r, r.Length, _out.AsSpan(0, _stripBytes)); }

    [Benchmark]
    public void Deflate6_ComponentAce() { foreach (var p in _plain) Zlib2.DeflateComponentAce(p, _out, 6); }

    [Benchmark]
    public void Deflate_ZLibStream_Optimal() { foreach (var p in _plain) Zlib.Deflate(p, _out, CompressionLevel.Optimal); }

    [Benchmark]
    public void Deflate_ZLibStream_Fastest() { foreach (var p in _plain) Zlib.Deflate(p, _out, CompressionLevel.Fastest); }
}

public static class Zlib2
{
    // As DeflateCodec does it: inflateInit per strip, inflate(Z_PARTIAL_FLUSH) until the strip is full.
    public static int InflateComponentAce(byte[] src, byte[] dst)
    {
        var z = new ZStream();
        z.inflateInit();
        z.next_in = src; z.next_in_index = 0; z.avail_in = src.Length;
        z.next_out = dst; z.next_out_index = 0; z.avail_out = dst.Length;
        do
        {
            int st = z.inflate(zlibConst.Z_PARTIAL_FLUSH);
            if (st == zlibConst.Z_STREAM_END) break;
            if (st != zlibConst.Z_OK) throw new InvalidDataException(st.ToString());
        } while (z.avail_out > 0);
        return dst.Length - z.avail_out;
    }

    public static int DeflateComponentAce(byte[] src, byte[] dst, int level)
    {
        var z = new ZStream();
        z.deflateInit(level);
        z.next_in = src; z.next_in_index = 0; z.avail_in = src.Length;
        z.next_out = dst; z.next_out_index = 0; z.avail_out = dst.Length;
        z.deflate(zlibConst.Z_NO_FLUSH);
        while (z.deflate(zlibConst.Z_FINISH) != zlibConst.Z_STREAM_END) { }
        int n = dst.Length - z.avail_out;
        z.deflateEnd();
        return n;
    }
}

[Config(typeof(MicroConfig))]
public class LzwBenchmarks
{
    byte[][] _raw = [];
    byte[] _out = [];

    [GlobalSetup]
    public void Setup()
    {
        using var t = Tiff.Open(Data.P("u16_24_lzw.tif"), "rc");
        _raw = new byte[t.NumberOfStrips()][];
        for (int s = 0; s < _raw.Length; s++)
        {
            _raw[s] = new byte[(int)t.RawStripSize(s)];
            t.ReadRawStrip(s, _raw[s], 0, _raw[s].Length);
        }
        _out = new byte[t.StripSize()];
        // correctness check of the prototype against LibTiff
        var expected = new byte[_out.Length];
        t.ReadEncodedStrip(0, expected, 0, expected.Length);
        Fast.LzwDecode(_raw[0], _out);
        if (!expected.AsSpan().SequenceEqual(_out)) throw new Exception("LZW prototype mismatch");
    }

    /// <summary>The whole file through LibTiff (decode + its raw read copy).</summary>
    [Benchmark(Baseline = true)]
    public void LibTiff_ReadEncodedStrip_all()
    {
        using var t = Tiff.Open(Data.P("u16_24_lzw.tif"), "rc");
        for (int s = 0; s < _raw.Length; s++) t.ReadEncodedStrip(s, _out, 0, -1);
    }

    struct CodeT { public int next; public short length; public byte value; public byte firstchar; }
    readonly CodeT[] _tab = new CodeT[4095 + 1024];

    /// <summary>Cost of the Array.Clear LZWCodec does on every CODE_CLEAR (34 per strip x 188 strips measured on this file).</summary>
    [Benchmark]
    public void ClearCost_only() { for (int i = 0; i < 188 * 34; i++) Array.Clear(_tab, 258, _tab.Length - 258); }

    [Benchmark]
    public void FastLzw_all() { foreach (var r in _raw) Fast.LzwDecode(r, _out); }
}
