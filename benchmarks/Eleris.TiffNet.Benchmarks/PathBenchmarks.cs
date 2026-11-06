using BenchmarkDotNet.Attributes;

namespace Eleris.TiffNet.Benchmarks;

/// <summary>
/// The paths of real astronomical files that <see cref="TargetBenchmarks"/> does not cover: planar files (PixInsight writes them),
/// planar output (a FITS cube), normalised and converted reads, 12-bit and big-endian samples. 24 MP, uncompressed, so that the
/// cost measured is that of unpacking and placing the samples, not of a codec. Results: benchmarks/results.
/// </summary>
[MemoryDiagnoser]
public class PathBenchmarks
{
    private const int Width = 6000, Height = 4000;
    private TiffFile _file = null!;

    /// <summary>The stored image: u16 RGB interleaved or planar, u16 grey big-endian, 12-bit grey.</summary>
    [Params("u16-rgb", "u16-rgb-planar", "u16-big-endian", "u12")]
    public string Source { get; set; } = "u16-rgb";

    [GlobalSetup]
    public void Setup()
    {
        byte[] bytes = Source switch
        {
            "u16-rgb" => Write(3, TiffPixelOrder.Interleaved),
            "u16-rgb-planar" => Write(3, TiffPixelOrder.Planar),
            "u16-big-endian" => RawFile(16, bigEndian: true),
            _ => RawFile(12, bigEndian: true),
        };
        _file = TiffFile.Read(new MemoryStream(bytes), TiffReadOptions.Default with { MaxPixelBytes = 1L << 31, MaxInputBytes = 1L << 31 });
    }

    [GlobalCleanup]
    public void Cleanup() => _file.Dispose();

    [Benchmark]
    public int Raw() => _file.Primary.ReadRawPixels<ushort>().Length;

    [Benchmark]
    public int Planar() => _file.Primary.ReadRawPixels<ushort>(TiffPixelOrder.Planar).Length;

    [Benchmark]
    public int NormalizedFloat() => _file.Primary.ReadPixels<float>(TiffPixelScale.Normalized).Length;

    [Benchmark]
    public int StoredDouble() => _file.Primary.ReadPixels<double>().Length;

    private static byte[] Write(int channels, TiffPixelOrder order)
    {
        var random = new Random(1);
        var samples = new ushort[Width * Height * channels];
        for (int i = 0; i < samples.Length; i++)
        {
            samples[i] = (ushort)(1000 + random.Next(4000));
        }

        using var stream = new MemoryStream();
        new TiffFile().Add(TiffImage.Create<ushort>(samples, Width, Height, channels, channels == 3 ? TiffColorModel.Rgb : null))
            .Save(stream, TiffWriteOptions.Default with { Compression = TiffCompression.None, PlanarConfiguration = order });
        return stream.ToArray();
    }

    /// <summary>A grey image of <paramref name="bits"/>-bit samples, uncompressed, in one strip, written by hand.</summary>
    private static byte[] RawFile(int bits, bool bigEndian)
    {
        long rowBytes = ((long)Width * bits + 7) / 8;
        byte[] data = new byte[rowBytes * Height];
        new Random(2).NextBytes(data);
        using var stream = new MemoryStream();
        using var w = new BinaryWriter(stream);
        void U16(int v) => w.Write(bigEndian ? System.Buffers.Binary.BinaryPrimitives.ReverseEndianness((ushort)v) : (ushort)v);
        void U32(long v) => w.Write(bigEndian ? System.Buffers.Binary.BinaryPrimitives.ReverseEndianness((uint)v) : (uint)v);
        w.Write(bigEndian ? "MM"u8 : "II"u8);
        U16(42);
        U32(8);
        (int Tag, int Type, long Value)[] entries =
        [
            (256, 4, Width), (257, 4, Height), (258, 3, bits), (259, 3, 1), (262, 3, 1), (273, 4, 8 + 2 + 9 * 12 + 4),
            (277, 3, 1), (278, 4, Height), (279, 4, data.Length),
        ];
        U16(entries.Length);
        foreach (var (tag, type, value) in entries)
        {
            U16(tag);
            U16(type);
            U32(1);
            if (type == 3)
            {
                U16((int)value);
                U16(0);
            }
            else
            {
                U32(value);
            }
        }

        U32(0);
        w.Write(data);
        w.Flush();
        return stream.ToArray();
    }
}
