using System.IO.Compression;
using Eleris.TiffNet.Internal.Codecs;

namespace Eleris.TiffNet.Tests.Codecs;

public class DeflateTests
{
    private static byte[] Compress(byte[] data)
    {
        using var output = new MemoryStream();
        using (var zlib = new ZLibStream(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            zlib.Write(data);
        }

        return output.ToArray();
    }

    [Fact]
    public void A_zlib_stream_is_inflated_into_the_destination()
    {
        byte[] data = LzwTests.Sample("random");
        byte[] compressed = Compress(data);
        byte[] decoded = new byte[data.Length];

        Assert.Equal(data.Length, Deflate.Decode(compressed, compressed.Length, decoded, checkOverflow: true, out bool overflow));
        Assert.Equal(data, decoded);
        Assert.False(overflow);
    }

    [Fact]
    public void The_output_is_bounded_by_the_destination_and_the_overflow_seen_when_asked()
    {
        byte[] compressed = Compress(new byte[1 << 20]);
        byte[] decoded = new byte[100];

        Assert.Equal(100, Deflate.Decode(compressed, compressed.Length, decoded, checkOverflow: true, out bool overflow));
        Assert.True(overflow);
        Assert.Equal(100, Deflate.Decode(compressed, compressed.Length, decoded, checkOverflow: false, out overflow));
        Assert.False(overflow);
    }

    [Fact]
    public void A_damaged_stream_is_invalid_data()
    {
        byte[] compressed = Compress(LzwTests.Sample("random"));
        compressed[10] ^= 0xFF;
        compressed[11] ^= 0x55;

        Assert.Throws<InvalidDataException>(() => Deflate.Decode(compressed, compressed.Length, new byte[50_000], false, out _));
    }
}
