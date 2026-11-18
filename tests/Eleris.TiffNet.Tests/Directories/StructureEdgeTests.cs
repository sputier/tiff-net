using Eleris.TiffNet.Tests.Fixtures;

namespace Eleris.TiffNet.Tests.Directories;

// Edge cases of the structure: values that do not fit their tag, layouts the library refuses, and how images are listed.
public class StructureEdgeTests
{
    private static TiffFile Read(TiffBuilder builder, TiffReadOptions? options = null) => TiffFile.Read(new MemoryStream(builder.Build()), options);

    [Theory]
    [InlineData(259)]   // Compression
    [InlineData(277)]   // SamplesPerPixel
    [InlineData(339)]   // SampleFormat
    [InlineData(262)]   // PhotometricInterpretation
    [InlineData(317)]   // Predictor
    public void A_64_bit_value_in_a_SHORT_tag_is_a_damaged_file_never_narrowed(ushort tag)
    {
        var builder = new TiffBuilder(bigTiff: true) { Data = new byte[12] };
        builder.AddDirectory().Image(4, 3).Remove(tag).Add(tag, TiffTagType.Long8, (1L << 32) + 1);

        Assert.Throws<TiffException>(() => Read(builder));
    }

    [Fact]
    public void Tiles_larger_than_an_int_make_the_image_unsupported_not_negative()
    {
        var builder = new TiffBuilder { Data = new byte[64] };
        builder.AddDirectory().Image(16, 16).Remove(273).Remove(278).Remove(279)
            .Add(322, TiffTagType.Long, 0xFFFF_FFF0).Add(323, TiffTagType.Long, 16)
            .Add(324, TiffTagType.Long, builder.DataOffset).Add(325, TiffTagType.Long, 16);

        using var file = Read(builder);

        Assert.Contains("tiles of", file.Primary.UnsupportedReason, StringComparison.Ordinal);
        Assert.Equal(int.MaxValue, file.Primary.Storage!.TileWidth);
    }

