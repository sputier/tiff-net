using Eleris.TiffNet.Internal.Codecs;

namespace Eleris.TiffNet.Tests.Codecs;

/// <summary>
/// The edges the mutation testing of the codecs found untested (#51): the bounds of what an encoder can write, the encoder past
/// a full LZW table, the overflow reported by the decoders, and loops through references that must not touch a byte beyond
/// their row.
/// </summary>
public class CodecBoundaryTests
{
    public static TheoryData<int> Sizes() => [1, 2, 127, 128, 129, 1000, 4096, 65_536, 1 << 20];

    [Theory]
    [MemberData(nameof(Sizes))]
    public void No_encoder_writes_more_than_its_bound_even_for_incompressible_data(int size)
    {
        byte[] noise = new byte[size];
        new Random(size).NextBytes(noise);

        // Exactly the bound, not a pooled buffer rounded up: an encoder going beyond it fails here.
        byte[] packBits = new byte[PackBitsEncoder.MaxEncoded(size)];
        Assert.InRange(PackBitsEncoder.EncodeRow(noise, packBits), 1, packBits.Length);

        byte[] lzw = new byte[LzwEncoder.MaxEncoded(size)];
        Assert.InRange(LzwEncoder.Encode(noise, lzw), 1, lzw.Length);

        foreach (var level in new[] { TiffCompressionLevel.Fastest, TiffCompressionLevel.Optimal, TiffCompressionLevel.SmallestSize })
        {
            byte[] deflate = new byte[DeflateEncoder.MaxEncoded(size)];
            Assert.InRange(DeflateEncoder.Encode(noise, deflate, level), 1, deflate.Length);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void The_LZW_encoder_restarts_its_table_when_it_is_full(int seed)
    {
        // Noise fills the 4 093 codes of the table many times over: each restart must be decodable.
        byte[] data = new byte[1 << 20];
        if (seed == 0)
        {
            new Random(51).NextBytes(data);
        }
        else
        {
            for (int i = 0; i < data.Length; i++)
            {
                data[i] = (byte)(i * 7 % 251 ^ i >> 9);
            }
        }

        byte[] encoded = new byte[LzwEncoder.MaxEncoded(data.Length)];
        int length = LzwEncoder.Encode(data, encoded);
        byte[] decoded = new byte[data.Length];

        Assert.Equal(data.Length, Lzw.Decode(encoded.AsSpan(0, length), decoded, out bool overflow));
        Assert.False(overflow);
        Assert.Equal(data, decoded);
    }

    [Fact]
    public void The_LZW_encoder_ends_right_whatever_the_state_of_its_table()
    {
        // Each byte more adds at most one code: the lengths 0 to 9 000 of noise end the stream with the table at every size,
        // including the last string filling it (a clear code before the end code) or widening the codes, as libtiff does.
        byte[] noise = new byte[9_000];
        new Random(7).NextBytes(noise);
        byte[] encoded = new byte[LzwEncoder.MaxEncoded(noise.Length)];
        byte[] decoded = new byte[noise.Length];
        for (int length = 0; length <= noise.Length; length++)
        {
            int size = LzwEncoder.Encode(noise.AsSpan(0, length), encoded);
            int written = Lzw.Decode(encoded.AsSpan(0, size), decoded.AsSpan(0, length), out bool overflow);
            if (written != length || overflow || !decoded.AsSpan(0, length).SequenceEqual(noise.AsSpan(0, length)))
            {
                Assert.Fail($"{length} bytes do not decode back");
            }
        }
    }

    [Fact]
    public void The_LZW_decoder_reports_a_string_cut_by_the_end_of_the_destination()
    {
        // Runs encode into long strings: a destination one byte short cuts the last one.
        byte[] data = [.. Enumerable.Repeat((byte)7, 5000)];
        byte[] encoded = new byte[LzwEncoder.MaxEncoded(data.Length)];
        int length = LzwEncoder.Encode(data, encoded);

        byte[] shorter = new byte[data.Length - 1];
        int written = Lzw.Decode(encoded.AsSpan(0, length), shorter, out bool overflow);

        Assert.Equal(shorter.Length, written);
        Assert.True(overflow);
        Assert.All(shorter, b => Assert.Equal(7, b));
    }

    [Fact]
    public void The_PackBits_decoder_reports_runs_cut_by_the_end_of_the_destination()
    {
        // A literal run of 4 bytes, then a repeated run of 10: destinations that cut each.
        byte[] source = [3, 1, 2, 3, 4, unchecked((byte)-9), 5];

        Assert.Equal(2, PackBits.Decode(source, new byte[2], out bool literalCut));
        Assert.True(literalCut);
        Assert.Equal(8, PackBits.Decode(source, new byte[8], out bool runCut));
        Assert.True(runCut);
        byte[] whole = new byte[14];
        Assert.Equal(14, PackBits.Decode(source, whole, out bool overflow));
        Assert.False(overflow);
        Assert.Equal(new byte[] { 1, 2, 3, 4, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5 }, whole);

        // A literal run cut by the end of the data is what there is, not an overflow.
        Assert.Equal(2, PackBits.Decode([3, 1, 2], new byte[4], out bool truncated));
        Assert.False(truncated);
    }

    [Theory]
    [InlineData(2, 1)]
    [InlineData(2, 3)]
    [InlineData(4, 1)]
    [InlineData(4, 3)]
    [InlineData(8, 1)]
    public void Undoing_the_floating_point_predictor_stays_within_its_row(int bytesPerSample, int stride)
    {
        int length = bytesPerSample * stride * 37;
        byte[] buffer = new byte[length + 64];
        new Random(length).NextBytes(buffer);
        byte[] after = buffer[length..];
        byte[] scratch = new byte[length + 64];
        byte[] scratchAfter = new byte[64];
        Array.Fill<byte>(scratch, 0xA5, length, 64);
        Array.Fill<byte>(scratchAfter, 0xA5);

        Predictors.UndoFloatingPoint(buffer.AsSpan(0, length), scratch.AsSpan(0, length), bytesPerSample, stride, littleEndian: true);

        Assert.Equal(after, buffer[length..]);
        Assert.Equal(scratchAfter, scratch[length..]);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(1, 3)]
    [InlineData(2, 1)]
    [InlineData(2, 4)]
    [InlineData(4, 3)]
    [InlineData(8, 2)]
    public void Undoing_the_horizontal_predictor_stays_within_its_row(int bytesPerSample, int stride)
    {
        int length = bytesPerSample * stride * 41;
        byte[] buffer = new byte[length + 64];
        new Random(length + stride).NextBytes(buffer);
        byte[] after = buffer[length..];

        Predictors.UndoHorizontal(buffer.AsSpan(0, length), bytesPerSample, stride, littleEndian: true);
        Assert.Equal(after, buffer[length..]);

        Predictors.UndoHorizontal(buffer.AsSpan(0, length), bytesPerSample, stride, littleEndian: false);
        Assert.Equal(after, buffer[length..]);
    }
}
