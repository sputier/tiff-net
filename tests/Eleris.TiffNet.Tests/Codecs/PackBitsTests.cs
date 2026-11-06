using Eleris.TiffNet.Internal.Codecs;

namespace Eleris.TiffNet.Tests.Codecs;

public class PackBitsTests
{
    [Fact]
    public void Literal_and_repeated_runs_are_decoded()
    {
        // The example of TIFF 6.0, section 9.
        byte[] packed = [0xFE, 0xAA, 0x02, 0x80, 0x00, 0x2A, 0xFD, 0xAA, 0x03, 0x80, 0x00, 0x2A, 0x22, 0xF7, 0xAA];
        byte[] expected = [0xAA, 0xAA, 0xAA, 0x80, 0x00, 0x2A, 0xAA, 0xAA, 0xAA, 0xAA, 0x80, 0x00, 0x2A, 0x22, .. Enumerable.Repeat((byte)0xAA, 10)];
        byte[] decoded = new byte[expected.Length];

        Assert.Equal(expected.Length, PackBits.Decode(packed, decoded, out bool overflow));
        Assert.Equal(expected, decoded);
        Assert.False(overflow);
    }

    [Fact]
    public void A_no_operation_is_skipped()
    {
        byte[] decoded = new byte[2];

        Assert.Equal(2, PackBits.Decode([0x80, 0x01, 1, 2, 0x80], decoded, out bool overflow));
        Assert.Equal(new byte[] { 1, 2 }, decoded);
        Assert.False(overflow);
    }

    [Fact]
    public void The_output_is_bounded_by_the_destination()
    {
        byte[] decoded = new byte[5];

        Assert.Equal(5, PackBits.Decode([0x81, 7], decoded, out bool overflow));   // 128 copies of 7
        Assert.True(overflow);
        Assert.Equal(5, PackBits.Decode([0x09, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10], decoded, out overflow));
        Assert.True(overflow);
        Assert.Equal(5, PackBits.Decode([0x04, 1, 2, 3, 4, 5, 0x00, 6], decoded, out overflow));
        Assert.True(overflow);
    }

    [Fact]
    public void A_cut_run_gives_what_there_is()
    {
        byte[] decoded = new byte[10];

        Assert.Equal(3, PackBits.Decode([0x05, 1, 2, 3], decoded, out bool overflow));
        Assert.False(overflow);
        Assert.Equal(0, PackBits.Decode([0xF0], decoded, out _));
    }

    [Fact]
    public void Rows_encode_as_the_byte_by_byte_encoder_does_and_decode_back()
    {
        // Runs found with vectors give the bytes of the encoder byte by byte (kept below as the reference); the decoder, with
        // its 16-byte copies, gives the row back and writes nothing beyond the destination.
        var random = new Random(112);
        foreach (int length in Enumerable.Range(0, 80).Concat([127, 128, 129, 255, 256, 257, 1000, 6001]))
        {
            foreach (int kind in new[] { 0, 1, 2, 3 })
            {
                byte[] row = [.. Enumerable.Range(0, length).Select(i => kind switch
                {
                    0 => (byte)random.Next(256),                         // literals
                    1 => (byte)(i / 7),                                  // runs of 7
                    2 => (byte)(i % 40 < 20 ? 5 : random.Next(3)),      // long runs and short ones
                    _ => (byte)(random.Next(4) == 0 ? random.Next(2) : 9),
                })];
                byte[] encoded = new byte[PackBitsEncoder.MaxEncoded(length)];
                int size = PackBitsEncoder.EncodeRow(row, encoded);
                byte[] reference = Reference(row);

                Assert.Equal(reference, encoded[..size]);
                byte[] decoded = new byte[length + 32];
                Array.Fill(decoded, (byte)0xEE);
                Assert.Equal(length, PackBits.Decode(encoded.AsSpan(0, size), decoded.AsSpan(0, length), out bool overflow));
                Assert.False(overflow);
                Assert.Equal(row, decoded[..length]);
                Assert.All(decoded[length..], b => Assert.Equal(0xEE, b));
            }
        }
    }

    /// <summary>The encoder as it was, byte by byte.</summary>
    private static byte[] Reference(byte[] row)
    {
        var output = new List<byte>();
        int i = 0, n = row.Length;
        while (i < n)
        {
            byte value = row[i];
            int run = 1;
            while (i + run < n && run < 128 && row[i + run] == value)
            {
                run++;
            }

            if (run >= 3)
            {
                output.Add((byte)(1 - run));
                output.Add(value);
                i += run;
                continue;
            }

            int j = i;
            while (j < n && j - i < 128 && !(j + 2 < n && row[j] == row[j + 1] && row[j] == row[j + 2]))
            {
                j++;
            }

            output.Add((byte)(j - i - 1));
            output.AddRange(row[i..j]);
            i = j;
        }

        return [.. output];
    }
}