    [Fact]
    public void A_table_of_bytes_is_refused()
    {
        var builder = new TiffBuilder { Data = new byte[12] };
        builder.AddDirectory().Image(4, 3).Remove(273).Add(273, TiffTagType.Byte, 8);

        Assert.Contains("SHORT, LONG or LONG8", Assert.Throws<TiffException>(() => Read(builder)).Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(3, 2, (ushort)2, "8-bit colour")]   // planar RGB JPEG
    [InlineData(3, 1, (ushort)1, "8-bit grey")]     // grey JPEG with two extra samples
    public void JPEG_layouts_the_decoder_does_not_handle_are_unsupported(ushort samples, ushort planar, ushort photometric, string reason)
    {
        var builder = new TiffBuilder { Data = new byte[100] };
        var directory = builder.AddDirectory().Image(4, 3, samples: samples, photometric: photometric).Remove(259).Add(259, TiffTagType.Short, 7)
            .Add(284, TiffTagType.Short, planar);
        if (planar == 2)
        {
            // One strip per plane.
            directory.Remove(273).Remove(279).Add(273, TiffTagType.Long, 8, 8, 8).Add(279, TiffTagType.Long, 12, 12, 12);
        }

        using var file = Read(builder);

        Assert.Contains(reason, file.Primary.UnsupportedReason, StringComparison.Ordinal);
    }

    [Fact]
    public void A_predictor_of_0_is_no_predictor()
    {
        var builder = new TiffBuilder { Data = new byte[12] };
        builder.AddDirectory().Image(4, 3).Add(317, TiffTagType.Short, 0);

        using var file = Read(builder);

        Assert.Null(file.Primary.UnsupportedReason);
        Assert.Equal(TiffPredictor.None, file.Primary.Storage!.Predictor);
    }

    [Fact]
    public void A_damaged_colour_map_does_not_fail_a_page_the_library_refuses_anyway()
    {
        var builder = new TiffBuilder { Data = new byte[12] };
        builder.AddDirectory().Image(4, 3, photometric: 3).Remove(259).Add(259, TiffTagType.Short, 50000).Add(320, TiffTagType.Short, 1, 2);

        using var file = Read(builder);

        Assert.Contains("ZSTD", file.Primary.UnsupportedReason, StringComparison.Ordinal);
    }

    [Fact]
    public void A_palette_image_with_an_extra_channel_delivers_RGB_and_the_extra_channel()
    {
        var builder = new TiffBuilder { Data = new byte[24] };
        var directory = builder.AddDirectory().Image(4, 3, samples: 2, photometric: 3).Add(338, TiffTagType.Short, 2);
        directory.Add(320, TiffTagType.Short, Enumerable.Range(0, 3 * 256).Select(i => (long)i).ToArray());

        using var file = Read(builder);

        Assert.Equal((TiffColorModel.Rgb, 4), (file.Primary.ColorModel, file.Primary.Channels));
        Assert.Equal([new TiffExtraChannel(TiffExtraChannelKind.UnassociatedAlpha)], file.Primary.ExtraChannels);
    }

    [Fact]
    public void Fewer_bits_per_sample_than_samples_are_refused_with_Strict()
    {
        var builder = new TiffBuilder { Data = new byte[48] };
        builder.AddDirectory().Image(4, 3, samples: 4, photometric: 2).Remove(258).Add(258, TiffTagType.Short, 8, 8);

        using (var tolerant = Read(builder))
        {
            Assert.Equal(4, tolerant.Primary.Channels);
        }

        Assert.Throws<TiffException>(() => Read(builder, TiffReadOptions.Default with { Strict = true }));
    }

    [Fact]
    public void The_size_of_an_image_is_that_of_the_delivered_pixels_with_its_orientation()
    {
        foreach (int value in Enumerable.Range(1, 8))
        {
            using var file = TiffFile.Open(DomainFixtures.PathOf($"orientation_{value}.tif"));   // stored: 3 wide, 2 tall
            var expected = value <= 4 ? (3, 2) : (2, 3);
            Assert.Equal(expected, (file.Primary.Width, file.Primary.Height));
            Assert.Equal(value, file.Primary.Storage!.Orientation);
        }
    }

    [Fact]
    public void A_damaged_sub_image_is_left_out_unless_Strict()
    {
        var builder = new TiffBuilder { Data = new byte[12] };
        var page = builder.AddDirectory().Image(4, 3).Add(330, TiffTagType.Long, 0);
        var sub = builder.AddDirectory().Image(2, 2).Remove(257);   // no ImageLength
        page.NextOverride = 0;
        long subOffset = builder.OffsetOf(1);
        page.Remove(330).Add(330, TiffTagType.Long, subOffset);

        using (var file = Read(builder))
        {
            Assert.Single(file);
            Assert.Empty(file.Primary.SubImages);
        }

        Assert.Throws<TiffException>(() => Read(builder, TiffReadOptions.Default with { Strict = true }));
    }

    [Fact]
    public void A_sub_directory_pointing_to_a_page_does_not_take_it_out_of_the_pages()
    {
        var builder = new TiffBuilder { Data = new byte[12] };
        var first = builder.AddDirectory().Image(4, 3).Add(330, TiffTagType.Long, 0);
        builder.AddDirectory().Image(4, 3);
        long second = builder.OffsetOf(1);
        first.Remove(330).Add(330, TiffTagType.Long, second);

        using var file = Read(builder);

        Assert.Equal(2, file.Count);
        Assert.Empty(file[0].SubImages);
    }

    [Fact]
    public void A_metadata_tag_larger_than_MaxTagBytes_is_left_out_so_that_the_image_opens()
    {
        var builder = new TiffBuilder { Data = new byte[12] };
        builder.AddDirectory().Image(4, 3).Text(305, "writer").Raw(34675, TiffTagType.Undefined, new byte[10_000]);
        var small = TiffReadOptions.Default with { MaxTagBytes = 4096 };

        using (var file = Read(builder, small))
        {
            Assert.Null(file.Primary.Metadata.IccProfile);
            Assert.Equal("writer", file.Primary.Metadata.Software);
        }

        Assert.Equal("MaxTagBytes", Assert.Throws<TiffException>(() => Read(builder, small with { Strict = true })).Limit);
    }

    [Fact]
    public void The_lists_of_an_image_cannot_be_changed_by_a_cast()
    {
        using var file = TiffFile.Open(DomainFixtures.PathOf("pyramid_subifds.tif"));

        Assert.False(file.Primary.SubImages is List<TiffImage>);
        Assert.False(file.Primary.ExtraChannels is List<TiffExtraChannel>);
        Assert.False(file.Primary.Metadata.Exif is List<TiffTag>);
    }

    [Fact]
    public void The_values_of_the_public_enumerations_do_not_change()
    {
        Assert.Equal([1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11], Enum.GetValues<TiffSampleType>().Select(v => (int)v));
        Assert.Equal([1, 5, 7, 8, 32773, -1], Enum.GetValues<TiffCompression>().Select(v => (int)v));   // Unknown is -1: no code of a file
        Assert.Equal([0, 1, 2, 3, 6, -1], Enum.GetValues<TiffPhotometric>().Select(v => (int)v));
        Assert.Equal([0, 1, 2, 3], Enum.GetValues<TiffPredictor>().Select(v => (int)v));
        Assert.Equal([1, 2], Enum.GetValues<TiffColorModel>().Select(v => (int)v));
        Assert.Equal([0, 1, 2], Enum.GetValues<TiffExtraChannelKind>().Select(v => (int)v));
        Assert.Equal([1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 16, 17, 18], Enum.GetValues<TiffTagType>().Select(v => (int)v));
        Assert.Equal([1, 2, 3], Enum.GetValues<TiffResolutionUnit>().Select(v => (int)v));
    }
}
