using System.Diagnostics;
using Eleris.TiffNet.Internal;
using Eleris.TiffNet.Internal.Structure;
using Eleris.TiffNet.Tests.Fixtures;

namespace Eleris.TiffNet.Tests.Directories;

public class StructureReaderTests
{
    private static StructureReader Reader(byte[] file, TiffReadOptions? options = null) =>
        new(TiffSource.FromMemory(file, "test.tif"), new ResourceBudget(options ?? TiffReadOptions.Default, "test.tif"));

    private static readonly TiffReadOptions Strict = TiffReadOptions.Default with { Strict = true };

    public static TheoryData<bool, bool> Layouts() => new() { { false, true }, { false, false }, { true, true }, { true, false } };

    [Theory]
    [InlineData(new byte[] { })]
    [InlineData(new byte[] { (byte)'I', (byte)'I', 42, 0 })]
    [InlineData(new byte[] { (byte)'X', (byte)'X', 42, 0, 8, 0, 0, 0 })]
    [InlineData(new byte[] { (byte)'I', (byte)'I', 44, 0, 8, 0, 0, 0 })]
    [InlineData(new byte[] { (byte)'I', (byte)'I', 43, 0, 4, 0, 0, 0, 16, 0, 0, 0, 0, 0, 0, 0 })]
    [InlineData(new byte[] { (byte)'I', (byte)'I', 43, 0, 8, 0, 0, 0 })]
    public void Data_that_is_not_TIFF_is_refused(byte[] data)
    {
        Assert.Throws<TiffException>(() => Reader(data));
    }

    [Theory]
    [MemberData(nameof(Layouts))]
    public void Entries_and_values_are_read_in_every_layout(bool bigTiff, bool little)
    {
        var builder = new TiffBuilder(bigTiff, little);
        builder.AddDirectory()
            .Add(256, TiffTagType.Short, 640)
            .Add(257, TiffTagType.Long, 480)
            .Add(258, TiffTagType.Short, 16, 16, 16)                  // inline in BigTIFF, out of line in classic
            .Add(273, TiffTagType.Long, 8, 1000, 2000, 3000, 4000)     // out of line
            .Add(65000, TiffTagType.SShort, -5)
            .Text(305, "a writer");

        var reader = Reader(builder.Build());
        var directory = reader.ReadDirectory(reader.Header.FirstDirectory)!;

        Assert.Equal(bigTiff, reader.Header.BigTiff);
        Assert.Equal(little, reader.Header.LittleEndian);
        Assert.Equal(6, directory.Entries.Count);
        Assert.Equal(640, reader.ReadInteger(directory, 256, 0));
        Assert.Equal(480, reader.ReadInteger(directory, 257, 0));
        Assert.Equal([16, 16, 16], reader.ReadIntegers(directory.Entries[258]));
        Assert.Equal([8, 1000, 2000, 3000, 4000], reader.ReadIntegers(directory.Entries[273]));
        Assert.Equal([-5], reader.ReadIntegers(directory.Entries[65000]));
        Assert.Equal("a writer\0"u8.ToArray(), reader.ReadBytes(directory.Entries[305]));
        Assert.Equal(7, reader.ReadInteger(directory, 999, 7));
        Assert.Equal(0, directory.Next);
    }

    [Theory]
    [MemberData(nameof(Layouts))]
    public void Values_beyond_the_file_are_marked_and_refused_when_read(bool bigTiff, bool little)
    {
        var builder = new TiffBuilder(bigTiff, little);
        builder.AddDirectory()
            .Add(256, TiffTagType.Short, 1)
            .Pointing(273, TiffTagType.Long, 100, 1_000_000)          // values far beyond the end
            .Pointing(279, TiffTagType.Long, uint.MaxValue, 8)        // 16 GB of values in a file of a few bytes
            .Pointing(330, TiffTagType.Long8, ulong.MaxValue / 2, 8);

        var reader = Reader(builder.Build());
        var directory = reader.ReadDirectory(reader.Header.FirstDirectory)!;

        Assert.True(directory.Entries[256].Valid);
        Assert.False(directory.Entries[273].Valid);
        Assert.False(directory.Entries[279].Valid);
        Assert.False(directory.Entries[330].Valid);
        long before = GC.GetAllocatedBytesForCurrentThread();
        Assert.Throws<TiffException>(() => reader.ReadIntegers(directory.Entries[279]));
        Assert.True(GC.GetAllocatedBytesForCurrentThread() - before < 100_000);
    }

