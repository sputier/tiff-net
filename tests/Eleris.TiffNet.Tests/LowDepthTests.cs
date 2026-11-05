namespace Eleris.TiffNet.Tests;

/// <summary>Low depths, MinIsWhite and palettes, built byte by byte.</summary>
public class LowDepthTests
{
    private static TiffImage Open(TiffBuilder builder) => TiffFile.Read(new MemoryStream(builder.Build())).Primary;

    /// <summary>Values of <paramref name="bits"/> bits packed most significant bit first, each row padded to a byte.</summary>
    private static byte[] Pack(int[] values, int bits, int width)
    {
        var stream = new List<bool>();
        for (int i = 0; i < values.Length; i++)
        {
            for (int b = bits - 1; b >= 0; b--)
            {
                stream.Add(((values[i] >> b) & 1) != 0);
            }

            if ((i + 1) % width == 0)
            {
                while (stream.Count % 8 != 0)
                {
                    stream.Add(false);
                }
            }
        }

        return [.. stream.Chunk(8).Select(c => (byte)c.Select((bit, k) => bit ? 1 << (7 - k) : 0).Sum())];
    }

    private static byte Reverse(byte b) => (byte)Enumerable.Range(0, 8).Sum(k => ((b >> k) & 1) << (7 - k));

    public static TheoryData<int, bool> Depths()
    {
        var data = new TheoryData<int, bool>();
        foreach (int bits in Enumerable.Range(1, 16).Where(b => b is not 8 and not 16))
        {
            data.Add(bits, false);
            data.Add(bits, true);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Depths))]
    public void Low_depths_are_unpacked_in_both_fill_orders_and_normalised_by_their_maximum(int bits, bool reversed)
    {
        const int width = 7, height = 3;
        int max = (1 << bits) - 1;
        int[] values = [.. Enumerable.Range(0, width * height).Select(i => i * 7919 % (max + 1))];
        values[0] = max;
        byte[] packed = Pack(values, bits, width);
        var builder = new TiffBuilder { Data = reversed ? [.. packed.Select(Reverse)] : packed };
        builder.AddDirectory().Image(width, height, bits: (ushort)bits).Remove(279).Add(279, TiffTagType.Long, packed.Length)
            .Add(266, TiffTagType.Short, reversed ? 2 : 1);
        var image = Open(builder);

        Assert.Equal(bits <= 8 ? TiffSampleType.UInt8 : TiffSampleType.UInt16, image.SampleType);
        Assert.Equal(values.Select(v => (double)v), image.ReadPixels<double>());
        Assert.Equal(values.Select(v => v / (double)max), image.ReadPixels<double>(TiffPixelScale.Normalized));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    [InlineData(12)]
    public void MinIsWhite_low_depths_are_inverted(int bits)
    {
        int max = (1 << bits) - 1;
        int[] values = [0, max, 1, max - 1];
        var builder = new TiffBuilder { Data = Pack(values, bits, 4) };
        builder.AddDirectory().Image(4, 1, bits: (ushort)bits, photometric: 0).Remove(279).Add(279, TiffTagType.Long, Pack(values, bits, 4).Length);

        Assert.Equal(values.Select(v => (double)(max - v)), Open(builder).ReadPixels<double>());
    }

    [Theory]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(8)]
    public void A_palette_is_expanded_to_16_bit_RGB(int bits)
    {
        int entries = 1 << bits;
        long[] map = [.. Enumerable.Range(0, 3 * entries).Select(i => (long)(i * 997 % 65536))];
        int[] indices = [0, entries - 1, 1, entries / 2];
        var builder = new TiffBuilder { Data = Pack(indices, bits, 4) };
        builder.AddDirectory().Image(4, 1, bits: (ushort)bits, photometric: 3).Remove(279).Add(279, TiffTagType.Long, Pack(indices, bits, 4).Length)
            .Add(320, TiffTagType.Short, map);
        var image = Open(builder);

        Assert.Equal((TiffColorModel.Rgb, TiffSampleType.UInt16, 3), (image.ColorModel, image.SampleType, image.Channels));
        Assert.Equal(indices.SelectMany(i => new[] { (ushort)map[i], (ushort)map[entries + i], (ushort)map[2 * entries + i] }), image.ReadRawPixels<ushort>());
    }
}
