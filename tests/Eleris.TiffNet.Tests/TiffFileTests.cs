using Eleris.TiffNet.Fuzz;
using Eleris.TiffNet.Tests.Fixtures;
using Eleris.TiffNet.Tests.Interop;

namespace Eleris.TiffNet.Tests;

public class TiffFileTests
{
    private static string Fixture(string name) => DomainFixtures.PathOf(name);

    [Fact]
    public async Task Open_Read_and_ReadAsync_give_the_same_pages()
    {
        foreach (var (name, entry) in DomainFixtures.Files)
        {
            using var opened = TiffFile.Open(Fixture(name));
            using var read = TiffFile.Read(new MemoryStream(File.ReadAllBytes(Fixture(name))));
            using var readAsync = await TiffFile.ReadAsync(new MemoryStream(File.ReadAllBytes(Fixture(name))));

            int pages = entry.GetProperty("pages").GetArrayLength();
            Assert.True(pages == opened.Count, $"{name}: {opened.Count} pages instead of {pages}");
            Assert.Equal(Describe(opened), Describe(read));
            Assert.Equal(Describe(opened), Describe(readAsync));
        }

        static string Describe(TiffFile file) => string.Join(" | ", file.Select(i => $"{i.Width}x{i.Height}x{i.Channels} {i.UnsupportedReason}"));
    }

    [Fact]
    public void The_primary_image_is_the_first_full_resolution_page()
    {
        using var thumbnailFirst = TiffFile.Open(Fixture("thumbnail_in_ifd0.tif"));
        Assert.Equal(2, thumbnailFirst.Count);
        Assert.True(thumbnailFirst[0].IsReducedResolution);
        Assert.Same(thumbnailFirst[1], thumbnailFirst.Primary);
        Assert.Equal((160, 120), (thumbnailFirst.Primary.Width, thumbnailFirst.Primary.Height));

        using var multipage = TiffFile.Open(Fixture("multipage.tif"));
        Assert.Equal(3, multipage.Count);
        Assert.Same(multipage[0], multipage.Primary);
        Assert.True(multipage[1].IsReducedResolution);
    }

    [Fact]
    public void Every_page_is_reduced_resolution_then_the_primary_is_the_first()
    {
        var builder = new TiffBuilder { Data = new byte[12] };
        builder.AddDirectory().Image(4, 3).Add(254, TiffTagType.Long, 1);
        builder.AddDirectory().Image(4, 3).Add(254, TiffTagType.Long, 1);

        using var file = TiffFile.Read(new MemoryStream(builder.Build()));

        Assert.Same(file[0], file.Primary);
    }

    [Fact]
    public void Sub_images_are_not_pages()
    {
        using var file = TiffFile.Open(Fixture("pyramid_subifds.tif"));

        Assert.Single(file);
        var levels = file[0].SubImages;
        Assert.Equal(2, levels.Count);
        Assert.Equal([(24, 32), (12, 16)], levels.Select(l => (l.Width, l.Height)));
        Assert.All(levels, l => Assert.True(l.IsReducedResolution));
    }

    [Fact]
    public void The_delivered_shape_is_exposed()
    {
        using var rgba = TiffFile.Open(Fixture("rgba_assocalpha.tif"));
        var image = rgba.Primary;

        Assert.Equal((TiffColorModel.Rgb, TiffSampleType.UInt16, 4), (image.ColorModel, image.SampleType, image.Channels));
        Assert.Equal([new TiffExtraChannel(TiffExtraChannelKind.AssociatedAlpha)], image.ExtraChannels);

        using var grey = TiffFile.Open(Fixture("grey_3_samples.tif"));
        Assert.Equal(TiffColorModel.Grey, grey.Primary.ColorModel);
        Assert.Equal(3, grey.Primary.Channels);
    }

