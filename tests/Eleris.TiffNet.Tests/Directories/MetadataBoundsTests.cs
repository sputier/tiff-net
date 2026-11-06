using Eleris.TiffNet.Tests.Fixtures;

namespace Eleris.TiffNet.Tests.Directories;

/// <summary>Metadata costs what it really takes in memory, and a property of one value does not materialise a tag of millions.</summary>
[Collection(nameof(Measurement))]
public class MetadataBoundsTests
{
    [Fact]
    public void A_resolution_of_millions_of_values_reads_its_first_one_only()
    {
        // XResolution and YResolution of 4 million BYTE values each: 4 MB in the file, 32 MB each as an array of doubles.
        byte[] values = new byte[4 << 20];
        values[0] = 72;
        var builder = new TiffBuilder { Data = new byte[16] };
        builder.AddDirectory().Image(4, 4).Raw(282, TiffTagType.Byte, values).Raw(283, TiffTagType.Byte, values).Add(296, TiffTagType.Short, 2);
        using var file = TiffFile.Read(new MemoryStream(builder.Build()));
        var metadata = file.Primary.Metadata;

        GC.Collect();
        long before = GC.GetTotalAllocatedBytes(precise: true);
        var resolution = metadata.Resolution;
        long allocated = GC.GetTotalAllocatedBytes(precise: true) - before;

        Assert.Equal(72, resolution!.X);
        Assert.InRange(allocated, 0, 64 << 10);
    }

    [Fact]
    public void A_directory_counts_the_table_of_its_entries()
    {
        // 4 000 entries: 48 KB in the file, about 190 KB once read into a table, which is what MaxMetadataBytes bounds.
        var builder = new TiffBuilder { Data = new byte[16] };
        var directory = builder.AddDirectory().Image(4, 4);
        for (int i = 0; i < 4_000; i++)
        {
            directory.Add((ushort)(60_000 + i), TiffTagType.Short, 1);
        }

        directory.Sorted();
        byte[] bytes = builder.Build();

        var thrown = Assert.Throws<TiffException>(() => TiffFile.Read(new MemoryStream(bytes), TiffReadOptions.Default with { MaxMetadataBytes = 120 << 10 }));
        Assert.Equal("MaxMetadataBytes", thrown.Limit);
        using var file = TiffFile.Read(new MemoryStream(bytes), TiffReadOptions.Default with { MaxMetadataBytes = 512 << 10 });
        Assert.Equal(4_000, file.Primary.Metadata.Count(t => t.Code >= 60_000));
    }
}
