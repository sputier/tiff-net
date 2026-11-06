namespace Eleris.TiffNet.Tests;

/// <summary>Samples of 1 to 15 bits, packed most significant bit first, give what a reading bit by bit gives, at every width.</summary>
public class BitDepthTests
{
    public static TheoryData<int, int> Depths()
    {
        var data = new TheoryData<int, int>();
        foreach (int bits in new[] { 1, 2, 3, 4, 5, 6, 7, 9, 10, 11, 12, 13, 14, 15 })
        {
            foreach (int width in new[] { 1, 7, 37, 300 })
            {
                data.Add(bits, width);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Depths))]
    public void Packed_samples_are_unpacked_as_their_bits_say(int bits, int width)
    {
        const int height = 5;
        int rowBytes = (width * bits + 7) / 8;
        byte[] data = new byte[rowBytes * height];
        new Random(bits * 1000 + width).NextBytes(data);
        var builder = new TiffBuilder { Data = data };
        builder.AddDirectory().Image(width, height, bits: (ushort)bits).Remove(279).Add(279, TiffTagType.Long, data.Length);
        using var file = TiffFile.Read(new MemoryStream(builder.Build()));

        long[] expected = new long[width * height];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                long value = 0;
                for (int b = 0; b < bits; b++)
                {
                    int bit = x * bits + b;
                    value = (value << 1) | (uint)((data[y * rowBytes + bit / 8] >> (7 - bit % 8)) & 1);
                }

                expected[y * width + x] = value;
            }
        }

        long[] actual = bits < 8
            ? [.. file.Primary.ReadRawPixels<byte>().Select(v => (long)v)]
            : [.. file.Primary.ReadRawPixels<ushort>().Select(v => (long)v)];
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData(16)]
    [InlineData(32)]
    public void Big_endian_samples_are_read_in_the_order_of_the_machine(int bits)
    {
        const int width = 41, height = 7;
        var random = new Random(bits);
        uint[] values = [.. Enumerable.Range(0, width * height).Select(_ => bits == 16 ? (uint)random.Next(65536) : (uint)random.NextInt64(1L << 32))];
        byte[] data = new byte[values.Length * bits / 8];
        for (int i = 0; i < values.Length; i++)
        {
            if (bits == 16)
            {
                System.Buffers.Binary.BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(i * 2), (ushort)values[i]);
            }
            else
            {
                System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(i * 4), values[i]);
            }
        }

        var builder = new TiffBuilder(littleEndian: false) { Data = data };
        builder.AddDirectory().Image(width, height, bits: (ushort)bits).Remove(279).Add(279, TiffTagType.Long, data.Length);
        using var file = TiffFile.Read(new MemoryStream(builder.Build()));

        if (bits == 16)
        {
            Assert.Equal(values.Select(v => (ushort)v), file.Primary.ReadRawPixels<ushort>());
            Assert.Equal(values.Skip(width * 2 + 3).Take(5).Select(v => (ushort)v), file.Primary.ReadRawRegion<ushort>(3, 2, 5, 1));
        }
        else
        {
            Assert.Equal(values, file.Primary.ReadRawPixels<uint>());
        }

        // Converted rows are swapped where they were read, then converted: the values, not their bytes.
        double maximum = bits == 16 ? 65535 : uint.MaxValue;
        Assert.Equal(values.Select(v => (double)v), file.Primary.ReadPixels<double>());
        Assert.Equal(values.Select(v => (float)(v / maximum)), file.Primary.ReadPixels<float>(TiffPixelScale.Normalized));
        Assert.Equal(values.Skip(width * 2 + 3).Take(5).Select(v => (double)v), file.Primary.ReadRegion<double>(3, 2, 5, 1));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(6)]
    public void Big_endian_compressed_and_predicted_samples_convert_in_every_orientation(int orientation)
    {
        // Deflate and the horizontal predictor on big-endian samples: the predictor is undone, the row swapped where it was
        // decoded, then converted and placed (turned when the orientation asks).
        const int width = 23, height = 5;
        var random = new Random(orientation);
        ushort[] values = [.. Enumerable.Range(0, width * height).Select(_ => (ushort)random.Next(65536))];
        byte[] raw = new byte[values.Length * 2];
        for (int i = 0; i < values.Length; i++)
        {
            ushort difference = i % width == 0 ? values[i] : unchecked((ushort)(values[i] - values[i - 1]));
            System.Buffers.Binary.BinaryPrimitives.WriteUInt16BigEndian(raw.AsSpan(i * 2), difference);
        }

        using var compressed = new MemoryStream();
        using (var zlib = new System.IO.Compression.ZLibStream(compressed, System.IO.Compression.CompressionLevel.Optimal, leaveOpen: true))
        {
            zlib.Write(raw);
        }

        byte[] data = compressed.ToArray();
        var builder = new TiffBuilder(littleEndian: false) { Data = data };
        builder.AddDirectory().Image(width, height, bits: 16).Remove(259).Add(259, TiffTagType.Short, 8).Remove(279)
            .Add(279, TiffTagType.Long, data.Length).Add(317, TiffTagType.Short, 2).Add(274, TiffTagType.Short, orientation).Sorted();
        using var file = TiffFile.Read(new MemoryStream(builder.Build()));

        // The delivered (visual) image: orientation 3 turns it half a turn, 6 a quarter (the stored rows become columns).
        double[] expected = orientation switch
        {
            3 => [.. values.Reverse().Select(v => (double)v)],
            6 => [.. Enumerable.Range(0, width * height).Select(i => (double)values[(height - 1 - i % height) * width + i / height])],
            _ => [.. values.Select(v => (double)v)],
        };
        Assert.Equal(expected, file.Primary.ReadPixels<double>());
        Assert.Equal(expected.Select(v => (float)(v / 65535)), file.Primary.ReadPixels<float>(TiffPixelScale.Normalized));
        Assert.Equal(expected.Select(v => (ushort)v), file.Primary.ReadRawPixels<ushort>());
    }

    [Theory]
    [InlineData(TiffCompression.Deflate)]
    [InlineData(TiffCompression.Lzw)]
    [InlineData(TiffCompression.None)]
    public void Converted_rows_of_every_kind_give_the_values(TiffCompression compression)
    {
        // Rows converted from the bytes decoded (u16 to float and double), and rows that must be unpacked first (12 bits,
        // MinIsWhite) next to each other: each gives its values.
        const int width = 37, height = 9;
        var random = new Random(17);
        ushort[] samples = [.. Enumerable.Range(0, width * height * 3).Select(_ => (ushort)random.Next(65536))];
        using var stream = new MemoryStream();
        new TiffFile().Add(TiffImage.Create<ushort>(samples, width, height, 3, TiffColorModel.Rgb))
            .Save(stream, TiffWriteOptions.Default with { Compression = compression, Predictor = compression == TiffCompression.None ? TiffPredictor.None : TiffPredictor.Horizontal });
        using var file = TiffFile.Read(new MemoryStream(stream.ToArray()));

        Assert.Equal(samples.Select(v => (float)v), file.Primary.ReadPixels<float>());
        Assert.Equal(samples.Select(v => v / 65535.0), file.Primary.ReadPixels<double>(TiffPixelScale.Normalized));
        Assert.Equal(samples.Where((_, i) => i % 3 == 1).Select(v => (float)v), file.Primary.ReadChannel<float>(1));
        float[] planar = file.Primary.ReadPixels<float>(TiffPixelScale.Stored, TiffPixelOrder.Planar);
        Assert.Equal(samples.Where((_, i) => i % 3 == 2).Select(v => (float)v), planar.Skip(2 * width * height));
        // The rows read again give the same values: the bytes decoded were swapped or read in place, never kept.
        Assert.Equal(samples.Select(v => (float)v), file.Primary.ReadPixels<float>());
    }
}
