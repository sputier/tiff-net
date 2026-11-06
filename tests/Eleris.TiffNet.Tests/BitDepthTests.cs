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
    }
}
