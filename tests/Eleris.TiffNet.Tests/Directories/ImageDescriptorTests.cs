using Eleris.TiffNet.Internal;
using Eleris.TiffNet.Internal.Structure;
using Eleris.TiffNet.Tests.Fixtures;
using Eleris.TiffNet.Tests.Interop;

namespace Eleris.TiffNet.Tests.Directories;

public class ImageDescriptorTests
{
    private static readonly TiffReadOptions Strict = TiffReadOptions.Default with { Strict = true };

    /// <summary>The images of the main chain of directories.</summary>
    internal static List<ImageDescriptor> Images(byte[] file, TiffReadOptions? options = null)
    {
        var reader = new StructureReader(TiffSource.FromMemory(file, "test.tif"), new ResourceBudget(options ?? TiffReadOptions.Default, "test.tif"));
        var images = new List<ImageDescriptor>();
        for (var d = reader.ReadDirectory(reader.Header.FirstDirectory); d is not null; d = d.Next == 0 ? null : reader.ReadDirectory(d.Next))
        {
            images.Add(ImageDescriptor.Read(reader, d));
        }

        return images;
    }

    private static ImageDescriptor Image(TiffBuilder builder, TiffReadOptions? options = null) => Images(builder.Build(), options).Single();

    [Fact]
    public void Every_page_of_the_domain_fixtures_is_described_as_tifffile_reads_it()
    {
        foreach (var (name, file) in DomainFixtures.Files)
        {
            var images = Images(File.ReadAllBytes(DomainFixtures.PathOf(name)));
            var pages = file.GetProperty("pages");
            Assert.True(pages.GetArrayLength() == images.Count, $"{name}: {images.Count} pages instead of {pages.GetArrayLength()}");
            for (int i = 0; i < images.Count; i++)
            {
                var page = pages[i];
                var image = images[i];
                string what = $"{name}, page {i}";
                Assert.True(page.GetProperty("width").GetInt64() == image.Width, what);
                Assert.True(page.GetProperty("height").GetInt64() == image.Height, what);
                Assert.True(page.GetProperty("samplesPerPixel").GetInt32() == image.SamplesPerPixel, what);
                Assert.True(page.GetProperty("bitsPerSample").GetInt32() == image.BitsPerSample, what);
                Assert.True(page.GetProperty("sampleFormat").GetInt32() == image.SampleFormat, what);
                Assert.True(page.GetProperty("photometric").GetInt32() == (int)image.Photometric, what);
                Assert.True(page.GetProperty("compression").GetInt32() == (int)image.Compression, what);
                Assert.True(page.GetProperty("planar").GetBoolean() == image.Planar, what);
                Assert.True(page.GetProperty("orientation").GetInt32() == image.Orientation, what);
                Assert.True(((page.GetProperty("subfileType").GetInt32() & 1) != 0) == image.IsReducedResolution, what);
                Assert.Equal(page.GetProperty("extraSamples").EnumerateArray().Select(e => (TiffExtraChannelKind)e.GetInt32()), image.ExtraChannels);
            }
        }
    }

    [Fact]
    public void The_delivered_samples_follow_the_design()
    {
        ImageDescriptor First(string name) => Images(File.ReadAllBytes(DomainFixtures.PathOf(name)))[0];

        Assert.Equal((TiffColorModel.Grey, TiffSampleType.UInt16, 3), Shape(First("grey_3_samples.tif")));
        Assert.Equal((TiffColorModel.Rgb, TiffSampleType.UInt16, 3), Shape(First("palette_8bit.tif")));
        Assert.Equal((TiffColorModel.Rgb, TiffSampleType.UInt16, 4), Shape(First("rgba_assocalpha.tif")));
        Assert.Equal((TiffColorModel.Grey, TiffSampleType.UInt8, 1), Shape(First("grey_1bit.tif")));
        Assert.Equal((TiffColorModel.Grey, TiffSampleType.UInt16, 1), Shape(First("grey_12bit_packed.tif")));
        Assert.Equal((TiffColorModel.Grey, TiffSampleType.Float16, 1), Shape(First("float16.tif")));
        Assert.Equal((TiffColorModel.Grey, TiffSampleType.Float32, 1), Shape(First("siril_m42_grey32_astro.tif")));
        Assert.Equal((TiffColorModel.Rgb, TiffSampleType.UInt8, 3), Shape(First("pillow_rgb8_jpeg.tif")));
        Assert.Equal([TiffExtraChannelKind.UnassociatedAlpha], First("grey_alpha_unassalpha.tif").ExtraChannels);
        Assert.Equal(3 << 8, First("palette_8bit.tif").ColorMap!.Length);
        Assert.Contains("complex", First("complex64.tif").Unsupported, StringComparison.Ordinal);

        static (TiffColorModel, TiffSampleType, int) Shape(ImageDescriptor d) => (d.ColorModel, d.SampleType, d.Channels);
    }

    [Fact]
    public void Every_file_of_the_interop_corpus_is_described_by_its_name()
    {
        if (!Directory.Exists(InteropCorpus.Folder))
        {
            Assert.True(InteropCorpus.Skipped, $"The interop corpus is not generated; set {InteropCorpus.SkipVariable}=1 to skip.");
            return;
        }

        foreach (var f in InteropCorpus.Files)
        {
            var image = Images(File.ReadAllBytes(Path.Combine(InteropCorpus.Folder, f.Name))).Single();
            string what = f.Name;
            Assert.True(image.Width == InteropCorpus.Width && image.Height == InteropCorpus.Height, what);
            Assert.True(image.Channels == f.Channels, what);
            Assert.True(image.Planar == f.Planar, what);
            Assert.True((image.Layout == TiffLayout.Tiles) == f.Tiled, what);
            Assert.True((image.Predictor != TiffPredictor.None) == f.Predictor, what);
            Assert.True(f.Supported == (image.Unsupported is null), $"{what}: {image.Unsupported}");
            if (f.Supported)
            {
                Assert.True(image.SampleType.ToString().Equals(f.SampleType, StringComparison.OrdinalIgnoreCase), $"{what}: {image.SampleType}");
            }
        }
    }

