namespace Eleris.TiffNet.Tests.Directories;

/// <summary>
/// Each refusal of the structure comes from the check that names it, not from a later one that happens to fail too (the
/// mutation testing of #51 found refusals that could be removed without any test noticing).
/// </summary>
public class StructureRefusalTests
{
    private static readonly TiffReadOptions Strict = TiffReadOptions.Default with { Strict = true };

    [Theory]
    [InlineData(new byte[] { (byte)'I', (byte)'I', 42, 0, 8, 0 }, "a header has at least 8")]
    [InlineData(new byte[] { (byte)'I', (byte)'I', 41, 0, 8, 0, 0, 0 }, "version 41; 42 for TIFF, 43 for BigTIFF")]
    [InlineData(new byte[] { (byte)'I', (byte)'I', 43, 0, 8, 0, 0, 0, 16, 0, 0, 0 }, "the BigTIFF header is truncated")]
    public void A_header_is_refused_for_what_it_lacks(byte[] bytes, string message)
    {
        var thrown = Assert.Throws<TiffException>(() => TiffFile.Read(new MemoryStream(bytes)));

        Assert.Contains(message, thrown.Message, StringComparison.Ordinal);
    }

    public static TheoryData<string, bool, string> Directories() => new()
    {
        { "tiles of no width", false, "the tiles are 0 x 16 pixels" },
        { "tiles not multiples of 16", true, "the tiles are 17 x 16, not multiples of 16" },
        { "planar configuration 3", true, "PlanarConfiguration is 3." },
        { "orientation 9", true, "Orientation is 9." },
        { "no width", false, "the required tag ImageWidth is missing." },
        { "no strip offsets", false, "the strip offsets are missing." },
        { "palette without colour map", false, "the ColorMap of a palette image is missing." },
        { "colour map too short", false, "the ColorMap has 3 values, 768 are expected for 8-bit indices." },
        { "a tag twice", true, "tag 305 appears twice" },
        { "tags out of order", true, "are not in ascending order (tag 305 after 315)" },
    };

    [Theory]
    [MemberData(nameof(Directories))]
    public void A_directory_is_refused_by_the_check_that_names_its_defect(string defect, bool strict, string message)
    {
        byte[] file = Build(defect);

        var thrown = Assert.Throws<TiffException>(() => TiffFile.Read(new MemoryStream(file), strict ? Strict : TiffReadOptions.Default));

        Assert.Contains(message, thrown.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Without_Strict_invalid_codes_are_read_as_their_defaults()
    {
        using var planar = TiffFile.Read(new MemoryStream(Build("planar configuration 3")));
        using var orientation = TiffFile.Read(new MemoryStream(Build("orientation 9")));
        using var single = TiffFile.Read(new MemoryStream(Build("planar with one sample")));

        Assert.Equal(TiffPixelOrder.Interleaved, planar.Primary.Storage!.PlanarConfiguration);
        Assert.Equal(1, orientation.Primary.Storage!.Orientation);
        Assert.Equal(TiffPixelOrder.Interleaved, single.Primary.Storage!.PlanarConfiguration);
        Assert.Equal(new byte[16], single.Primary.ReadRawPixels<byte>());
    }

    private static byte[] Build(string defect)
    {
        var builder = new TiffBuilder { Data = new byte[4096] };
        var d = builder.AddDirectory();
        switch (defect)
        {
            case "tiles of no width":
                d.Image(4, 4).Remove(273).Remove(278).Remove(279).Add(322, TiffTagType.Short, 0).Add(323, TiffTagType.Short, 16)
                    .Add(324, TiffTagType.Long, builder.DataOffset).Add(325, TiffTagType.Long, 256);
                break;
            case "tiles not multiples of 16":
                d.Image(4, 4).Remove(273).Remove(278).Remove(279).Add(322, TiffTagType.Short, 17).Add(323, TiffTagType.Short, 16)
                    .Add(324, TiffTagType.Long, builder.DataOffset).Add(325, TiffTagType.Long, 272);
                break;
            case "planar configuration 3":
                d.Image(4, 4).Add(284, TiffTagType.Short, 3);
                break;
            case "planar with one sample":
                d.Image(4, 4).Add(284, TiffTagType.Short, 2);
                break;
            case "orientation 9":
                d.Image(4, 4).Add(274, TiffTagType.Short, 9);
                break;
            case "no width":
                d.Image(4, 4).Remove(256);
                break;
            case "no strip offsets":
                d.Image(4, 4).Remove(273);
                break;
            case "palette without colour map":
                d.Image(4, 4, photometric: 3);
                break;
            case "colour map too short":
                d.Image(4, 4, photometric: 3).Add(320, TiffTagType.Short, 1, 2, 3);
                break;
            case "a tag twice":
                d.Image(4, 4).Text(305, "a").Text(305, "b");
                break;
            case "tags out of order":
                d.Image(4, 4).Text(315, "a").Text(305, "b");
                break;
        }

        if (defect is not ("a tag twice" or "tags out of order"))
        {
            d.Sorted();
        }

        return builder.Build();
    }
}
