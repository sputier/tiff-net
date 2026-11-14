using Eleris.TiffNet.Internal.Codecs;

namespace Eleris.TiffNet.Tests.Codecs;

/// <summary>LZW and PackBits decoded as streams give the bytes of the whole decoders, read in pieces of any size.</summary>
public class StreamDecoderTests
{
    public static TheoryData<string, bool> LzwCases() => new()
    {
        { "one", false }, { "runs", false }, { "random", false }, { "text", false }, { "long", false },
        { "random", true }, { "long", true },
    };

    [Theory]
    [MemberData(nameof(LzwCases))]
    public void LZW_as_a_stream_gives_the_bytes_of_the_whole_decoder(string name, bool oldStyle)
    {
        byte[] data = LzwTests.Sample(name);
        foreach (byte[] encoded in new[] { LzwTests.Encode(data, oldStyle), LzwTests.Encode(data, oldStyle, endCode: false) })
        {
            foreach (int piece in new[] { 1, 7, 4096, data.Length + 1 })
            {
                Assert.Equal(data, ReadAll(new LzwStream(new MemoryStream(encoded)), piece));
            }
        }
    }

    [Fact]
    public void LZW_going_on_with_a_full_table_streams_without_keeping_what_follows()
    {
        // Without clear codes once the table is full, a long stream refers only to the output before: decoded whole and as a stream.
        byte[] data = [.. LzwTests.Sample("long"), .. LzwTests.Sample("long"), .. LzwTests.Sample("random")];
        byte[] encoded = LzwTests.Encode(data, clearWhenFull: false);
        byte[] whole = new byte[data.Length];

        Assert.Equal(data.Length, Lzw.Decode(encoded, whole, out _));
        Assert.Equal(data, whole);
        Assert.Equal(data, ReadAll(new LzwStream(new MemoryStream(encoded)), 1000));
    }

    [Fact]
    public void Flat_LZW_fills_the_output_a_generation_can_keep_and_no_more()
    {
        // Zeros: each string one byte longer than the one before, the most output before the table is full (with clear codes,
        // as libtiff writes, and without).
        byte[] data = new byte[20 << 20];
        foreach (bool clear in new[] { true, false })
        {
            byte[] encoded = LzwTests.Encode(data, clearWhenFull: clear);
            Assert.Equal(data, ReadAll(new LzwStream(new MemoryStream(encoded)), 1 << 20));
        }
    }

    [Fact]
    public void Damaged_LZW_fails_as_a_stream_too()
    {
        Assert.Throws<InvalidDataException>(() => ReadAll(new LzwStream(new MemoryStream(LzwTests.Pack(256, 65, 300))), 16));
        Assert.Throws<InvalidDataException>(() => ReadAll(new LzwStream(new MemoryStream(LzwTests.Pack(256, 258))), 16));
    }

    [Fact]
    public void PackBits_as_a_stream_gives_the_bytes_of_the_whole_decoder()
    {
        var random = new Random(125);
        byte[] row = [.. Enumerable.Range(0, 20_000).Select(i => i % 300 < 150 ? (byte)(i / 40) : (byte)random.Next(256))];
        byte[] encoded = new byte[PackBitsEncoder.MaxEncoded(row.Length)];
        int length = PackBitsEncoder.EncodeRow(row, encoded);
        // No-operations here and there, and data cut in a literal run then in a repeat.
        byte[] noisy = [0x80, .. encoded[..length], 0x80, 0x80];
        foreach (byte[] stream in new[] { encoded[..length], noisy, encoded[..(length / 2)], [0x05, 1, 2], [0xF0] })
        {
            byte[] whole = new byte[row.Length + 100];
            int written = PackBits.Decode(stream, whole, out _);
            foreach (int piece in new[] { 1, 3, 129, 100_000 })
            {
                Assert.Equal(whole[..written], ReadAll(new PackBitsStream(new MemoryStream(stream)), piece));
            }
        }
    }

    /// <summary>LZW codes packed most significant bit first, with the width of the codes growing as the table does (early change).</summary>
    private static byte[] PackWidths(IEnumerable<int> codes)
    {
        var output = new List<byte>();
        ulong accumulator = 0;
        int bits = 0, width = 9, free = 258, previous = -1;
        foreach (int code in codes)
        {
            accumulator = (accumulator << width) | (uint)code;
            bits += width;
            while (bits >= 8)
            {
                output.Add((byte)(accumulator >> (bits - 8)));
                bits -= 8;
                accumulator &= (1UL << bits) - 1;
            }

            if (code == 256)
            {
                (width, free, previous) = (9, 258, -1);
            }
            else if (previous == -1)
            {
                previous = code;
            }
            else
            {
                if (free < 4096 && ++free >= (1 << width) - 1 && width < 12)
                {
                    width++;
                }

                previous = code;
            }
        }

        if (bits > 0)
        {
            output.Add((byte)(accumulator << (8 - bits)));
        }

        return [.. output];
    }

