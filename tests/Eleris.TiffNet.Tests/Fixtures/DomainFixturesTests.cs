namespace Eleris.TiffNet.Tests.Fixtures;

// The domain fixtures are committed: these tests make sure they are intact and that the manifest holds the expectations the
// readers will be tested against.
public class DomainFixturesTests
{
    [Fact]
    public void Every_fixture_is_intact_and_small()
    {
        Assert.Equal(DomainFixtures.Files.Keys.Order(StringComparer.Ordinal),
            Directory.GetFiles(DomainFixtures.Folder, "*.tif").Select(Path.GetFileName).Order(StringComparer.Ordinal));
        Assert.All(DomainFixtures.Files, file =>
        {
            string path = DomainFixtures.PathOf(file.Key);
            Assert.True(new FileInfo(path).Length <= 1 << 20, $"{file.Key} is larger than 1 MB");
            Assert.Equal(file.Value.GetProperty("fileSha256").GetString(), DomainFixtures.FileSha256(path));
        });
    }

    [Theory]
    [InlineData("siril_m42_grey16_astro.tif", 1, 16)]
    [InlineData("siril_m42_grey16_deflate.tif", 1, 16)]
    [InlineData("siril_m42_grey32_astro.tif", 1, 32)]
    [InlineData("siril_m42_rgb16_astro.tif", 3, 16)]
    [InlineData("siril_m42_rgb32_astro.tif", 3, 32)]
    public void Siril_files_carry_an_Astro_TIFF_header_and_an_ICC_profile(string name, int channels, int bits)
    {
        var page = DomainFixtures.Page(name);

        Assert.Equal(channels, page.GetProperty("samplesPerPixel").GetInt32());
        Assert.Equal(bits, page.GetProperty("bitsPerSample").GetInt32());
        Assert.StartsWith("SIMPLE  =", page.GetProperty("description").GetString(), StringComparison.Ordinal);
        Assert.Contains("OBJECT  = 'M42", page.GetProperty("description").GetString(), StringComparison.Ordinal);
        Assert.True(page.GetProperty("iccBytes").GetInt32() > 1000);
        Assert.StartsWith("siril", page.GetProperty("software").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public void The_cases_of_the_design_are_present()
    {
        // The primary image is not IFD0 when IFD0 is a reduced-resolution thumbnail.
        Assert.Equal(1, DomainFixtures.Expect("thumbnail_in_ifd0.tif")!.Value.GetProperty("primary").GetInt32());
        Assert.Equal(1, DomainFixtures.Page("thumbnail_in_ifd0.tif").GetProperty("subfileType").GetInt32());

        // The 8 orientations, each with the samples a reader delivers top-down.
        for (int value = 1; value <= 8; value++)
        {
            string name = $"orientation_{value}.tif";
            Assert.Equal(value, DomainFixtures.Page(name).GetProperty("orientation").GetInt32());
            Assert.Matches("^[0-9A-F]{64}$", DomainFixtures.Expect(name)!.Value.GetProperty("delivered").GetString());
        }

        // Grey with three samples stays grey; the kinds of extra channel are both there.
        Assert.Equal("grey", DomainFixtures.Expect("grey_3_samples.tif")!.Value.GetProperty("colorModel").GetString());
        Assert.Equal([1], DomainFixtures.Page("rgba_assocalpha.tif").GetProperty("extraSamples").EnumerateArray().Select(e => e.GetInt32()));
        Assert.Equal([2], DomainFixtures.Page("rgba_unassalpha.tif").GetProperty("extraSamples").EnumerateArray().Select(e => e.GetInt32()));

        // Low depths, MinIsWhite and palettes, with the samples to deliver.
        foreach (int bits in new[] { 1, 2, 4, 12, 14 })
        {
            Assert.Contains(DomainFixtures.Files, f => f.Value.GetProperty("pages")[0].GetProperty("bitsPerSample").GetInt32() == bits);
        }

        Assert.NotNull(DomainFixtures.Expect("miniswhite_16bit.tif"));
        Assert.Equal("rgb", DomainFixtures.Expect("palette_8bit.tif")!.Value.GetProperty("colorModel").GetString());

        // Text that is not ASCII, as many tools write it.
        Assert.Equal("Nébuleuse d'Orion — M42 ✨", DomainFixtures.Page("description_utf8.tif").GetProperty("description").GetString());

        // JPEG written by Pillow, for the JPEG decoder.
        Assert.Equal(7, DomainFixtures.Page("pillow_rgb8_jpeg.tif").GetProperty("compression").GetInt32());
    }
}
