using Eleris.TiffNet.Tests.Fixtures;

namespace Eleris.TiffNet.Tests;

public class MetadataTests
{
    private static TiffMetadata Of(string fixture)
    {
        using var file = TiffFile.Open(DomainFixtures.PathOf(fixture));
        return file.Primary.Metadata;
    }

    private static TiffMetadata Of(TiffBuilder builder, TiffReadOptions? options = null)
    {
        using var file = TiffFile.Read(new MemoryStream(builder.Build()), options);
        return file.Primary.Metadata;
    }

    [Fact]
    public void The_common_tags_are_typed()
    {
        var m = Of("metadata.tif");
        var page = DomainFixtures.Page("metadata.tif");

        Assert.Equal("OBJECT  = 'M42'", m.Description);
        Assert.Equal("make_domain_fixtures", m.Software);
        Assert.Equal(new DateTime(2026, 11, 5, 12, 0, 0), m.DateTime);
        Assert.Equal("An astronomer", m.Artist);
        Assert.Equal("CC0", m.Copyright);
        Assert.Equal(new TiffResolution(300, 300, TiffResolutionUnit.Inch), m.Resolution);
        Assert.Equal((0d, 65535d), m.SampleRange);
        Assert.Equal(page.GetProperty("iccBytes").GetInt32(), m.IccProfile!.Length);
        Assert.StartsWith("<x:xmpmeta", System.Text.Encoding.ASCII.GetString(m.Xmp!), StringComparison.Ordinal);
        Assert.Equal("0", m[42113]!.AsString());                          // GDAL_NODATA, a private tag
        Assert.Equal([1, 2, 3, 4], m[65000]!.AsNumbers<byte>());
    }

    [Fact]
    public void Structure_tags_are_not_metadata()
    {
        var m = Of("metadata.tif");

        foreach (int structural in new[] { 256, 257, 258, 259, 262, 273, 277, 278, 279, 284 })
        {
            Assert.Null(m[structural]);
        }

        Assert.All(m, tag => Assert.DoesNotContain(tag.Code, TiffMetadata.Excluded));
        Assert.Throws<ArgumentException>(() => m.Set(TiffTag.FromNumbers<ushort>(256, [10])));

        // A tag of the Ifd type holds an offset into the file: it is not kept.
        var builder = new TiffBuilder { Data = new byte[12] };
        builder.AddDirectory().Image(4, 3).Add(60000, TiffTagType.Ifd, 8);
        Assert.Null(Of(builder)[60000]);
    }

    [Theory]
    [InlineData("siril_m42_grey16_astro.tif")]
    [InlineData("siril_m42_rgb32_astro.tif")]
    public void The_Astro_TIFF_header_and_the_ICC_profile_of_Siril_are_read(string name)
    {
        var m = Of(name);
        var page = DomainFixtures.Page(name);

        Assert.Equal(page.GetProperty("description").GetString(), m.Description);
        Assert.Contains("OBJECT  = 'M42", m.Description, StringComparison.Ordinal);
        Assert.Equal(page.GetProperty("iccBytes").GetInt32(), m.IccProfile!.Length);
        Assert.Equal("acsp", System.Text.Encoding.ASCII.GetString(m.IccProfile, 36, 4));   // the signature of an ICC profile
        Assert.StartsWith("siril", m.Software, StringComparison.Ordinal);
        Assert.NotNull(m.DateTime);
    }

    [Fact]
    public void UTF8_text_is_read_and_other_bytes_as_Latin1()
    {
        Assert.Equal("Nébuleuse d'Orion — M42 ✨", Of("description_utf8.tif").Description);

        var builder = new TiffBuilder { Data = new byte[12] };
        builder.AddDirectory().Image(4, 3).Raw(270, TiffTagType.Ascii, [(byte)'C', 0xE9, (byte)'t', (byte)'e', 0]);   // Latin-1 é
        Assert.Equal("Céte", Of(builder).Description);
    }

    [Fact]
    public void A_tag_with_several_strings_gives_each()
    {
        var builder = new TiffBuilder { Data = new byte[12] };
        builder.AddDirectory().Image(4, 3).Raw(269, TiffTagType.Ascii, "first\0second\0third\0"u8.ToArray());

        var tag = Of(builder)[269]!;

        Assert.Equal(["first", "second", "third"], tag.AsStrings());
        Assert.Equal("first", tag.AsString());
    }

    [Fact]
    public void Values_of_a_big_endian_file_are_read_in_the_right_order()
    {
        var builder = new TiffBuilder(littleEndian: false) { Data = new byte[12] };
        builder.AddDirectory().Image(4, 3)
            .Add(282, TiffTagType.Rational, 300, 1)
            .Add(283, TiffTagType.Rational, 600, 2)
            .Add(296, TiffTagType.Short, 3)
            .Add(65001, TiffTagType.SLong, -123456)
            .Raw(33723, (ushort)TiffTagType.Long, 2, [0x1C, 0x02, 0x00, 0x02, 0x00, 0x04, 0x41, 0x42]);   // IPTC declared as LONG

        var m = Of(builder);

        Assert.Equal(new TiffResolution(300, 300, TiffResolutionUnit.Centimeter), m.Resolution);
        Assert.Equal([-123456], m[65001]!.AsNumbers<int>());
        Assert.Equal(new byte[] { 0x1C, 0x02, 0x00, 0x02, 0x00, 0x04, 0x41, 0x42 }, m.Iptc);   // bytes untouched
    }

