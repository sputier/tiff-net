using Eleris.TiffNet.Internal.Codecs;

namespace Eleris.TiffNet.Tests.Codecs;

public class LzwTests
{
    /// <summary>
    /// An LZW encoder for the tests: TIFF 6.0 (most significant bit first, width growing one code early), or the old style
    /// (least significant bit first, width growing on time). It clears the table when it is full.
    /// </summary>
    internal static byte[] Encode(ReadOnlySpan<byte> data, bool oldStyle = false, bool endCode = true, bool clearWhenFull = true)
    {
        var output = new List<byte>();
        ulong bits = 0;
        int count = 0, width = 9;
        void Put(int code)
        {
            if (oldStyle)
            {
                bits |= (ulong)code << count;
                count += width;
                while (count >= 8)
                {
                    output.Add((byte)bits);
                    bits >>= 8;
                    count -= 8;
                }
            }
            else
            {
                bits = (bits << width) | (uint)code;
                count += width;
                while (count >= 8)
                {
                    output.Add((byte)(bits >> (count - 8)));
                    count -= 8;
                }
            }
        }

        // The decoder adds an entry one code after the encoder: the encoder grows the width one entry later than the decoder does.
        int threshold(int w) => (1 << w) + 1 - (oldStyle ? 0 : 1);
        var table = new Dictionary<(int, byte), int>();
        int free = 258;
        Put(256);
        int current = -1;
        foreach (byte b in data)
        {
            if (current == -1)
            {
                current = b;
                continue;
            }

            if (table.TryGetValue((current, b), out int code))
            {
                current = code;
                continue;
            }

            Put(current);
            if (free < 4096)
            {
                table[(current, b)] = free;
            }

            free++;
            current = b;
            if (free >= threshold(width) && width < 12)
            {
                width++;
            }

            if (free == 4094 && clearWhenFull)
            {
                Put(256);
                table.Clear();
                free = 258;
                width = 9;
            }
        }

        if (current != -1)
        {
            Put(current);
            // The decoder adds an entry for this code too (libtiff's LZWPostEncode): the table may be full (a clear code, back to
            // 9 bits, when the encoder clears a full table) or the width may grow before the end code.
            if (++free == 4094 && clearWhenFull)
            {
                Put(256);
                width = 9;
            }
            else if (free >= threshold(width) && width < 12)
            {
                width++;
            }
        }

        if (endCode)
        {
            Put(257);
        }

        if (count > 0)
        {
            output.Add(oldStyle ? (byte)bits : (byte)(bits << (8 - count)));
        }

        return [.. output];
    }

    public static TheoryData<string, bool> Cases()
    {
        var data = new TheoryData<string, bool>();
        foreach (string name in new[] { "empty", "one", "runs", "random", "text", "long" })
        {
            data.Add(name, false);
            data.Add(name, true);
        }

        return data;
    }