    [Fact]
    public void Unknown_types_and_empty_entries_are_skipped_or_refused_with_Strict()
    {
        var builder = new TiffBuilder();
        builder.AddDirectory()
            .Add(256, TiffTagType.Short, 1)
            .Raw(300, 99, 1, [1, 0, 0, 0])
            .Raw(301, (ushort)TiffTagType.Short, 0, []);
        byte[] file = builder.Build();

        var directory = Reader(file).ReadDirectory(Reader(file).Header.FirstDirectory)!;

        Assert.Equal([256], directory.Entries.Keys);
        Assert.Throws<TiffException>(() => Reader(file, Strict).ReadDirectory(8));
    }

    [Fact]
    public void Duplicates_keep_the_first_and_disorder_is_tolerated_unless_Strict()
    {
        var builder = new TiffBuilder();
        builder.AddDirectory().Add(257, TiffTagType.Short, 2).Add(256, TiffTagType.Short, 1).Add(256, TiffTagType.Short, 99);
        byte[] file = builder.Build();
        var reader = Reader(file);

        var directory = reader.ReadDirectory(reader.Header.FirstDirectory)!;

        Assert.Equal(1, reader.ReadInteger(directory, 256, 0));
        Assert.Equal(2, reader.ReadInteger(directory, 257, 0));
        var strict = Reader(file, Strict);
        Assert.Throws<TiffException>(() => strict.ReadDirectory(strict.Header.FirstDirectory));
    }

    [Fact]
    public void A_loop_of_directories_ends_the_chain_or_is_refused_with_Strict()
    {
        var builder = new TiffBuilder();
        builder.AddDirectory().Add(256, TiffTagType.Short, 1);
        builder.AddDirectory().Add(256, TiffTagType.Short, 2);
        long first = builder.OffsetOf(0);
        builder.AddDirectory().Add(256, TiffTagType.Short, 3).NextOverride = first;
        byte[] file = builder.Build();

        var reader = Reader(file);
        var offsets = new List<long>();
        for (var d = reader.ReadDirectory(reader.Header.FirstDirectory); d is not null; d = d.Next == 0 ? null : reader.ReadDirectory(d.Next))
        {
            offsets.Add(d.Offset);
        }

        Assert.Equal(3, offsets.Count);
        var strict = Reader(file, Strict);
        var a = strict.ReadDirectory(strict.Header.FirstDirectory)!;
        var b = strict.ReadDirectory(a.Next)!;
        var c = strict.ReadDirectory(b.Next)!;
        Assert.Throws<TiffException>(() => strict.ReadDirectory(c.Next));
    }

    [Fact]
    public void Directory_offsets_outside_the_file_end_the_chain()
    {
        var builder = new TiffBuilder();
        builder.AddDirectory().Add(256, TiffTagType.Short, 1).NextOverride = 1_000_000;
        var reader = Reader(builder.Build());

        var directory = reader.ReadDirectory(reader.Header.FirstDirectory)!;

        Assert.Null(reader.ReadDirectory(directory.Next));
        Assert.Null(reader.ReadDirectory(3));
        Assert.Throws<TiffException>(() => Reader(builder.Build(), Strict).ReadDirectory(1_000_000));
    }

