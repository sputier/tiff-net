using System.IO.Compression;
using Eleris.TiffNet.Tests.Fixtures;

namespace Eleris.TiffNet.Tests.Writing;

/// <summary>Saving a file that came from anywhere costs what reading it would: nothing is allocated for pixels a file does not hold.</summary>
[Collection(nameof(Measurement))]
public class HostileSaveTests
{
    [Theory]
    [InlineData(65_536, 2_000_000_000, "TiffException:MaxPixelBytes")]
    [InlineData(1_500_000_000, 2, "NotSupportedException")]          // rows larger than an array
    [InlineData(int.MaxValue, int.MaxValue, "NotSupportedException")]
    public void A_tiny_file_declaring_a_huge_image_fails_before_anything_is_allocated(long width, long height, string outcome)
    {
        // A few hundred bytes: one uncompressed strip of 200 bytes for an image of billions of pixels.
        var builder = new TiffBuilder { Data = new byte[200] };
        builder.AddDirectory().Image(width, height).Remove(279).Add(279, TiffTagType.Long, 200);
        using var file = TiffFile.Read(new MemoryStream(builder.Build()));

        HostileAssert.FailsWithinBounds(() => file.Save(Stream.Null), outcome, TimeSpan.FromSeconds(1), 1 << 20, "huge save");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(8)]
    public void Strips_sharing_their_data_cost_the_pixels_they_declare(ushort compression)
    {
        // 100 000 strips of one row of 1 000 bytes, all pointing at the same data: 100 MB of pixels from a file of 1.6 MB.
        const int strips = 100_000;
        byte[] data = compression == 8 ? Compress(new byte[1000]) : new byte[1000];
        var builder = new TiffBuilder { Data = data };
        builder.AddDirectory().Image(1000, strips).Remove(259).Add(259, TiffTagType.Short, compression).Remove(273).Remove(278).Remove(279)
            .Add(273, TiffTagType.Long, [.. Enumerable.Repeat(builder.DataOffset, strips)])
            .Add(278, TiffTagType.Long, 1)
            .Add(279, TiffTagType.Long, [.. Enumerable.Repeat((long)data.Length, strips)]);
        using var file = TiffFile.Read(new MemoryStream(builder.Build()), TiffReadOptions.Default with { MaxPixelBytes = 64L << 20 });

        HostileAssert.FailsWithinBounds(() => file.Save(Stream.Null), "TiffException:MaxPixelBytes", TimeSpan.FromSeconds(2), 4L << 20,
            "shared strips");
    }

    [Fact]
    public void A_refused_save_creates_nothing_and_says_how_to_save_the_image()
    {
        var builder = new TiffBuilder { Data = new byte[200] };
        builder.AddDirectory().Image(65_536, 2_000_000_000).Remove(279).Add(279, TiffTagType.Long, 200);
        using var file = TiffFile.Read(new MemoryStream(builder.Build()));
        string path = SaveTests.Temporary();
        try
        {
            var thrown = Assert.Throws<TiffException>(() => file.Save(path));

            Assert.Equal("MaxPixelBytes", thrown.Limit);
            Assert.Contains("Open the file with a larger MaxPixelBytes", thrown.Message, StringComparison.Ordinal);
            Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(path)!));
        }
        finally
        {
            SaveTests.Delete(path);
        }
    }

    [Fact]
    public void An_image_within_the_limits_is_saved_band_by_band()
    {
        // The same file within MaxPixelBytes: saved, every row read back.
        const int strips = 1_000;
        byte[] row = [.. Enumerable.Range(0, 1000).Select(i => (byte)i)];
        var builder = new TiffBuilder { Data = row };
        builder.AddDirectory().Image(1000, strips).Remove(273).Remove(278).Remove(279)
            .Add(273, TiffTagType.Long, [.. Enumerable.Repeat(builder.DataOffset, strips)])
            .Add(278, TiffTagType.Long, 1)
            .Add(279, TiffTagType.Long, [.. Enumerable.Repeat(1000L, strips)]);
        using var file = TiffFile.Read(new MemoryStream(builder.Build()));
        using var output = new MemoryStream();

        file.Save(output);

        using var back = TiffFile.Read(new MemoryStream(output.ToArray()));
        byte[] pixels = back.Primary.ReadRawPixels<byte>();
        Assert.All(Enumerable.Range(0, strips), y => Assert.Equal(row, pixels.AsSpan(y * 1000, 1000).ToArray()));
    }

    [Fact]
    public void A_cancelled_save_stops_and_leaves_the_target_as_it_was()
    {
        string path = SaveTests.Temporary();
        File.WriteAllBytes(path, [1, 2, 3]);
        try
        {
            var file = new TiffFile().Add(BandWritingTests.Gradient(512, 512, 1));
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();

            Assert.ThrowsAny<OperationCanceledException>(() => file.Save(path, cancellationToken: cancelled.Token));
            Assert.ThrowsAny<OperationCanceledException>(() => file.Save(new MemoryStream(), cancellationToken: cancelled.Token));

            Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(path));
            Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(path)!, ".tiff-*.tmp*"));
        }
        finally
        {
            SaveTests.Delete(path);
        }
    }

    private static byte[] Compress(byte[] data)
    {
        using var output = new MemoryStream();
        using (var zlib = new ZLibStream(output, CompressionLevel.Optimal))
        {
            zlib.Write(data);
        }

        return output.ToArray();
    }
}