    internal static byte[] Sample(string name)
    {
        var random = new Random(name.Length);
        return name switch
        {
            "empty" => [],
            "one" => [42],
            "runs" => [.. Enumerable.Range(0, 20_000).Select(i => (byte)(i / 1000))],
            "random" => [.. Enumerable.Range(0, 50_000).Select(_ => (byte)random.Next(256))],
            "text" => System.Text.Encoding.ASCII.GetBytes(string.Concat(Enumerable.Repeat("TOBEORNOTTOBEORTOBEORNOT#", 400))),
            _ => [.. Enumerable.Range(0, 300_000).Select(_ => (byte)random.Next(4))],   // tables cleared many times
        };
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Encoded_data_is_decoded_back(string name, bool oldStyle)
    {
        byte[] data = Sample(name);
        byte[] encoded = Encode(data, oldStyle);
        byte[] decoded = new byte[data.Length];

        Assert.Equal(data.Length, Lzw.Decode(encoded, decoded, out bool overflow));
        Assert.Equal(data, decoded);
        Assert.False(overflow);
    }

    [Fact]
    public void A_stream_without_an_end_code_is_decoded()
    {
        byte[] data = Sample("text");
        byte[] decoded = new byte[data.Length];

        Assert.Equal(data.Length, Lzw.Decode(Encode(data, endCode: false), decoded, out _));
        Assert.Equal(data, decoded);
    }

    [Fact]
    public void The_output_is_bounded_by_the_destination()
    {
        byte[] data = Sample("runs");
        byte[] encoded = Encode(data);
        byte[] decoded = new byte[1000];

        Assert.Equal(1000, Lzw.Decode(encoded, decoded, out bool overflow));
        Assert.True(overflow);
        Assert.Equal(data[..1000], decoded);
    }

    [Fact]
    public void A_short_stream_decodes_to_fewer_bytes()
    {
        byte[] data = Sample("random");
        byte[] encoded = Encode(data);
        byte[] decoded = new byte[data.Length];

        int written = Lzw.Decode(encoded.AsSpan(0, encoded.Length / 2), decoded, out bool overflow);

        Assert.InRange(written, 1, data.Length - 1);
        Assert.False(overflow);
        Assert.Equal(data[..written], decoded[..written]);
    }

    [Fact]
    public void An_undefined_code_is_damaged_data()
    {
        Assert.Throws<InvalidDataException>(() => Lzw.Decode(Pack(256, 65, 300), new byte[16], out _));   // 258 is the next code
        Assert.Throws<InvalidDataException>(() => Lzw.Decode(Pack(256, 258), new byte[16], out _));        // a byte after a clear
    }

    /// <summary>Codes of 9 bits, most significant bit first.</summary>
    internal static byte[] Pack(params int[] codes)
    {
        var bits = string.Concat(codes.Select(c => Convert.ToString(c, 2).PadLeft(9, '0')));
        bits = bits.PadRight((bits.Length + 7) / 8 * 8, '0');
        return [.. Enumerable.Range(0, bits.Length / 8).Select(i => Convert.ToByte(bits.Substring(i * 8, 8), 2))];
    }

    [Fact]
    public void A_stream_going_on_with_a_full_table_keeps_the_table_as_it_is()
    {
        // libtiff refuses it; it is decoded as written (the codes after the full table are defined), which is safe.
        byte[] data = Sample("long");
        byte[] decoded = new byte[data.Length];

        Assert.Equal(data.Length, Lzw.Decode(Encode(data, clearWhenFull: false), decoded, out _));
        Assert.Equal(data, decoded);
    }

    [Fact]
    public void A_clear_code_after_a_clear_code_is_accepted()
    {
        byte[] decoded = new byte[2];

        Assert.Equal(2, Lzw.Decode(Pack(256, 256, 65, 66, 257), decoded, out _));
        Assert.Equal("AB"u8.ToArray(), decoded);
    }

    [Fact]
    public void Short_strings_are_decoded_at_every_place_of_the_output()
    {
        // Short strings (copied 16 bytes at a time when the output has room) and runs being defined, ending at every distance
        // from the end of the output, and cut by it: the bytes are those encoded, and nothing is written beyond the destination.
        var random = new Random(105);
        foreach (var (length, oldStyle) in Enumerable.Range(1, 90).Concat([255, 256, 257, 1000, 4097]).SelectMany(l => new[] { (l, false), (l, true) }))
        {
            byte[] data = [.. Enumerable.Range(0, length).Select(i => i % 37 < 20 ? (byte)random.Next(3) : (byte)(i % 5))];
            byte[] encoded = Encode(data, oldStyle);
            foreach (int size in new[] { length, length - 1, length - 15, length - 16, length - 17, length / 2 }.Where(s => s > 0).Distinct())
            {
                byte[] guarded = new byte[size + 32];
                Array.Fill(guarded, (byte)0xEE);
                int written = Lzw.Decode(encoded, guarded.AsSpan(0, size), out bool overflow);

                Assert.Equal(size, written);
                Assert.Equal(size < length, overflow);
                Assert.Equal(data[..size], guarded[..size]);
                Assert.All(guarded[size..], b => Assert.Equal(0xEE, b));
            }
        }
    }

    [Fact]
    public void Long_runs_and_strings_cut_by_the_destination_are_decoded()
    {
        // A run of one value gives strings of growing length, each defined by the code being read (the "KwKwK" case).
        byte[] data = [.. Enumerable.Repeat((byte)7, 200_000), .. Sample("random")[..1000], .. Enumerable.Repeat((byte)9, 50_000)];
        byte[] encoded = Encode(data);
        byte[] whole = new byte[data.Length];
        Assert.Equal(data.Length, Lzw.Decode(encoded, whole, out _));
        Assert.Equal(data, whole);

        foreach (int size in new[] { 1, 17, 1000, 100_123, 200_500 })
        {
            byte[] part = new byte[size];
            Assert.Equal(size, Lzw.Decode(encoded, part, out bool overflow));
            Assert.True(overflow);
            Assert.Equal(data[..size], part);
        }
    }
}
