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
    private string? _path;

    /// <summary>The stored image: u16 RGB interleaved or planar, u16 grey big-endian, 12-bit grey.</summary>
    [Params("u16-rgb", "u16-rgb-planar", "u16-big-endian", "u12", "u16-small-strips", "u16-small-deflate")]
    public string Source { get; set; } = "u16-rgb";

    [GlobalSetup]
    public void Setup()
    {
        byte[] bytes = Source switch
        {
            "u16-rgb" => Write(3, TiffPixelOrder.Interleaved),
            "u16-rgb-planar" => Write(3, TiffPixelOrder.Planar),
            "u16-big-endian" => RawFile(16, bigEndian: true),
            "u16-small-strips" => SmallStrips(deflate: false),
            "u16-small-deflate" => SmallStrips(deflate: true),
            _ => RawFile(12, bigEndian: true),
        };
        var options = TiffReadOptions.Default with { MaxPixelBytes = 1L << 31, MaxInputBytes = 1L << 31 };
        if (Source.StartsWith("u16-small", StringComparison.Ordinal))
        {
            // Files of small strips are read from a file (in the cache of the system): what counts is the number of reads.
            _path = Path.Combine(Path.GetTempPath(), $"tiffnet-bench-{Source}.tif");
            File.WriteAllBytes(_path, bytes);
            _file = TiffFile.Open(_path, options);
            return;
        }

        _file = TiffFile.Read(new MemoryStream(bytes), options);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _file.Dispose();
        if (_path is not null)
        {
            File.Delete(_path);
        }
    }

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

    /// <summary>A u16 grey image in strips of one row (12 KB, as libtiff writes 8 KB strips), uncompressed or Deflate.</summary>
    private static byte[] SmallStrips(bool deflate)
    {
        var random = new Random(3);
        var strips = new List<byte[]>();
        for (int y = 0; y < Height; y++)
        {
            byte[] row = new byte[Width * 2];
            for (int x = 0; x < Width; x++)
            {
                System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(row.AsSpan(x * 2), (ushort)(1000 + x / 4 + y / 8 + random.Next(16)));
            }

            if (deflate)
            {
                using var compressed = new MemoryStream();
                using (var z = new System.IO.Compression.ZLibStream(compressed, System.IO.Compression.CompressionLevel.Fastest))
                {
                    z.Write(row);
                }

                row = compressed.ToArray();
            }

            strips.Add(row);
        }

        using var stream = new MemoryStream();
        using var w = new BinaryWriter(stream);
        const int entries = 9;
        long ifd = 8, tables = ifd + 2 + entries * 12 + 4, data = tables + 8L * Height;
        w.Write("II"u8);
        w.Write((ushort)42);
        w.Write((uint)ifd);
        w.Write((ushort)entries);
        void Entry(int tag, int type, long count, long value)
        {
            w.Write((ushort)tag);
            w.Write((ushort)type);
            w.Write((uint)count);
            if (type == 3 && count == 1)
            {
                w.Write((ushort)value);
                w.Write((ushort)0);
            }
            else
            {
                w.Write((uint)value);
            }
        }

        Entry(256, 4, 1, Width);
        Entry(257, 4, 1, Height);
        Entry(258, 3, 1, 16);
        Entry(259, 3, 1, deflate ? 8 : 1);
        Entry(262, 3, 1, 1);
        Entry(273, 4, Height, tables);
        Entry(277, 3, 1, 1);
        Entry(278, 4, 1, 1);
        Entry(279, 4, Height, tables + 4L * Height);
        w.Write(0u);
        long offset = data;
        foreach (var strip in strips)
        {
            w.Write((uint)offset);
            offset += strip.Length;
        }

        foreach (var strip in strips)
        {
            w.Write((uint)strip.Length);
        }

        foreach (var strip in strips)
        {
            w.Write(strip);
        }

        w.Flush();
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
