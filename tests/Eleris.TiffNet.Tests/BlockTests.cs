using Eleris.TiffNet.Tests.Fixtures;

namespace Eleris.TiffNet.Tests;

/// <summary>The stored strips and tiles of an image, read without decoding them.</summary>
[Collection(nameof(Measurement))]
public sealed class BlockTests : IDisposable
{
    private const int Size = 32;
    private const int Compression = 1;
    private const int Unknown = 32000;

    private readonly List<string> _paths = [];

    public void Dispose()
    {
        foreach (string path in _paths)
        {
            File.Delete(path);
        }
    }

    /// <summary>A file of one image of 32 x 32 pixels, its blocks of the given sizes filled with distinct bytes.</summary>
    private sealed record Fixture(byte[] File, long[] Offsets, long[] Counts);

    /// <summary>Strips of 8 rows or tiles of 16 x 16, one plane or three; <paramref name="compression"/> 1 stores each block at its
    /// natural size, any other value gives blocks of different sizes.</summary>
    private static Fixture Make(bool bigTiff, bool littleEndian, bool tiles, bool planar, int compression = Compression)
    {
        int samples = planar ? 3 : 1;
        const int perPlane = 4;
        int blocks = perPlane * samples;
        long[] counts = new long[blocks];
        for (int i = 0; i < blocks; i++)
        {
            counts[i] = compression == Compression ? (tiles ? 16 * 16 : Size * 8) : 37 + i * 11;
        }

        var builder = new TiffBuilder(bigTiff, littleEndian);
        var data = new List<byte>();
        long[] offsets = new long[blocks];
        for (int i = 0; i < blocks; i++)
        {
            offsets[i] = builder.DataOffset + data.Count;
            data.AddRange(Enumerable.Range(0, (int)counts[i]).Select(j => (byte)(i * 41 + j * 7 + 1)));
        }

        builder.Data = [.. data];
        var d = builder.AddDirectory();
        d.Add(256, TiffTagType.Long, Size).Add(257, TiffTagType.Long, Size).Add(258, TiffTagType.Short, Enumerable.Repeat(8L, samples).ToArray())
            .Add(259, TiffTagType.Short, compression).Add(262, TiffTagType.Short, planar ? 2 : 1).Add(277, TiffTagType.Short, samples);
        if (planar)
        {
            d.Add(284, TiffTagType.Short, 2);
        }

        if (tiles)
        {
            d.Add(322, TiffTagType.Long, 16).Add(323, TiffTagType.Long, 16).Add(324, TiffTagType.Long, offsets).Add(325, TiffTagType.Long, counts);
        }
        else
        {
            d.Add(278, TiffTagType.Long, 8).Add(273, TiffTagType.Long, offsets).Add(279, TiffTagType.Long, counts);
        }

        return new Fixture(builder.Build(), offsets, counts);
    }

    public static TheoryData<bool, bool, bool, bool> Layouts()
    {
        var data = new TheoryData<bool, bool, bool, bool>();
        foreach (bool big in new[] { false, true })
        {
            foreach (bool little in new[] { false, true })
            {
                foreach (bool tiles in new[] { false, true })
                {
                    foreach (bool planar in new[] { false, true })
                    {
                        data.Add(big, little, tiles, planar);
                    }
                }
            }
        }

        return data;
    }

    private string Temporary(byte[] bytes)
    {
        string path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.tif");
        File.WriteAllBytes(path, bytes);
        _paths.Add(path);
        return path;
    }

    public static TheoryData<string> Sources() => new() { "memory", "file", "seekable stream", "forward-only stream" };

    private TiffFile Open(byte[] bytes, string source) => source switch
    {
        "memory" => TiffFile.Read(bytes),
        "file" => TiffFile.Open(Temporary(bytes)),
        "seekable stream" => TiffFile.Open(new MemoryStream(bytes)),
        _ => TiffFile.Read(new ForwardOnlyStream(new MemoryStream(bytes))),
    };

    [Theory]
    [MemberData(nameof(Layouts))]
    public void Blocks_are_the_offsets_and_counts_of_the_file_in_order(bool big, bool little, bool tiles, bool planar)
    {
        var fixture = Make(big, little, tiles, planar);
        using var file = TiffFile.Read(fixture.File);

        var blocks = file.Primary.Blocks;

        Assert.Equal(fixture.Offsets.Length, blocks.Count);
        Assert.Equal(fixture.Offsets.Zip(fixture.Counts, (o, c) => new TiffBlock(o, c)), blocks);
        Assert.Equal(tiles ? TiffLayout.Tiles : TiffLayout.Strips, file.Primary.Storage!.Layout);
    }