    [Fact]
    public void An_unsupported_page_is_listed_and_says_why()
    {
        using var file = TiffFile.Open(Fixture("complex64.tif"));
        var image = file.Primary;

        Assert.Equal((8, 8), (image.Width, image.Height));
        Assert.Contains("complex", image.UnsupportedReason, StringComparison.Ordinal);
        var ex = Assert.Throws<NotSupportedException>(() => image.SampleType);
        Assert.StartsWith("complex64.tif: complex", ex.Message, StringComparison.Ordinal);
        Assert.Throws<NotSupportedException>(() => image.ColorModel);
    }

    [Fact]
    public void Data_that_is_not_TIFF_is_a_TiffException()
    {
        Assert.Throws<TiffException>(() => TiffFile.Read(new MemoryStream()));
        Assert.Throws<TiffException>(() => TiffFile.Read(new MemoryStream("not a TIFF file at all"u8.ToArray())));
        var noImage = new TiffBuilder().Build();   // a header whose first directory is 0
        Assert.Contains("no image", Assert.Throws<TiffException>(() => TiffFile.Read(new MemoryStream(noImage))).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_opened_file_is_closed_by_Dispose_and_after_a_failure()
    {
        string path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.tif");
        File.Copy(Fixture("multipage.tif"), path);
        var file = TiffFile.Open(path);
        file.Dispose();
        File.Delete(path);

        File.WriteAllText(path, "not a TIFF file");
        Assert.Throws<TiffException>(() => TiffFile.Open(path));
        File.Delete(path);   // the handle opened for the failed read is closed
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void Messages_name_the_file_by_its_name_only()
    {
        string folder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "private observations");
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, "damaged.tif");
        File.WriteAllBytes(path, [(byte)'I', (byte)'I', 42, 0, 200, 0, 0, 0]);
        try
        {
            var ex = Assert.Throws<TiffException>(() => TiffFile.Open(path, TiffReadOptions.Default with { Strict = true }));
            Assert.StartsWith("damaged.tif: ", ex.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("private", ex.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(folder)!, recursive: true);
        }
    }

    [Fact]
    public void Every_file_of_the_interop_corpus_opens()
    {
        if (!Directory.Exists(InteropCorpus.Folder))
        {
            Assert.True(InteropCorpus.Skipped, $"The interop corpus is not generated; set {InteropCorpus.SkipVariable}=1 to skip.");
            return;
        }

        foreach (var f in InteropCorpus.Files)
        {
            using var file = TiffFile.Open(Path.Combine(InteropCorpus.Folder, f.Name));
            Assert.True(file.Count == 1 && file.Primary.Channels == f.Channels, f.Name);
        }
    }
}

// Every hostile file must open (its defect is then in its pixels) or fail with its expected outcome, within its bounds.
[Collection(nameof(Measurement))]
public class HostileOpenTests
{
    [Fact]
    public void A_fuzzing_campaign_on_opening_finds_nothing()
    {
        var corpus = new Corpus([.. Directory.GetFiles(DomainFixtures.Folder, "*.tif").Order(StringComparer.Ordinal)]);
        string output = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            var campaign = new Campaign(corpus, Targets.All["open"], MutationMode.Mixed, seed: 2026, output);

            campaign.Run(TimeSpan.FromSeconds(20), maxIterations: 3000);

            Assert.False(campaign.Failed, string.Join(", ", campaign.Outcomes.Select(o => $"{o.Key}: {o.Value}")));
            Assert.True(campaign.Outcomes.GetValueOrDefault("ok") > 300, "most mutations should still open");
        }
        finally
        {
            Directory.Delete(output, recursive: true);
        }
    }


    [Fact]
    public void Hostile_files_open_or_fail_as_expected_within_bounds()
    {
        foreach (var hostile in HostileAssert.Cases)
        {
            HostileAssert.OpensOrFailsWithinBounds(hostile, () =>
            {
                using var file = TiffFile.Open(hostile.Path, hostile.Options);
                foreach (var image in file)
                {
                    _ = (image.Width, image.Height, image.Channels, image.UnsupportedReason);
                }
            });
        }
    }
}
