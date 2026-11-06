using Eleris.TiffNet.Internal.Writing;
using Eleris.TiffNet.Tests.Fixtures;

namespace Eleris.TiffNet.Tests.Writing;

public class LayoutTests
{
    private static byte[] Write(TiffFile file, TiffWriteOptions options)
    {
        using var stream = new MemoryStream();
        file.Save(stream, options);
        return stream.ToArray();
    }

    public static TheoryData<TiffLayout, TiffPixelOrder, int> Layouts() => new()
    {
        { TiffLayout.Strips, TiffPixelOrder.Interleaved, 256 },
        { TiffLayout.Strips, TiffPixelOrder.Planar, 256 },
        { TiffLayout.Tiles, TiffPixelOrder.Interleaved, 16 },
        { TiffLayout.Tiles, TiffPixelOrder.Planar, 32 },
        { TiffLayout.Tiles, TiffPixelOrder.Interleaved, 256 },
    };

    [Theory]
    [MemberData(nameof(Layouts))]
    public void Every_layout_is_read_back_and_stored_as_asked(TiffLayout layout, TiffPixelOrder order, int tileSize)
    {
        var image = DirectoryWriterTests.Sample<ushort>(53, 37, 4, 3);
        foreach (var compression in new[] { TiffCompression.None, TiffCompression.PackBits, TiffCompression.Lzw, TiffCompression.Deflate })
        {
            var options = TiffWriteOptions.Default with { Layout = layout, PlanarConfiguration = order, TileSize = tileSize, Compression = compression };

            using var back = TiffFile.Read(new MemoryStream(Write(new TiffFile().Add(image), options)));

            var s = back.Primary.Storage!;
            Assert.Equal((layout, order, compression), (s.Layout, s.PlanarConfiguration, s.Compression));
            if (layout == TiffLayout.Tiles)
            {
                Assert.Equal((tileSize, tileSize), (s.TileWidth, s.TileHeight));
            }

            Assert.Equal(image.ReadRawPixels<ushort>(), back.Primary.ReadRawPixels<ushort>());
        }
    }

    [Fact]
    public void Strips_hold_about_256_KB()
    {
        var image = TiffImage.Create<float>(new float[1000 * 300], 1000, 300);   // rows of 4 000 bytes

        using var back = TiffFile.Read(new MemoryStream(Write(new TiffFile().Add(image), TiffWriteOptions.Default)));

        Assert.Equal(ImageEncoder.StripBytes / 4000, back.Primary.Storage!.RowsPerStrip);
    }

    [Fact]
    public void Edge_tiles_are_whole_and_padded_with_zeros()
    {
        // A 20 x 20 image in tiles of 16: the tiles at the right and the bottom are 16 x 16, their part beyond the image zeros.
        byte[] pixels = [.. Enumerable.Repeat((byte)0xAB, 20 * 20)];
        var image = TiffImage.Create<byte>(pixels, 20, 20);
        byte[] bytes = Write(new TiffFile().Add(image),
            TiffWriteOptions.Default with { Layout = TiffLayout.Tiles, TileSize = 16, Compression = TiffCompression.None });

        using var back = TiffFile.Read(new MemoryStream(bytes));
        var d = back.Primary.Descriptor;
        Assert.Equal(4, d.ByteCounts.Length);
        Assert.All(d.ByteCounts, c => Assert.Equal(256, c));
        byte[] lastTile = bytes.AsSpan((int)d.Offsets[3], 256).ToArray();
        Assert.Equal(16, lastTile.Count(b => b == 0xAB));   // 4 x 4 pixels of the image, the rest zeros
        Assert.Equal(pixels, back.Primary.ReadRawPixels<byte>());
    }

    [Fact]
    public void A_tiled_file_read_from_disk_is_written_in_strips_with_the_same_pixels()
    {
        using var file = TiffFile.Open(DomainFixtures.PathOf("siril_m42_rgb16_astro.tif"));

        using var back = TiffFile.Read(new MemoryStream(Write(file, TiffWriteOptions.Default with { Layout = TiffLayout.Tiles, TileSize = 64 })));
        using var again = TiffFile.Read(new MemoryStream(Write(back, TiffWriteOptions.Default)));

        Assert.Equal(file.Primary.ReadRawPixels<ushort>(), again.Primary.ReadRawPixels<ushort>());
    }
}