    [Fact]
    public void EXIF_and_GPS_sub_directories_are_read_as_tag_lists()
    {
        var builder = new TiffBuilder { Data = new byte[12] };
        var image = builder.AddDirectory().Image(4, 3);
        var exif = builder.AddDirectory().Add(33434, TiffTagType.Rational, 1, 250).Add(36867, TiffTagType.Ascii, (long)'2');
        builder.AddDirectory().Add(1, TiffTagType.Ascii, (long)'N', 0);
        exif.NextOverride = 0;
        image.NextOverride = 0;
        // Placeholders first, so that the size of the first directory, and the offsets after it, do not change.
        image.Add(34665, TiffTagType.Long, 0).Add(34853, TiffTagType.Long, 0);
        long exifOffset = builder.OffsetOf(1), gpsOffset = builder.OffsetOf(2);
        image.Remove(34665).Remove(34853).Add(34665, TiffTagType.Long, exifOffset).Add(34853, TiffTagType.Long, gpsOffset);

        using var file = TiffFile.Read(new MemoryStream(builder.Build()));

        Assert.Single(file);   // the sub-directories are not pages
        var m = file.Primary.Metadata;
        Assert.Equal([33434, 36867], m.Exif.Select(t => t.Code));
        Assert.Equal([1d / 250], m.Exif[0].AsNumbers<double>());
        Assert.Equal("N", m.Gps.Single().AsString());
        Assert.Null(m[34665]);
    }

    [Fact]
    public void A_tag_whose_values_are_outside_the_file_is_dropped_unless_Strict()
    {
        var builder = new TiffBuilder { Data = new byte[12] };
        builder.AddDirectory().Image(4, 3).Pointing(270, TiffTagType.Ascii, 1000, 1_000_000).Sorted();

        Assert.Null(Of(builder).Description);
        Assert.Throws<TiffException>(() => Of(builder, TiffReadOptions.Default with { Strict = true }));
    }

    [Fact]
    public void Metadata_can_be_edited()
    {
        var m = new TiffMetadata
        {
            Description = "SIMPLE  = T",
            Software = "Urania",
            DateTime = new DateTime(2026, 11, 6, 22, 30, 15),
            Resolution = new TiffResolution(72.5, 72, TiffResolutionUnit.Centimeter),
            SampleRange = (0, 1),
            IccProfile = [1, 2, 3],
        };
        m.Set(TiffTag.FromNumbers<double>(65100, [1.5, -2]));

        Assert.Equal("SIMPLE  = T", m.Description);
        Assert.Equal("2026:11:06 22:30:15", m[306]!.AsString());
        Assert.Equal(new TiffResolution(72.5, 72, TiffResolutionUnit.Centimeter), m.Resolution);
        Assert.Equal((0d, 1d), m.SampleRange);
        Assert.Equal([1.5, -2], m[65100]!.AsNumbers<double>());
        Assert.Equal([270, 282, 283, 296, 305, 306, 340, 341, 34675, 65100], m.Select(t => t.Code));

        m.Description = null;
        m.Resolution = null;
        Assert.Null(m[270]);
        Assert.Null(m[282]);
        Assert.True(m.Remove(65100));
        Assert.False(m.Remove(65100));
    }

    [Fact]
    public void Tags_convert_their_values_safely()
    {
        var shorts = TiffTag.FromNumbers<ushort>(300, [1, 65535]);

        Assert.Equal((TiffTagType.Short, 2), (shorts.Type, shorts.Count));
        Assert.Equal([1, 65535], shorts.AsNumbers<int>());
        Assert.Throws<InvalidOperationException>(() => shorts.AsNumbers<byte>());          // 65535 does not fit
        Assert.Throws<InvalidOperationException>(() => shorts.AsString());
        Assert.Throws<InvalidOperationException>(() => TiffTag.FromNumbers<double>(301, [0.5]).AsNumbers<int>());
        Assert.Throws<InvalidOperationException>(() => TiffTag.FromString(302, "x").AsNumbers<int>());
        Assert.Throws<ArgumentException>(() => TiffTag.FromBytes(303, [1, 2], TiffTagType.Short));
        Assert.Throws<ArgumentOutOfRangeException>(() => TiffTag.FromString(70000, "x"));
        Assert.Equal(TiffTag.FromString(304, "é"), TiffTag.FromString(304, "é"));
        Assert.NotEqual(TiffTag.FromString(304, "é"), TiffTag.FromString(304, "e"));
        Assert.Equal("é", TiffTag.FromString(304, "é").AsString());
    }

    [Fact]
    public void Every_domain_fixture_gives_its_tags()
    {
        foreach (var (name, entry) in DomainFixtures.Files)
        {
            using var file = TiffFile.Open(DomainFixtures.PathOf(name));
            var page = entry.GetProperty("pages")[0];
            var codes = page.GetProperty("tagCodes").EnumerateArray().Select(e => e.GetInt32())
                .Where(c => !TiffMetadata.Excluded.Contains(c)).ToArray();
            var read = file[0].Metadata.Select(t => t.Code).ToArray();
            Assert.True(codes.SequenceEqual(read), $"{name}: [{string.Join(",", read)}] instead of [{string.Join(",", codes)}]");
        }
    }
}
