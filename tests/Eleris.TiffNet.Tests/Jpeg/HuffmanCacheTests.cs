using Eleris.TiffNet.Internal.Jpeg;

namespace Eleris.TiffNet.Tests.Jpeg;

/// <summary>The Huffman tables of a read, built once: the same bytes give the same table, damaged ones fail as when defined.</summary>
public class HuffmanCacheTests
{
    private static byte[] Definition(byte classAndId, byte firstCount)
    {
        // Counts of codes of each length, then the symbols: firstCount codes of 1 bit... a few codes of 3 bits.
        byte[] counts = [firstCount, 0, 3, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];
        int total = counts.Sum(c => c);
        return [classAndId, .. counts, .. Enumerable.Range(1, total).Select(i => (byte)i)];
    }

    [Fact]
    public void The_same_bytes_give_the_same_built_table()
    {
        var cache = new HuffmanCache();
        var first = cache.Find(Definition(0x10, 1));

        Assert.NotNull(first);
        Assert.Same(first, cache.Find(Definition(0x10, 1)));
        Assert.NotSame(first, cache.Find(Definition(0x11, 1)));   // another slot: another definition
        Assert.Equal(1 << 8 | 1, first!.Lookup[0]);                // built: the first code, 1 bit, symbol 1
    }

    [Fact]
    public void A_table_that_is_not_valid_fails_as_when_it_is_defined()
    {
        var cache = new HuffmanCache();

        Assert.Throws<InvalidDataException>(() => cache.Find(Definition(0x10, 2)));   // two codes of one bit and more: over-subscribed
        Assert.Throws<InvalidDataException>(() => cache.Find(Definition(0x10, 2)));   // still, never kept
    }

    [Fact]
    public void Beyond_its_capacity_tables_are_defined_as_before()
    {
        var cache = new HuffmanCache();
        for (int i = 0; i < HuffmanCache.Capacity; i++)
        {
            byte[] definition = Definition(0x10, 1);
            definition[^1] = (byte)(100 + i);   // another last symbol: another table
            Assert.NotNull(cache.Find(definition));
        }

        Assert.Null(cache.Find(Definition(0x01, 1)));
    }

    [Fact]
    public void Data_that_ran_out_stays_insufficient_without_a_restart_marker()
    {
        // The bits ran out; a restart finds no RSTn marker: the scan stays exhausted (its MCUs grey). With the marker, it resumes.
        byte[] data = [0xAB, 0xFF, 0xD9];
        var reader = new JpegBitReader(data, 0);
        _ = reader.Read(16);
        Assert.True(reader.Insufficient);
        reader.Restart();
        Assert.True(reader.Insufficient);

        byte[] resumed = [0xAB, 0xFF, 0xD0, 0x7E];
        reader = new JpegBitReader(resumed, 0);
        _ = reader.Read(16);
        Assert.True(reader.Insufficient);
        reader.Restart();
        Assert.False(reader.Insufficient);
        Assert.Equal(0x7E, reader.Read(8));
        Assert.False(reader.Insufficient);
    }
}
