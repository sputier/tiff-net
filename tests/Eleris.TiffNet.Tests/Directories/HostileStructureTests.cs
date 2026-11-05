using Eleris.TiffNet.Internal;
using Eleris.TiffNet.Internal.Structure;
using Eleris.TiffNet.Tests.Fixtures;

namespace Eleris.TiffNet.Tests.Directories;

// The hostile files whose defect is in the structure: they must fail while the directories and tables are read, quickly and
// without allocating what they declare. They measure allocations, so they do not run in parallel with other tests.
[Collection(nameof(Measurement))]
public class HostileStructureTests
{
    private static readonly TimeSpan Seconds2 = TimeSpan.FromSeconds(2);

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

    [Fact]
    public void Hostile_tables_fail_quickly_without_allocating_them()
    {
        foreach (string name in new[] { "nstrips-2G.tif", "nstrips-256M.tif", "nstrips-256M-short.tif", "ntiles-256M.tif",
                     "dos-nstrips-2G.tif", "dos-nstrips-268M.tif" })
        {
            var hostile = HostileAssert.Cases.Single(c => c.ToString() == name);
            HostileAssert.FailsWithinBounds(hostile, () => ImageDescriptorTests.Images(File.ReadAllBytes(hostile.Path), hostile.Options));
        }
    }

    [Fact]
    public void Tags_sharing_their_values_cannot_multiply_the_cost_of_a_small_file()
    {
        // 4000 tags of 900 KB each, all pointing to the same bytes: 3.4 GB declared by a file of 1 MB.
        var builder = new TiffBuilder { Data = new byte[900_000] };
        var directory = builder.AddDirectory().Image(4, 3);
        for (int i = 0; i < 4000; i++)
        {
            directory.Pointing((ushort)(40000 + i), TiffTagType.Undefined, 900_000, (ulong)builder.DataOffset);
        }

        byte[] file = builder.Build();
        HostileAssert.FailsWithinBounds(() => TiffFile.Read(new MemoryStream(file)).Dispose(), "TiffException:MaxMetadataBytes",
            Seconds2, 300L << 20, "4000 overlapping tags");
    }

    [Fact]
    public void Strip_tables_shared_by_many_directories_are_bounded()
    {
        // 300 directories whose strip tables (500 000 strips each) are the same bytes.
        const int strips = 500_000;
        var builder = new TiffBuilder { Data = new byte[strips * 4] };
        for (int i = 0; i < 300; i++)
        {
            builder.AddDirectory()
                .Add(256, TiffTagType.Long, 1).Add(257, TiffTagType.Long, strips).Add(258, TiffTagType.Short, 8)
                .Add(262, TiffTagType.Short, 1).Add(278, TiffTagType.Long, 1)
                .Pointing(273, TiffTagType.Long, strips, (ulong)builder.DataOffset)
                .Pointing(279, TiffTagType.Long, strips, (ulong)builder.DataOffset);
        }

        byte[] file = builder.Build();
        HostileAssert.FailsWithinBounds(() => TiffFile.Read(new MemoryStream(file)).Dispose(), "TiffException:MaxMetadataBytes",
            Seconds2, 300L << 20, "300 directories sharing a table of 500 000 strips");
    }

    [Fact]
    public void A_deep_chain_of_sub_directories_cannot_exhaust_the_stack()
    {
        // 5000 levels: each directory's SubIFDs points to the next. Read recursively, this overflows a 1 MB stack.
        const int levels = 5000;
        var builder = new TiffBuilder { Data = new byte[12] };
        var directories = new List<TiffBuilder.Directory>();
        for (int i = 0; i < levels; i++)
        {
            var d = builder.AddDirectory().Image(4, 3).Add(330, TiffTagType.Long, 0);
            d.NextOverride = 0;
            directories.Add(d);
        }

        // Every directory keeps its size (a placeholder is replaced by its value), so one build gives every offset.
        var offsets = builder.Offsets();
        for (int i = 0; i < levels - 1; i++)
        {
            directories[i].Remove(330).Add(330, TiffTagType.Long, offsets[i + 1]);
        }

        byte[] file = builder.Build();
        TiffFile? read = null;
        var thread = new Thread(() => read = TiffFile.Read(new MemoryStream(file)), maxStackSize: 256 * 1024);
        thread.Start();
        thread.Join();

        Assert.NotNull(read);
        int depth = 0;
        for (var image = read!.Primary; image.SubImages.Count > 0; image = image.SubImages[0])
        {
            depth++;
        }

        Assert.Equal(TiffFile.MaxSubImageDepth, depth);
        Assert.Throws<TiffException>(() => TiffFile.Read(new MemoryStream(file), TiffReadOptions.Default with { Strict = true }));
    }
}
