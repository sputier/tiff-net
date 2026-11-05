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

    [Fact]
    public void The_orientation_expectations_follow_TIFF_6()
    {
        // An oracle independent of the Python generator: the stored image is 2 rows of 3 samples, 1 to 6; TIFF 6.0 says where its
        // row 0 and column 0 are on the visual image, and the delivered image is the visual one, top-down, left to right.
        ushort[,] stored = { { 1, 2, 3 }, { 4, 5, 6 } };
        Func<int, int, ushort>[] views =
        [
            (r, c) => stored[r, c],             // 1: top, left
            (r, c) => stored[r, 2 - c],         // 2: top, right
            (r, c) => stored[1 - r, 2 - c],     // 3: bottom, right
            (r, c) => stored[1 - r, c],         // 4: bottom, left
            (r, c) => stored[c, r],             // 5: left, top (transposed)
            (r, c) => stored[1 - c, r],         // 6: right, top (90 degrees clockwise)
            (r, c) => stored[1 - c, 2 - r],     // 7: right, bottom
            (r, c) => stored[c, 2 - r],         // 8: left, bottom (90 degrees counterclockwise)
        ];

        for (int value = 1; value <= 8; value++)
        {
            (int rows, int columns) = value <= 4 ? (2, 3) : (3, 2);
            var delivered = new ushort[rows * columns];
            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < columns; c++)
                {
                    delivered[r * columns + c] = views[value - 1](r, c);
                }
            }

            var expect = DomainFixtures.Expect($"orientation_{value}.tif")!.Value;
            string hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                System.Runtime.InteropServices.MemoryMarshal.AsBytes(delivered.AsSpan())));
            Assert.Equal(hash, expect.GetProperty("delivered").GetString());
            Assert.Equal([rows, columns], expect.GetProperty("deliveredShape").EnumerateArray().Select(e => e.GetInt32()));
        }
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

        // Low depths: MinIsWhite is inverted at every depth, a bilevel mask included.
        Assert.NotNull(DomainFixtures.Expect("miniswhite_1bit.tif"));

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
