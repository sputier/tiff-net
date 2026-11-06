namespace Eleris.TiffNet.Tests.Writing;

public class ImageWriterTests
{
    private static byte[] Saved(TiffImage image, TiffWriteOptions options)
    {
        using var stream = new MemoryStream();
        new TiffFile().Add(image).Save(stream, options);
        return stream.ToArray();
    }

    public static TheoryData<TiffLayout, TiffPixelOrder, TiffCompression, int> Cases() => new()
    {
        { TiffLayout.Strips, TiffPixelOrder.Interleaved, TiffCompression.Deflate, 1 },
        { TiffLayout.Strips, TiffPixelOrder.Planar, TiffCompression.Lzw, 4 },
        { TiffLayout.Tiles, TiffPixelOrder.Interleaved, TiffCompression.PackBits, 4 },
        { TiffLayout.Tiles, TiffPixelOrder.Planar, TiffCompression.None, 1 },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void Writing_piece_by_piece_gives_the_file_Save_gives(TiffLayout layout, TiffPixelOrder order, TiffCompression compression, int threads)
    {
        const int width = 300, height = 211, channels = 3;
        var image = DirectoryWriterTests.Sample<ushort>(width, height, channels, 9);
        ushort[] pixels = image.ReadRawPixels<ushort>();
        var options = TiffWriteOptions.Default with
        {
            Layout = layout, PlanarConfiguration = order, Compression = compression, TileSize = 64, MaxDegreeOfParallelism = threads,
        };
        using var stream = new MemoryStream();

        using (var writer = TiffImageWriter.Create(stream, width, height, TiffSampleType.UInt16, channels, TiffColorModel.Rgb, options: options))
        {
            if (layout == TiffLayout.Strips)
            {
                // Rows in uneven pieces, across the strips.
                for (int y = 0; y < height;)
                {
                    int rows = Math.Min(height - y, 1 + y % 37);
                    writer.WriteRows<ushort>(pixels.AsSpan(y * width * channels, rows * width * channels), rows);
                    y += rows;
                }
            }
            else
            {
                // Tiles from the last to the first, edge tiles given only within the image.
                for (int ty = 3; ty >= 0; ty--)
                {
                    for (int tx = 4; tx >= 0; tx--)
                    {
                        int w = Math.Min(64, width - tx * 64), h = Math.Min(64, height - ty * 64);
                        var tile = new ushort[w * h * channels];
                        for (int r = 0; r < h; r++)
                        {
                            pixels.AsSpan(((ty * 64 + r) * width + tx * 64) * channels, w * channels).CopyTo(tile.AsSpan(r * w * channels));
                        }

                        writer.WriteTile<ushort>(tx, ty, tile);
                    }
                }
            }

            writer.Complete();
        }

        if (layout == TiffLayout.Strips && order == TiffPixelOrder.Interleaved)
        {
            Assert.Equal(Saved(image, options), stream.ToArray());
        }

        using var back = TiffFile.Read(new MemoryStream(stream.ToArray()));
        Assert.Equal(pixels, back.Primary.ReadRawPixels<ushort>());
    }

    [Fact]
    public void Missing_rows_or_tiles_are_refused_on_completion()
    {
        using var rows = TiffImageWriter.Create(new MemoryStream(), 10, 10, TiffSampleType.UInt8);
        rows.WriteRows<byte>(new byte[30], 3);
        Assert.Throws<InvalidOperationException>(rows.Complete);
        Assert.Throws<InvalidOperationException>(() => rows.WriteRows<byte>(new byte[80], 8));   // beyond the image

        var tiled = TiffWriteOptions.Default with { Layout = TiffLayout.Tiles, TileSize = 16 };
        using var tiles = TiffImageWriter.Create(new MemoryStream(), 20, 20, TiffSampleType.UInt8, options: tiled);
        tiles.WriteTile<byte>(0, 0, new byte[256]);
        Assert.Throws<InvalidOperationException>(() => tiles.WriteTile<byte>(0, 0, new byte[256]));   // each once
        Assert.Throws<InvalidOperationException>(tiles.Complete);
        Assert.Throws<ArgumentOutOfRangeException>(() => tiles.WriteTile<byte>(2, 0, new byte[256]));
        Assert.Throws<ArgumentException>(() => tiles.WriteTile<byte>(1, 0, new byte[7]));
        Assert.Throws<InvalidOperationException>(() => tiles.WriteRows<byte>(new byte[20], 1));
    }

    [Fact]
    public void The_samples_must_be_of_the_declared_type()
    {
        using var writer = TiffImageWriter.Create(new MemoryStream(), 4, 4, TiffSampleType.Float32);

        Assert.Throws<ArgumentException>(() => writer.WriteRows<double>(new double[16], 4));
        Assert.Throws<ArgumentException>(() => TiffImageWriter.Create(new MemoryStream(), 4, 4, TiffSampleType.UInt8, channels: 3));
        Assert.Throws<ArgumentException>(() => TiffImageWriter.Create(new MemoryStream([], writable: false), 4, 4, TiffSampleType.UInt8));
    }

    [Fact]
    public void A_path_is_replaced_on_completion_and_left_as_it_was_when_abandoned()
    {
        string path = SaveTests.Temporary();
        File.WriteAllBytes(path, [1, 2, 3]);
        try
        {
            using (var abandoned = TiffImageWriter.Create(path, 4, 4, TiffSampleType.UInt8))
            {
                abandoned.WriteRows<byte>(new byte[16], 4);
            }

            Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(path));
            Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(path)!, ".tiff-*.tmp*"));

            using (var writer = TiffImageWriter.Create(path, 4, 4, TiffSampleType.UInt8))
            {
                writer.WriteRows<byte>([.. Enumerable.Range(0, 16).Select(i => (byte)i)], 4);
                writer.Complete();
            }

            using var back = TiffFile.Open(path);
            Assert.Equal(Enumerable.Range(0, 16).Select(i => (byte)i), back.Primary.ReadRawPixels<byte>());
        }
        finally
        {
            SaveTests.Delete(path);
        }
    }

    [Fact]
    public void BigTIFF_is_decided_from_the_geometry_before_the_first_byte()
    {
        var tiled = TiffWriteOptions.Default with { Layout = TiffLayout.Tiles, TileSize = 1024, Compression = TiffCompression.None };
        using var large = new MemoryStream();
        using (TiffImageWriter.Create(large, 70_000, 70_000, TiffSampleType.UInt8, options: tiled))
        {
            // 4.9 GB of pixels: BigTIFF from the start (the writer is abandoned without data).
        }

        using var small = new MemoryStream();
        using (TiffImageWriter.Create(small, 100, 100, TiffSampleType.UInt8))
        {
        }

        Assert.Equal((byte)43, large.ToArray()[2]);
        Assert.Equal((byte)42, small.ToArray()[2]);
    }

    [Fact]
    public void Metadata_is_written_with_the_image()
    {
        var metadata = new TiffMetadata { Description = "OBJECT  = 'M42'", IccProfile = [1, 2, 3, 4, 5] };
        using var stream = new MemoryStream();
        using (var writer = TiffImageWriter.Create(stream, 2, 2, TiffSampleType.Float32, metadata: metadata))
        {
            writer.WriteRows<float>([1f, 2f, 3f, 4f], 2);
            writer.Complete();
        }

        using var back = TiffFile.Read(new MemoryStream(stream.ToArray()));
        Assert.Equal(metadata.Description, back.Primary.Metadata.Description);
        Assert.Equal(metadata.IccProfile, back.Primary.Metadata.IccProfile);
    }
}
