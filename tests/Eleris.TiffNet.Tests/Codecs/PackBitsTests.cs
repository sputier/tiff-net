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
}