    [Theory]
    [InlineData(256)]
    [InlineData(257)]
    [InlineData(273)]
    public void A_missing_required_tag_is_a_damaged_file(ushort tag)
    {
        var builder = new TiffBuilder();
        builder.AddDirectory().Image(4, 3).Remove(tag);

        var ex = Assert.Throws<TiffException>(() => Image(builder));
        Assert.Null(ex.Limit);
    }

    [Fact]
    public void Byte_counts_of_uncompressed_strips_are_computed_when_missing()
    {
        var builder = new TiffBuilder { Data = new byte[4 * 10] };
        var directory = builder.AddDirectory().Image(4, 10).Remove(278).Remove(279).Remove(273);
        directory.Add(278, TiffTagType.Short, 4).Add(273, TiffTagType.Long, 8, 24, 40);

        var image = Image(builder);

        Assert.Equal([16, 16, 8], image.ByteCounts);
        Assert.Throws<TiffException>(() => Image(builder, Strict));
    }

    [Fact]
    public void Byte_counts_of_compressed_strips_are_required()
    {
        var builder = new TiffBuilder { Data = new byte[40] };
        builder.AddDirectory().Image(4, 10).Remove(279).Remove(259).Add(259, TiffTagType.Short, 8);

        Assert.Throws<TiffException>(() => Image(builder));
    }

    [Fact]
    public void A_table_shorter_than_the_geometry_is_a_damaged_file()
    {
        var builder = new TiffBuilder { Data = new byte[40] };
        builder.AddDirectory().Image(4, 10).Remove(278).Add(278, TiffTagType.Short, 2);   // 5 strips, tables of 1

        var ex = Assert.Throws<TiffException>(() => Image(builder));
        Assert.Contains("5 strips", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Strips_outside_the_file_are_refused_and_a_cut_last_strip_is_clamped()
    {
        var outside = new TiffBuilder { Data = new byte[40] };
        outside.AddDirectory().Image(4, 10).Remove(273).Add(273, TiffTagType.Long, 1_000_000);
        Assert.Throws<TiffException>(() => Image(outside));

        var cut = new TiffBuilder { Data = new byte[40] };
        cut.AddDirectory().Image(4, 10).Remove(279).Add(279, TiffTagType.Long, 1_000_000);
        var image = Image(cut);
        Assert.True(image.Offsets[0] + image.ByteCounts[0] <= cut.Build().Length);
        Assert.Throws<TiffException>(() => Image(cut, Strict));
    }

    [Fact]
    public void A_missing_photometric_interpretation_is_inferred_unless_Strict()
    {
        var grey = new TiffBuilder { Data = new byte[12] };
        grey.AddDirectory().Image(4, 3).Remove(262);
        var rgb = new TiffBuilder { Data = new byte[36] };
        rgb.AddDirectory().Image(4, 3, samples: 3).Remove(262);

        Assert.Equal(TiffPhotometric.MinIsBlack, Image(grey).Photometric);
        Assert.Equal(TiffPhotometric.Rgb, Image(rgb).Photometric);
        Assert.Throws<TiffException>(() => Image(grey, Strict));
    }

    [Fact]
    public void An_RGB_image_needs_three_samples()
    {
        var builder = new TiffBuilder { Data = new byte[24] };
        builder.AddDirectory().Image(4, 3, samples: 2, photometric: 2);

        Assert.Throws<TiffException>(() => Image(builder));
    }

    [Fact]
    public void A_palette_image_needs_its_colour_map()
    {
        var builder = new TiffBuilder { Data = new byte[12] };
        builder.AddDirectory().Image(4, 3, photometric: 3);

        Assert.Contains("ColorMap", Assert.Throws<TiffException>(() => Image(builder)).Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(259, 3, "CCITT")]
    [InlineData(259, 6, "old-style JPEG")]
    [InlineData(259, 50000, "ZSTD")]
    [InlineData(259, 34925, "LZMA")]
    [InlineData(262, 5, "CMYK")]
    [InlineData(262, 8, "Lab")]
    [InlineData(262, 32803, "raw decoder")]
    [InlineData(262, 6, "YCbCr images are supported with JPEG compression only")]
    [InlineData(258, 24, "24-bit")]
    public void Valid_features_the_library_does_not_handle_are_described(ushort tag, int value, string reason)
    {
        var builder = new TiffBuilder { Data = new byte[400] };
        var directory = builder.AddDirectory().Image(4, 3, samples: tag == 262 && value is 5 or 8 or 6 ? (ushort)3 : (ushort)1).Remove(tag);
        directory.Add(tag, TiffTagType.Short, value);

        var image = Image(builder);

        Assert.Contains(reason, image.Unsupported, StringComparison.Ordinal);
    }

    [Fact]
    public void Subfile_types_mark_reduced_resolution_images()
    {
        var builder = new TiffBuilder { Data = new byte[12] };
        builder.AddDirectory().Image(4, 3).Add(254, TiffTagType.Long, 1);
        builder.AddDirectory().Image(4, 3).Add(255, TiffTagType.Short, 2);
        builder.AddDirectory().Image(4, 3).Add(254, TiffTagType.Long, 2);

        var images = Images(builder.Build());

        Assert.Equal([true, true, false], images.Select(i => i.IsReducedResolution));
    }
}
