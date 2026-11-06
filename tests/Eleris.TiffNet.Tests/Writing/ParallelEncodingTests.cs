using Eleris.TiffNet.Internal.Writing;

namespace Eleris.TiffNet.Tests.Writing;

public class ParallelEncodingTests
{
    [Theory]
    [InlineData(TiffLayout.Strips, TiffCompression.Deflate)]
    [InlineData(TiffLayout.Tiles, TiffCompression.Lzw)]
    [InlineData(TiffLayout.Tiles, TiffCompression.PackBits)]
    public void The_file_does_not_depend_on_the_number_of_threads(TiffLayout layout, TiffCompression compression)
    {
        var file = new TiffFile().Add(DirectoryWriterTests.Sample<float>(300, 200, 3, 1)).Add(DirectoryWriterTests.Sample<byte>(500, 90, 1, 2));
        var options = TiffWriteOptions.Default with { Layout = layout, Compression = compression, TileSize = 32 };

        byte[] one = Bytes(file, options with { MaxDegreeOfParallelism = 1 });
        byte[] many = Bytes(file, options with { MaxDegreeOfParallelism = 8 });

        Assert.Equal(one, many);
    }

    private static byte[] Bytes(TiffFile file, TiffWriteOptions options)
    {
        using var stream = new MemoryStream();
        file.Save(stream, options);
        return stream.ToArray();
    }

    [Fact]
    public void At_most_two_blocks_per_thread_are_in_flight_and_they_are_written_in_order()
    {
        int active = 0, maxActive = 0, encoded = 0, appended = 0, maxAhead = 0;
        var order = new List<int>();

        BlockPipeline.Run<byte[]>(200, 4, block =>
        {
            int now = Interlocked.Increment(ref active);
            InterlockedMax(ref maxActive, now);
            int ahead = Interlocked.Increment(ref encoded) - Volatile.Read(ref appended);
            InterlockedMax(ref maxAhead, ahead);
            Thread.SpinWait(2000 + block % 7 * 3000);
            Interlocked.Decrement(ref active);
            return [(byte)block];
        }, (block, data) =>
        {
            order.Add(data[0]);
            Interlocked.Increment(ref appended);
        }, _ => { }, CancellationToken.None);

        Assert.Equal(Enumerable.Range(0, 200).Select(i => (int)(byte)i), order);
        Assert.InRange(maxActive, 1, 4);
        Assert.InRange(maxAhead, 1, 8);
    }

    [Fact]
    public void A_failing_block_stops_the_write_with_its_exception()
    {
        var failure = new InvalidDataException("block 37");

        var thrown = Assert.Throws<InvalidDataException>(() => BlockPipeline.Run(100, 4,
            block => block == 37 ? throw failure : new byte[] { 1 }, (_, _) => { }, _ => { }, CancellationToken.None));

        Assert.Same(failure, thrown);
    }

    private static void InterlockedMax(ref int target, int value)
    {
        int current;
        while (value > (current = Volatile.Read(ref target)) && Interlocked.CompareExchange(ref target, value, current) != current)
        {
        }
    }
}