    [Theory]
    [InlineData(1 << 20)]
    [InlineData(700_000)]
    public void A_flat_generation_after_a_clear_code_with_bytes_not_given_yet_is_decoded_as_a_whole(int piece)
    {
        // A generation of zeros that fills the table (3 838 codes of 2 to 3 839 bytes, 7.4 MB) and goes on with its longest string,
        // a clear code while a batch of output waits to be given, and again: the output kept is the batch left, the generation
        // and the next batch. The stream must give what Lzw.Decode gives, not refuse a valid file. How much waits at the clear
        // code depends on where the batches end, so the length of the first generation is varied over more than a batch.
        static IEnumerable<int> Generation(int repeats) => [256, 0, .. Enumerable.Range(258, 4096 - 258), .. Enumerable.Repeat(4095, repeats)];
        byte[] whole = new byte[24 << 20];
        for (int repeats = 0; repeats < 280; repeats += 9)
        {
            byte[] encoded = PackWidths([.. Generation(repeats), .. Generation(400), 257]);
            int length = Lzw.Decode(encoded, whole, out bool overflow);

            Assert.False(overflow);
            Assert.True(length > 14 << 20);
            Assert.True(whole.AsSpan(0, length).SequenceEqual(ReadAll(new LzwStream(new MemoryStream(encoded)), piece)), $"{repeats} repeats");
        }
    }

    [Theory]
    [InlineData(TiffCompression.Lzw)]
    [InlineData(TiffCompression.PackBits)]
    public void A_large_strip_is_decoded_as_a_stream_within_a_budget_below_its_size(TiffCompression compression)
    {
        // One strip of 21.6 MB (u16 RGB, LZW with the horizontal predictor, or PackBits), read as floats: the budget holds the
        // floats and 13 MB (the stream of LZW keeps up to 9.6 MB), not the strip decoded whole besides them.
        const int width = 2400, height = 1500;
        var random = new Random(126);
        ushort[] samples = [.. Enumerable.Range(0, width * height * 3).Select(i => (ushort)(i / 3 % width * 20 + random.Next(30)))];
        byte[] raw = System.Runtime.InteropServices.MemoryMarshal.AsBytes(samples.AsSpan()).ToArray();
        int rowBytes = width * 3 * 2;
        byte[] data;
        if (compression == TiffCompression.Lzw)
        {
            byte[] predicted = [.. raw];
            for (int y = 0; y < height; y++)
            {
                PredictorEncoder.Horizontal(predicted.AsSpan(y * rowBytes, rowBytes), 2, 3);
            }

            data = new byte[LzwEncoder.MaxEncoded(predicted.Length)];
            data = data[..LzwEncoder.Encode(predicted, data)];
        }
        else
        {
            using var output = new MemoryStream();
            byte[] buffer = new byte[PackBitsEncoder.MaxEncoded(rowBytes)];
            for (int y = 0; y < height; y++)
            {
                output.Write(buffer, 0, PackBitsEncoder.EncodeRow(raw.AsSpan(y * rowBytes, rowBytes), buffer));
            }

            data = output.ToArray();
        }

        var builder = new TiffBuilder { Data = data };
        var directory = builder.AddDirectory().Image(width, height, bits: 16, samples: 3, photometric: 2).Remove(259)
            .Add(259, TiffTagType.Short, compression == TiffCompression.Lzw ? 5 : 32773).Remove(279).Add(279, TiffTagType.Long, data.Length);
        if (compression == TiffCompression.Lzw)
        {
            directory.Add(317, TiffTagType.Short, 2);
        }

        directory.Sorted();
        long floats = (long)width * height * 3 * 4;
        var options = TiffReadOptions.Default with { MaxPixelBytes = floats + (13 << 20) };
        using var file = TiffFile.Read(new MemoryStream(builder.Build()), options);

        Assert.Equal(samples.Select(v => (float)v), file.Primary.ReadPixels<float>());
        Assert.Equal(samples.Skip((500 * width + 10) * 3).Take(5 * 3).Select(v => (float)v), file.Primary.ReadRegion<float>(10, 500, 5, 1));
        Assert.Equal(samples, file.Primary.ReadRawPixels<ushort>());
    }

    private static byte[] ReadAll(Stream stream, int piece)
    {
        using var output = new MemoryStream();
        byte[] buffer = new byte[piece];
        int got;
        while ((got = stream.Read(buffer)) > 0)
        {
            output.Write(buffer, 0, got);
        }

        stream.Dispose();
        return output.ToArray();
    }
}