    [Theory]
    [MemberData(nameof(Layouts))]
    public void Blocks_equal_what_the_decoder_reads(bool big, bool little, bool tiles, bool planar)
    {
        var fixture = Make(big, little, tiles, planar);
        using var file = TiffFile.Read(fixture.File);
        var image = file.Primary;

        // Uncompressed samples: the blocks, put back where they belong, are the pixels.
        byte[] expected = new byte[Size * Size * (planar ? 3 : 1)];
        int at = 0;
        foreach (var block in image.Blocks)
        {
            if (!tiles)
            {
                fixture.File.AsSpan((int)block.Offset, (int)block.ByteCount).CopyTo(expected.AsSpan(at));
                at += (int)block.ByteCount;
            }
        }

        if (!tiles)
        {
            Assert.Equal(expected, image.ReadRawPixels<byte>(TiffPixelOrder.Planar));
            return;
        }

        byte[] pixels = image.ReadRawPixels<byte>(TiffPixelOrder.Planar);
        for (int b = 0; b < image.Blocks.Count; b++)
        {
            int plane = b / 4, tx = b % 4 % 2, ty = b % 4 / 2;
            var stored = fixture.File.AsSpan((int)image.Blocks[b].Offset, 256);
            for (int row = 0; row < 16; row++)
            {
                Assert.True(stored.Slice(row * 16, 16).SequenceEqual(pixels.AsSpan(plane * Size * Size + (ty * 16 + row) * Size + tx * 16, 16)), $"tile {b} row {row}");
            }
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(false, true)]
    public void An_unsupported_image_lists_its_blocks_without_throwing(bool tiles, bool planar)
    {
        var fixture = Make(bigTiff: false, littleEndian: true, tiles, planar, Unknown);
        using var file = TiffFile.Read(fixture.File);
        var image = file.Primary;

        Assert.NotNull(image.UnsupportedReason);
        Assert.Equal(fixture.Offsets.Zip(fixture.Counts, (o, c) => new TiffBlock(o, c)), image.Blocks);
        for (int i = 0; i < image.Blocks.Count; i++)
        {
            byte[] bytes = new byte[fixture.Counts[i]];
            Assert.Equal(bytes.Length, image.ReadBlock(i, bytes));
            Assert.True(fixture.File.AsSpan((int)fixture.Offsets[i], bytes.Length).SequenceEqual(bytes));
        }
    }

    [Theory]
    [MemberData(nameof(Sources))]
    public void ReadBlock_returns_the_stored_bytes_from_every_source(string source)
    {
        var fixture = Make(bigTiff: true, littleEndian: false, tiles: true, planar: true, Unknown);
        using var file = Open(fixture.File, source);
        var image = file.Primary;

        // A buffer larger than the block: only its first bytes are written.
        byte[] buffer = new byte[200];
        for (int i = 0; i < image.Blocks.Count; i++)
        {
            Array.Fill(buffer, (byte)0xEE);
            int count = image.ReadBlock(i, buffer);

            Assert.Equal(fixture.Counts[i], count);
            Assert.True(fixture.File.AsSpan((int)fixture.Offsets[i], count).SequenceEqual(buffer.AsSpan(0, count)));
            Assert.All(buffer.Skip(count), b => Assert.Equal(0xEE, b));
        }
    }

    [Theory]
    [MemberData(nameof(Sources))]
    public void Blocks_are_read_from_several_threads_at_once(string source)
    {
        var fixture = Make(bigTiff: false, littleEndian: true, tiles: false, planar: true, Unknown);
        using var file = Open(fixture.File, source);
        var image = file.Primary;
        int failures = 0;

        Parallel.For(0, 2000, new ParallelOptions { MaxDegreeOfParallelism = 8 }, i =>
        {
            int index = i % image.Blocks.Count;
            byte[] bytes = new byte[image.Blocks[index].ByteCount];
            if (image.ReadBlock(index, bytes) != bytes.Length || !fixture.File.AsSpan((int)fixture.Offsets[index], bytes.Length).SequenceEqual(bytes))
            {
                Interlocked.Increment(ref failures);
            }
        });

        Assert.Equal(0, failures);
    }

    [Theory]
    [InlineData("memory")]
    [InlineData("file")]
    public void Reading_a_block_allocates_nothing_once_warm(string source)
    {
        var fixture = Make(bigTiff: false, littleEndian: true, tiles: false, planar: false, Unknown);
        using var file = Open(fixture.File, source);
        var image = file.Primary;
        byte[] buffer = new byte[256];
        for (int i = 0; i < 3; i++)
        {
            image.ReadBlock(i % image.Blocks.Count, buffer);
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        int total = 0;
        for (int i = 0; i < 1000; i++)
        {
            total += image.ReadBlock(i % image.Blocks.Count, buffer);
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(total > 0);
        Assert.Equal(0, allocated);
    }

    [Fact]
    public void A_destination_that_is_too_small_is_refused_and_left_alone()
    {
        var fixture = Make(bigTiff: false, littleEndian: true, tiles: false, planar: false, Unknown);
        using var file = TiffFile.Read(fixture.File);
        byte[] small = new byte[fixture.Counts[2] - 1];

        var ex = Assert.Throws<ArgumentException>(() => file.Primary.ReadBlock(2, small));

        Assert.Equal("destination", ex.ParamName);
        Assert.All(small, b => Assert.Equal(0, b));
    }

    [Fact]
    public void An_index_outside_the_blocks_is_out_of_range()
    {
        var fixture = Make(bigTiff: false, littleEndian: true, tiles: true, planar: false);
        using var file = TiffFile.Read(fixture.File);
        var image = file.Primary;
        byte[] buffer = new byte[1000];

        Assert.Throws<ArgumentOutOfRangeException>(() => image.ReadBlock(-1, buffer));
        Assert.Throws<ArgumentOutOfRangeException>(() => image.ReadBlock(image.Blocks.Count, buffer));
        Assert.Throws<ArgumentOutOfRangeException>(() => image.TryGetBlockMemory(image.Blocks.Count));
        Assert.Throws<ArgumentOutOfRangeException>(() => image.Blocks[image.Blocks.Count]);
    }

    [Fact]
    public void A_file_that_is_disposed_can_no_longer_be_read_but_still_lists_its_blocks()
    {
        var fixture = Make(bigTiff: false, littleEndian: true, tiles: false, planar: false);
        var file = TiffFile.Open(Temporary(fixture.File));
        var image = file.Primary;
        file.Dispose();

        Assert.Equal(4, image.Blocks.Count);
        Assert.Throws<ObjectDisposedException>(() => image.ReadBlock(0, new byte[1000]));
    }

    [Fact]
    public void An_image_created_in_memory_has_no_blocks_and_cannot_read_any()
    {
        var image = TiffImage.Create<byte>(new byte[16], 4, 4);

        Assert.Empty(image.Blocks);
        Assert.Throws<InvalidOperationException>(() => image.ReadBlock(0, new byte[16]));
        Assert.Throws<InvalidOperationException>(() => image.TryGetBlockMemory(0));
    }

    [Fact]
    public void An_image_whose_pixels_are_replaced_no_longer_has_stored_blocks()
    {
        var fixture = Make(bigTiff: false, littleEndian: true, tiles: false, planar: false);
        using var file = TiffFile.Read(fixture.File);
        var image = file.Primary;
        Assert.NotEmpty(image.Blocks);

        image.SetPixels<byte>(new byte[Size * Size], Size, Size);

        Assert.Empty(image.Blocks);
        Assert.Throws<InvalidOperationException>(() => image.ReadBlock(0, new byte[1000]));
    }

    [Theory]
    [InlineData("memory", true)]
    [InlineData("seekable stream", false)]
    [InlineData("file", false)]
    [InlineData("forward-only stream", false)]
    public void A_block_in_memory_is_a_view_without_a_copy(string source, bool inMemory)
    {
        var fixture = Make(bigTiff: false, littleEndian: true, tiles: false, planar: false, Unknown);
        using var file = Open(fixture.File, source);

        var view = file.Primary.TryGetBlockMemory(1);

        if (!inMemory)
        {
            Assert.Null(view);
            return;
        }

        Assert.True(fixture.File.AsSpan((int)fixture.Offsets[1], (int)fixture.Counts[1]).SequenceEqual(view!.Value.Span));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void The_last_block_of_a_truncated_file_is_clipped_unless_the_options_are_strict(bool bigTiff)
    {
        var fixture = Make(bigTiff, littleEndian: true, tiles: false, planar: false, Unknown);
        // The last strip claims more bytes than the file holds.
        var builder = new TiffBuilder(bigTiff);
        builder.Data = fixture.File[(int)builder.DataOffset..(int)(fixture.Offsets[3] + fixture.Counts[3])];
        var d = builder.AddDirectory();
        long[] counts = [.. fixture.Counts[..3], fixture.Counts[3] + 100_000];
        d.Add(256, TiffTagType.Long, Size).Add(257, TiffTagType.Long, Size).Add(258, TiffTagType.Short, 8).Add(259, TiffTagType.Short, Unknown)
            .Add(262, TiffTagType.Short, 1).Add(277, TiffTagType.Short, 1).Add(278, TiffTagType.Long, 8)
            .Add(273, TiffTagType.Long, fixture.Offsets).Add(279, TiffTagType.Long, counts);
        byte[] bytes = builder.Build();

        using (var file = TiffFile.Read(bytes))
        {
            var last = file.Primary.Blocks[3];
            Assert.Equal(bytes.Length - last.Offset, last.ByteCount);
            byte[] stored = new byte[last.ByteCount];
            Assert.Equal(stored.Length, file.Primary.ReadBlock(3, stored));
            Assert.True(bytes.AsSpan((int)last.Offset).SequenceEqual(stored));
            Assert.Equal(fixture.Counts[..3], file.Primary.Blocks.Take(3).Select(b => b.ByteCount));
        }

        Assert.Throws<TiffException>(() => TiffFile.Read(bytes, TiffReadOptions.Default with { Strict = true }));
    }

    [Fact]
    public void A_block_that_starts_outside_the_file_is_refused_when_the_file_is_opened()
    {
        var builder = new TiffBuilder();
        builder.Data = new byte[64];
        builder.AddDirectory().Image(8, 8).Remove(273).Add(273, TiffTagType.Long, 1_000_000);

        Assert.Throws<TiffException>(() => TiffFile.Read(builder.Build()));
    }

    [Theory]
    [MemberData(nameof(Sources))]
    public async Task ReadBlockAsync_returns_the_stored_bytes(string source)
    {
        var fixture = Make(bigTiff: false, littleEndian: false, tiles: true, planar: false, Unknown);
        using var file = Open(fixture.File, source);
        var image = file.Primary;

        for (int i = 0; i < image.Blocks.Count; i++)
        {
            byte[] buffer = new byte[300];
            int count = await image.ReadBlockAsync(i, buffer);

            Assert.Equal(fixture.Counts[i], count);
            Assert.True(fixture.File.AsSpan((int)fixture.Offsets[i], count).SequenceEqual(buffer.AsSpan(0, count)));
        }
    }

    [Theory]
    [MemberData(nameof(Sources))]
    public async Task ReadBlockAsync_can_be_cancelled(string source)
    {
        var fixture = Make(bigTiff: false, littleEndian: true, tiles: false, planar: false, Unknown);
        using var file = Open(fixture.File, source);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        byte[] buffer = new byte[300];

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await file.Primary.ReadBlockAsync(0, buffer, cancelled.Token));

        Assert.All(buffer, b => Assert.Equal(0, b));
    }

    [Fact]
    public async Task ReadBlockAsync_reports_its_failures_through_the_task()
    {
        var fixture = Make(bigTiff: false, littleEndian: true, tiles: false, planar: false, Unknown);
        using var file = TiffFile.Read(fixture.File);

        var badIndex = file.Primary.ReadBlockAsync(99, new byte[300]);
        var tooSmall = file.Primary.ReadBlockAsync(0, new byte[1]);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () => await badIndex);
        await Assert.ThrowsAsync<ArgumentException>(async () => await tooSmall);
    }

    [Fact]
    public void Each_page_lists_the_blocks_of_its_own_image()
    {
        var builder = new TiffBuilder();
        builder.Data = new byte[200];
        builder.AddDirectory().Image(8, 8).Remove(273).Remove(279).Add(273, TiffTagType.Long, builder.DataOffset + 10).Add(279, TiffTagType.Long, 64);
        builder.AddDirectory().Image(8, 8).Remove(273).Remove(279).Add(273, TiffTagType.Long, builder.DataOffset + 100).Add(279, TiffTagType.Long, 64);
        using var file = TiffFile.Read(builder.Build());

        Assert.Equal([new TiffBlock(builder.DataOffset + 10, 64)], file[0].Blocks);
        Assert.Equal([new TiffBlock(builder.DataOffset + 100, 64)], file[1].Blocks);
    }

    private sealed class ForwardOnlyStream(Stream inner) : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
