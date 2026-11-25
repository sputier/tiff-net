using Eleris.TiffNet.Tests.Fixtures;

namespace Eleris.TiffNet.Tests.Writing;

public class MetadataWritingTests
{
    private static TiffFile RoundTrip(TiffFile file)
    {
        using var stream = new MemoryStream();
        file.Save(stream);
        return TiffFile.Read(new MemoryStream(stream.ToArray()));
    }

    [Theory]
    [InlineData("siril_m42_grey16_astro.tif")]
    [InlineData("siril_m42_rgb32_astro.tif")]
    public void The_Astro_TIFF_header_and_ICC_profile_of_Siril_survive_byte_for_byte(string name)
    {
        using var file = TiffFile.Open(DomainFixtures.PathOf(name));

        using var back = RoundTrip(file);

        var before = file.Primary.Metadata;
        var after = back.Primary.Metadata;
        Assert.Equal(before[270]!.AsBytes().ToArray(), after[270]!.AsBytes().ToArray());
        Assert.Equal(before.IccProfile, after.IccProfile);
        Assert.Equal(before.Select(t => t.Code), after.Select(t => t.Code));   // every non-structural tag is kept
        Assert.All(before, t => Assert.Equal(t, after[t.Code]));
    }

    [Fact]
    public void Every_domain_fixture_keeps_its_metadata_except_the_tags_of_offsets()
    {
        foreach (var (name, _) in DomainFixtures.Files)
        {
            using var file = TiffFile.Open(DomainFixtures.PathOf(name));
            if (file.Any(i => i.UnsupportedReason is not null))
            {
                continue;
            }

            using var back = RoundTrip(file);
            for (int i = 0; i < file.Count; i++)
            {
                var kept = file[i].Metadata.Where(t => t.Type is not (TiffTagType.Ifd or TiffTagType.Ifd8)).ToList();
                Assert.True(kept.Select(t => t.Code).SequenceEqual(back[i].Metadata.Select(t => t.Code)), $"{name}, page {i}");
            }
        }
    }

    [Fact]
    public void Typed_and_generic_tags_are_written()
    {
        var image = TiffImage.Create<byte>(new byte[16], 4, 4);
        var m = image.Metadata;
        m.Description = "SIMPLE  =                    T";
        m.Software = "Urania";
        m.DateTime = new DateTime(2026, 11, 6, 1, 2, 3);
        m.Resolution = new TiffResolution(300, 150, TiffResolutionUnit.Inch);
        m.SampleRange = (0, 65535);
        m.IccProfile = [1, 2, 3, 4, 5];
        m.Set(TiffTag.FromNumbers<uint>(65000, [7u, 8u, 9u]));
        m.Set(TiffTag.FromString(65001, "private"));

        using var back = RoundTrip(new TiffFile().Add(image));

        var r = back.Primary.Metadata;
        Assert.Equal(m.Description, r.Description);
        Assert.Equal("Urania", r.Software);
        Assert.Equal(m.DateTime, r.DateTime);
        Assert.Equal(m.Resolution, r.Resolution);
        Assert.Equal(m.SampleRange, r.SampleRange);
        Assert.Equal(m.IccProfile, r.IccProfile);
        Assert.Equal(new uint[] { 7, 8, 9 }, r[65000]!.AsNumbers<uint>());
        Assert.Equal("private", r[65001]!.AsString());
    }

    [Fact]
    public void Tags_that_hold_offsets_are_dropped()
    {
        // A read file with a private tag and an EXIF sub-directory: the offset of the sub-directory would point nowhere.
        var builder = new TiffBuilder { Data = new byte[12] };
        builder.AddDirectory().Image(4, 3).Add(65010, TiffTagType.Long, 1234).Add(34665, TiffTagType.Long, 0).Sorted();
        using var file = TiffFile.Read(new MemoryStream(builder.Build()));

        using var back = RoundTrip(file);

        Assert.Equal(new uint[] { 1234 }, back.Primary.Metadata[65010]!.AsNumbers<uint>());
        Assert.Null(back.Primary.Metadata[34665]);
        Assert.Throws<ArgumentException>(() => file.Primary.Metadata.Set(TiffTag.FromFile(65011, TiffTagType.Ifd, [8, 0, 0, 0], littleEndian: true, fileOffset: 8)));
    }
}
