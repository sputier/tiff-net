using Eleris.TiffNet.Internal.Codecs;
using Eleris.TiffNet.Tests.Codecs;

namespace Eleris.TiffNet.Tests.Writing;

public class EncoderTests
{
    public static TheoryData<string> Samples() => ["empty", "one", "runs", "random", "text", "long"];

    [Theory]
    [MemberData(nameof(Samples))]
    public void PackBits_rows_decode_back(string name)
    {
        byte[] data = LzwTests.Sample(name);
        byte[] encoded = new byte[PackBitsEncoder.MaxEncoded(data.Length)];
        int length = PackBitsEncoder.EncodeRow(data, encoded);
        byte[] decoded = new byte[data.Length];

        Assert.InRange(length, 0, PackBitsEncoder.MaxEncoded(data.Length));
        Assert.Equal(data.Length, PackBits.Decode(encoded.AsSpan(0, length), decoded, out bool overflow));
        Assert.Equal(data, decoded);
        Assert.False(overflow);
    }

    [Theory]
    [MemberData(nameof(Samples))]
    public void LZW_decodes_back_and_matches_the_encoder_of_libtiff(string name)
    {
        byte[] data = LzwTests.Sample(name);
        byte[] encoded = new byte[LzwEncoder.MaxEncoded(data.Length)];
        int length = LzwEncoder.Encode(data, encoded);
        byte[] decoded = new byte[data.Length];

        Assert.Equal(data.Length, Lzw.Decode(encoded.AsSpan(0, length), decoded, out bool overflow));
        Assert.Equal(data, decoded);
        Assert.False(overflow);
        // The reference encoder of the tests writes libtiff's code stream: the same codes, the same widths.
        Assert.Equal(LzwTests.Encode(data), encoded[..length]);
    }

    [Fact]
    public void Incompressible_data_stays_within_the_bounds_of_the_encoders()
    {
        byte[] noise = new byte[1 << 20];
        new Random(40).NextBytes(noise);

        Assert.InRange(LzwEncoder.Encode(noise, new byte[LzwEncoder.MaxEncoded(noise.Length)]), 1, LzwEncoder.MaxEncoded(noise.Length));
        Assert.InRange(PackBitsEncoder.EncodeRow(noise, new byte[PackBitsEncoder.MaxEncoded(noise.Length)]), 1, PackBitsEncoder.MaxEncoded(noise.Length));
    }

    [Theory]
    [InlineData(TiffCompressionLevel.Fastest)]
    [InlineData(TiffCompressionLevel.Optimal)]
    [InlineData(TiffCompressionLevel.SmallestSize)]
    public void Deflate_decodes_back_at_every_level(TiffCompressionLevel level)
    {
        byte[] data = LzwTests.Sample("long");
        byte[] encoded = DeflateEncoder.Encode(data, level);
        byte[] decoded = new byte[data.Length];

        Assert.Equal(data.Length, Deflate.Decode(encoded, 0, encoded.Length, decoded, true, out bool overflow));
        Assert.Equal(data, decoded);
        Assert.False(overflow);
    }

    [Fact]
    public void Smaller_sizes_come_from_higher_levels()
    {
        byte[] data = LzwTests.Sample("text");

        Assert.True(DeflateEncoder.Encode(data, TiffCompressionLevel.SmallestSize).Length
            <= DeflateEncoder.Encode(data, TiffCompressionLevel.Fastest).Length);
    }

    [Theory]
    [InlineData(TiffCompressionLevel.Fastest, TiffPredictor.FloatingPoint, true)]
    [InlineData(TiffCompressionLevel.Fastest, TiffPredictor.Horizontal, false)]
    [InlineData(TiffCompressionLevel.Optimal, TiffPredictor.FloatingPoint, false)]
    public void The_run_length_strategy_compresses_floating_point_data_at_the_level_Fastest(TiffCompressionLevel level, TiffPredictor predictor, bool runLength)
    {
        // The stream is the one zlib writes with that strategy (deterministic), plain Deflate, decoded back.
        byte[] data = LzwTests.Sample("long");
        byte[] encoded = DeflateEncoder.Encode(data, level, predictor);
        using var expected = new MemoryStream();
        var options = new System.IO.Compression.ZLibCompressionOptions
        {
            CompressionLevel = level == TiffCompressionLevel.Fastest ? 1 : 6,
            CompressionStrategy = runLength ? System.IO.Compression.ZLibCompressionStrategy.RunLengthEncoding : System.IO.Compression.ZLibCompressionStrategy.Default,
        };
        using (var zlib = new System.IO.Compression.ZLibStream(expected, options, leaveOpen: true))
        {
            zlib.Write(data);
        }

        Assert.Equal(expected.ToArray(), encoded);
        byte[] decoded = new byte[data.Length];
        Assert.Equal(data.Length, Deflate.Decode(encoded, 0, encoded.Length, decoded, true, out _));
        Assert.Equal(data, decoded);
    }
}