    [Fact]
    public void An_entry_count_beyond_the_file_is_refused_before_anything_is_allocated()
    {
        var builder = new TiffBuilder(bigTiff: true);
        builder.AddDirectory().Add(256, TiffTagType.Short, 1);
        byte[] file = builder.Build();
        long directory = Reader(file).Header.FirstDirectory;
        new TiffBuilder(bigTiff: true).U64(4000).CopyTo(file, directory);   // 4000 entries of 20 bytes: 80 KB, in a file of ~60 bytes

        var ex = Assert.Throws<TiffException>(() => Reader(file).ReadDirectory(directory));
        Assert.Null(ex.Limit);

        new TiffBuilder(bigTiff: true).U64(ulong.MaxValue).CopyTo(file, directory);
        Assert.Equal("MaxTagsPerDirectory", Assert.Throws<TiffException>(() => Reader(file).ReadDirectory(directory)).Limit);
    }

    [Fact]
    public void The_number_of_directories_is_limited()
    {
        var builder = new TiffBuilder();
        for (int i = 0; i < 6; i++)
        {
            builder.AddDirectory().Add(256, TiffTagType.Short, i);
        }

        var reader = Reader(builder.Build(), TiffReadOptions.Default with { MaxDirectories = 5 });
        var d = reader.ReadDirectory(reader.Header.FirstDirectory)!;
        for (int i = 1; i < 5; i++)
        {
            d = reader.ReadDirectory(d.Next)!;
        }

        Assert.Equal("MaxDirectories", Assert.Throws<TiffException>(() => reader.ReadDirectory(d.Next)).Limit);
    }

    [Fact]
    public void Tag_values_are_limited()
    {
        var builder = new TiffBuilder();
        builder.AddDirectory().Raw(34675, TiffTagType.Undefined, new byte[5000]);
        var reader = Reader(builder.Build(), TiffReadOptions.Default with { MaxTagBytes = 4096 });
        var directory = reader.ReadDirectory(reader.Header.FirstDirectory)!;

        Assert.Equal("MaxTagBytes", Assert.Throws<TiffException>(() => reader.ReadBytes(directory.Entries[34675])).Limit);
    }

    [Fact]
    public void Thousands_of_unknown_tags_are_read_in_linear_time()
    {
        var builder = new TiffBuilder();
        var directory = builder.AddDirectory();
        for (int code = 30000; code < 34000; code++)
        {
            directory.Add((ushort)code, TiffTagType.Short, 1);
        }

        byte[] file = builder.Build();
        var clock = Stopwatch.StartNew();
        var reader = Reader(file);
        var read = reader.ReadDirectory(reader.Header.FirstDirectory)!;

        Assert.Equal(4000, read.Entries.Count);
        Assert.True(clock.Elapsed < TimeSpan.FromMilliseconds(500), $"{clock.ElapsedMilliseconds} ms");
    }

    [Fact]
    public void A_truncated_next_offset_ends_the_chain_unless_Strict()
    {
        var builder = new TiffBuilder();
        builder.AddDirectory().Add(256, TiffTagType.Short, 1).Add(257, TiffTagType.Short, 1);
        byte[] full = builder.Build();
        long directory = Reader(full).Header.FirstDirectory;
        byte[] truncated = full[..(int)(directory + 2 + 2 * 12)];   // the entries, not the next offset

        Assert.Equal(0, Reader(truncated).ReadDirectory(directory)!.Next);
        Assert.Throws<TiffException>(() => Reader(truncated, Strict).ReadDirectory(directory));
    }

    [Fact]
    public void Hostile_directories_fail_within_bounds()
    {
        foreach (string name in new[] { "unknowntags-5k.tif", "unknowntags-20k.tif", "bigtiff-unknowntags-32k.tif" })
        {
            var hostile = HostileAssert.Cases.Single(c => c.ToString() == name);
            HostileAssert.FailsWithinBounds(hostile, () =>
            {
                var reader = new StructureReader(TiffSource.FromMemory(File.ReadAllBytes(hostile.Path)), new ResourceBudget(hostile.Options, name));
                reader.ReadDirectory(reader.Header.FirstDirectory);
            });
        }
    }
}
