using Eleris.TiffNet.Internal.Jpeg;

namespace Eleris.TiffNet.Tests.Jpeg;

public class HuffmanTableTests
{
    [Fact]
    public void Valid_tables_decode_their_codes()
    {
        // Lengths 2, 2, 3, 3: codes 00, 01, 100, 101.
        var table = HuffmanTable.Create([0, 2, 2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0], [7, 8, 9, 10], dc: true);

        Assert.Equal((7, 2), (table.Decode(0b00 << 14, out int l0), l0));
        Assert.Equal((8, 2), (table.Decode(0b01 << 14, out int l1), l1));
        Assert.Equal((10, 3), (table.Decode(0b101 << 13, out int l2), l2));
        Assert.Equal((0, 16), (table.Decode(0b111 << 13, out int l3), l3));   // not a code: 0, as libjpeg does
    }

    [Fact]
    public void Long_codes_take_the_slow_path()
    {
        // One code of each length from 1 to 15: the code of length n is n - 1 ones then a zero.
        byte[] symbols = [.. Enumerable.Range(1, 15).Select(i => (byte)i)];
        var table = HuffmanTable.Create([.. Enumerable.Repeat((byte)1, 15), 0], symbols, dc: false);

        for (int length = 1; length <= 15; length++)
        {
            int code = ((1 << length) - 2) << (16 - length);
            Assert.Equal((length, length), (table.Decode(code, out int l), l));
        }
    }

    [Fact]
    public void Invalid_tables_are_refused()
    {
        byte[] many = new byte[16];
        many[15] = 255;
        many[14] = 2;
        Assert.Throws<InvalidDataException>(() => HuffmanTable.Create(many, new byte[257], dc: false));   // 257 symbols
        Assert.Throws<InvalidDataException>(() => HuffmanTable.Create([3, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0], [1, 2, 3], dc: false));   // over-subscribed
        Assert.Throws<InvalidDataException>(() => HuffmanTable.Create([2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0], [1, 2], dc: false));   // a full length
        Assert.Throws<InvalidDataException>(() => HuffmanTable.Create([0, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0], [16], dc: true));   // DC size 16
        Assert.Throws<InvalidDataException>(() => HuffmanTable.Create([0, 4, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0], [1, 2], dc: false));   // cut
    }

    [Fact]
    public void The_standard_tables_are_valid() =>
        Assert.All([(true, true), (true, false), (false, true), (false, false)], t => HuffmanTable.Standard(t.Item1, t.Item2));
}
