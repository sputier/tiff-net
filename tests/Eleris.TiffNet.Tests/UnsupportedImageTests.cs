using Eleris.TiffNet.Fuzz;
using Eleris.TiffNet.Tests.Fixtures;

namespace Eleris.TiffNet.Tests;

// The contract of an image the library cannot decode (guide: "Images the library cannot decode"): what describes it never throws,
// whatever the file holds; what delivers its pixels throws, with the reason.
public class UnsupportedImageTests
{
    private const int Width = UnsupportedFixtures.Width, Height = UnsupportedFixtures.Height;

    /// <summary>What each fixture must read as: compression code, photometric code, bits of the first sample, tiles, big endian, BigTIFF.</summary>
    private sealed record Expected(int Compression, int Photometric, int Bits, bool Tiles = false, bool BigEndian = false, bool BigTiff = false);

    private static readonly Dictionary<string, Expected> Table = new()
    {
        ["compression_34713.tif"] = new(34713, 1, 8),
        ["compression_34713_big_endian_bigtiff.tif"] = new(34713, 1, 8, BigEndian: true, BigTiff: true),
        ["photometric_32803_cfa.tif"] = new(1, 32803, 16),
        ["photometric_32803_cfa_12bit.tif"] = new(1, 32803, 12),
        ["photometric_32803_cfa_14bit.tif"] = new(1, 32803, 14),
        ["photometric_34892_linear_raw.tif"] = new(1, 34892, 16),
        ["compression_34713_tiles.tif"] = new(34713, 1, 8, Tiles: true),
        ["two_samples_12_and_16_bits.tif"] = new(1, 1, 12),
        ["cfa_with_sub_image.tif"] = new(1, 32803, 16),
    };

    private static readonly TiffReadOptions StrictOptions = TiffReadOptions.Default with { Strict = true };

    public static TheoryData<string, bool> Cases()
    {
        var data = new TheoryData<string, bool>();
        foreach (string name in UnsupportedFixtures.All.Keys)
        {
            data.Add(name, false);
            data.Add(name, true);
        }

        return data;
    }

    private static TiffFile Open(string name, bool strict) =>
        TiffFile.Read(UnsupportedFixtures.All[name], strict ? StrictOptions : TiffReadOptions.Default);

    private static bool HasCfa(string name) => name.Contains("32803", StringComparison.Ordinal) || name == "cfa_with_sub_image.tif";

    [Fact]
    public void The_table_of_expectations_covers_every_fixture()
    {
        Assert.Equal(UnsupportedFixtures.All.Keys.Order(StringComparer.Ordinal), Table.Keys.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void The_committed_fixtures_are_what_the_builder_writes()
    {
        string folder = FixtureFolder();
        if (Environment.GetEnvironmentVariable("TIFFNET_UPDATE_FIXTURES") == "1")
        {
            Directory.CreateDirectory(folder);
            foreach (var (name, bytes) in UnsupportedFixtures.All)
            {
                File.WriteAllBytes(Path.Combine(folder, name), bytes);
            }
        }

        Assert.Equal(UnsupportedFixtures.All.Keys, Directory.GetFiles(folder, "*.tif").Select(Path.GetFileName).Order(StringComparer.Ordinal));
        foreach (var (name, bytes) in UnsupportedFixtures.All)
        {
            Assert.True(bytes.AsSpan().SequenceEqual(File.ReadAllBytes(Path.Combine(folder, name))), name);
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void A_fixture_opens_with_the_values_of_the_file(string name, bool strict)
    {
        var expected = Table[name];
        using var file = Open(name, strict);

        var storage = file.Primary.Storage!;
        Assert.Equal(expected.Compression, storage.RawCompressionCode);
        Assert.Equal(expected.Photometric, storage.RawPhotometricCode);
        Assert.Equal(expected.Bits, storage.BitsPerSample);
        Assert.Equal(expected.Tiles ? TiffLayout.Tiles : TiffLayout.Strips, storage.Layout);
        Assert.Equal(expected.BigEndian, storage.BigEndian);
        Assert.Equal(expected.BigTiff, storage.BigTiff);
        Assert.Equal(expected.Compression == 34713 ? TiffCompression.Unknown : TiffCompression.None, storage.Compression);
        Assert.Equal(expected.Photometric is 32803 or 34892 ? TiffPhotometric.Unknown : TiffPhotometric.MinIsBlack, storage.Photometric);
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void The_unnamed_codes_read_as_Unknown_with_the_code_in_the_raw_property(string name, bool strict)
    {
        using var file = Open(name, strict);
        var storage = file.Primary.Storage!;

        if (name.StartsWith("compression_34713", StringComparison.Ordinal))
        {
            Assert.Equal(TiffCompression.Unknown, storage.Compression);
            Assert.Equal(34713, storage.RawCompressionCode);
        }

        if (HasCfa(name))
        {
            Assert.Equal(TiffPhotometric.Unknown, storage.Photometric);
            Assert.Equal(32803, storage.RawPhotometricCode);
        }

        // The code is part of the record: it takes part in equality and survives a copy.
        Assert.Equal(storage, storage with { });
        Assert.NotEqual(storage, storage with { RawCompressionCode = storage.RawCompressionCode + 1 });
    }

    // One test per row of the table of the guide.

    [Theory]
    [MemberData(nameof(Cases))]
    public void Width_and_Height_are_the_stored_size(string name, bool strict)
    {
        using var file = Open(name, strict);

        Assert.Equal(Width, file.Primary.Width);
        Assert.Equal(Height, file.Primary.Height);
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void UnsupportedReason_says_why_and_IsSupported_follows_it(string name, bool strict)
    {
        using var file = Open(name, strict);
        var image = file.Primary;

        Assert.NotNull(image.UnsupportedReason);
        Assert.False(image.IsSupported);
        if (name == "two_samples_12_and_16_bits.tif")
        {
            Assert.Contains("different numbers of bits", image.UnsupportedReason, StringComparison.Ordinal);
        }
        else if (Table[name].Compression == 34713)
        {
            Assert.Contains("compression 34713 is not supported", image.UnsupportedReason, StringComparison.Ordinal);
        }
        else if (Table[name].Photometric == 32803)
        {
            Assert.Contains("CFA", image.UnsupportedReason, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void IsSupported_is_true_for_an_image_the_library_decodes()
    {
        var builder = new TiffBuilder { Data = new byte[12] };
        builder.AddDirectory().Image(4, 3);

        using var file = TiffFile.Read(builder.Build());

        Assert.True(file.Primary.IsSupported);
        Assert.Null(file.Primary.UnsupportedReason);
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Storage_describes_the_layout_of_the_file(string name, bool strict)
    {
        using var file = Open(name, strict);
        var storage = file.Primary.Storage!;

        if (Table[name].Tiles)
        {
            Assert.Equal((UnsupportedFixtures.TileSize, UnsupportedFixtures.TileSize, 0), (storage.TileWidth, storage.TileHeight, storage.RowsPerStrip));
        }
        else
        {
            Assert.Equal((0, 0, Height), (storage.TileWidth, storage.TileHeight, storage.RowsPerStrip));
        }

        Assert.Equal(1, storage.Orientation);
        Assert.Equal(TiffPixelOrder.Interleaved, storage.PlanarConfiguration);
        Assert.Equal(TiffPredictor.None, storage.Predictor);
    }

    [Theory]
    [InlineData(12)]
    [InlineData(14)]
    public void A_depth_of_12_or_14_bits_is_a_stored_value_not_an_error(int bits)
    {
        using var file = Open($"photometric_32803_cfa_{bits}bit.tif", strict: true);

        Assert.Equal(bits, file.Primary.Storage!.BitsPerSample);
        Assert.DoesNotContain("bits", file.Primary.UnsupportedReason, StringComparison.Ordinal);
        Assert.Contains("CFA", file.Primary.UnsupportedReason, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Metadata_holds_the_CFA_and_raw_tags(string name, bool strict)
    {
        using var file = Open(name, strict);
        var metadata = file.Primary.Metadata;
        if (!UnsupportedFixtures.CfaNames.Contains(name) && name != "cfa_with_sub_image.tif")
        {
            Assert.Null(metadata[33421]);
            return;
        }

        int bits = Table[name].Bits;
        Assert.Equal([2, 2], metadata[33421]!.AsNumbers<int>());
        Assert.Equal([0, 1, 1, 2], metadata[33422]!.AsNumbers<int>());
        Assert.Equal([0, 0, Height, Width], metadata[50829]!.AsNumbers<int>());
        Assert.Equal([1, 1], metadata[50719]!.AsNumbers<int>());
        Assert.Equal([Width - 2, Height - 2], metadata[50720]!.AsNumbers<int>());
        Assert.Equal([256], metadata[50714]!.AsNumbers<int>());
        Assert.Equal([(1 << bits) - 1], metadata[50717]!.AsNumbers<int>());
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Channels_and_ExtraChannels_describe_the_samples_of_the_file(string name, bool strict)
    {
        using var file = Open(name, strict);
        var image = file.Primary;

        Assert.Equal(name == "two_samples_12_and_16_bits.tif" ? 2 : 1, image.Channels);
        Assert.Equal(image.Channels - 1, image.ExtraChannels.Count);
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void SubImages_and_IsReducedResolution_are_readable(string name, bool strict)
    {
        using var file = Open(name, strict);
        var image = file.Primary;

        Assert.False(image.IsReducedResolution);
        if (name != "cfa_with_sub_image.tif")
        {
            Assert.Empty(image.SubImages);
            return;
        }

        var sub = Assert.Single(image.SubImages);
        Assert.True(sub.IsReducedResolution);
        Assert.Equal((Width / 2, Height / 2), (sub.Width, sub.Height));
        Assert.Equal(34713, sub.Storage!.RawCompressionCode);
        Assert.Equal(TiffCompression.Unknown, sub.Storage.Compression);
        Assert.False(sub.IsSupported);
        Assert.Single(file);
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Blocks_are_the_stored_bytes(string name, bool strict)
    {
        using var file = Open(name, strict);
        var image = file.Primary;

        var block = Assert.Single(image.Blocks);
        byte[] bytes = new byte[block.ByteCount];
        Assert.Equal(bytes.Length, image.ReadBlock(0, bytes));
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void SampleType_and_ColorModel_throw_with_the_reason(string name, bool strict)
    {
        using var file = Open(name, strict);
        var image = file.Primary;

        var type = Assert.Throws<NotSupportedException>(() => image.SampleType);
        var model = Assert.Throws<NotSupportedException>(() => image.ColorModel);
        Assert.Contains(image.UnsupportedReason!, type.Message, StringComparison.Ordinal);
        Assert.Contains(image.UnsupportedReason!, model.Message, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task Every_pixel_read_throws_with_the_reason_and_nothing_else(string name, bool strict)
    {
        using var file = Open(name, strict);
        var image = file.Primary;
        string reason = image.UnsupportedReason!;

        void Check(Action read) => Assert.Contains(reason, Assert.Throws<NotSupportedException>(read).Message, StringComparison.Ordinal);
        async Task CheckAsync(Func<Task> read) => Assert.Contains(reason, (await Assert.ThrowsAsync<NotSupportedException>(read)).Message, StringComparison.Ordinal);

        Check(() => image.ReadPixels<double>());
        Check(() => image.ReadPixels<byte>(TiffPixelScale.Normalized, TiffPixelOrder.Planar));
        Check(() => image.ReadRawPixels<ushort>());
        Check(() => image.ReadPixels(new double[24]));
        Check(() => image.ReadPixels(new double[24].AsMemory()));
        Check(() =>
        {
            Span<double> span = new double[24];
            image.ReadPixels(span);
        });
        Check(() => image.ReadRegion<double>(0, 0, 2, 2));
        Check(() => image.ReadRawRegion<double>(0, 0, 2, 2));
        Check(() => image.ReadChannel<double>(0));
        Check(() => image.ReadIndices());
        await CheckAsync(() => image.ReadPixelsAsync<double>());
        await CheckAsync(() => image.ReadRegionAsync<double>(0, 0, 2, 2));
        await CheckAsync(() => image.ReadChannelAsync<double>(0));
    }

    [Fact]
    public void The_exception_of_a_pixel_read_names_the_file()
    {
        string path = Path.Combine(Path.GetTempPath(), $"unsupported-{Guid.NewGuid():N}.tif");
        File.WriteAllBytes(path, UnsupportedFixtures.All["compression_34713.tif"]);
        try
        {
            using var file = TiffFile.Open(path);
            var ex = Assert.Throws<NotSupportedException>(() => file.Primary.ReadPixels<byte>());
            Assert.Equal($"{Path.GetFileName(path)}: {file.Primary.UnsupportedReason}", ex.Message);
        }
        finally
        {
            File.Delete(path);
        }
    }

    // The numeric codes.

    [Fact]
    public void Unknown_is_a_value_no_code_of_a_file_can_have()
    {
        Assert.Equal(-1, (int)TiffCompression.Unknown);
        Assert.Equal(-1, (int)TiffPhotometric.Unknown);
    }

    [Fact]
    public void The_raw_compression_code_of_every_named_compression_is_its_numeric_value()
    {
        var named = Enum.GetValues<TiffCompression>().Where(c => c != TiffCompression.Unknown).ToArray();
        Assert.Equal(5, named.Length);
        foreach (var compression in named)
        {
            foreach (bool strict in new[] { false, true })
            {
                var builder = new TiffBuilder { Data = new byte[Width * Height] };
                builder.AddDirectory().Image(Width, Height).Remove(259).Add(259, TiffTagType.Short, (int)compression).Sorted();

                using var file = TiffFile.Read(builder.Build(), strict ? StrictOptions : TiffReadOptions.Default);

                Assert.Equal(compression, file.Primary.Storage!.Compression);
                Assert.Equal((int)compression, file.Primary.Storage.RawCompressionCode);
            }
        }
    }

    [Fact]
    public void The_raw_photometric_code_of_every_named_photometric_is_its_numeric_value()
    {
        var named = Enum.GetValues<TiffPhotometric>().Where(p => p != TiffPhotometric.Unknown).ToArray();
        Assert.Equal(5, named.Length);
        foreach (var photometric in named)
        {
            foreach (bool strict in new[] { false, true })
            {
                // Three samples fit every interpretation; the compression keeps the palette and YCbCr files from being decoded.
                var builder = new TiffBuilder { Data = new byte[Width * Height * 3] };
                builder.AddDirectory().Image(Width, Height, 8, 3, (ushort)photometric).Remove(259).Add(259, TiffTagType.Short, 34713).Sorted();

                using var file = TiffFile.Read(builder.Build(), strict ? StrictOptions : TiffReadOptions.Default);

                Assert.Equal(photometric, file.Primary.Storage!.Photometric);
                Assert.Equal((int)photometric, file.Primary.Storage.RawPhotometricCode);
            }
        }
    }

    [Fact]
    public void The_first_code_of_Deflate_reads_as_Deflate_and_keeps_its_code()
    {
        var builder = new TiffBuilder { Data = new byte[Width * Height] };
        builder.AddDirectory().Image(Width, Height).Remove(259).Add(259, TiffTagType.Short, 32946).Sorted();

        using var file = TiffFile.Read(builder.Build());

        Assert.Equal(TiffCompression.Deflate, file.Primary.Storage!.Compression);
        Assert.Equal(32946, file.Primary.Storage.RawCompressionCode);
    }

    [Fact]
    public void A_file_written_by_the_library_holds_the_code_of_its_compression()
    {
        foreach (var compression in new[] { TiffCompression.None, TiffCompression.Lzw, TiffCompression.Deflate, TiffCompression.PackBits })
        {
            var pages = new TiffFile { TiffImage.Create<ushort>(new ushort[Width * Height], Width, Height) };
            using var output = new MemoryStream();
            pages.Save(output, TiffWriteOptions.Default with { Compression = compression });

            using var back = TiffFile.Read(output.ToArray());

            Assert.Equal((int)compression, back.Primary.Storage!.RawCompressionCode);
            Assert.Equal(1, back.Primary.Storage.RawPhotometricCode);
        }
    }

    // Writing.

    [Fact]
    public void Writing_with_the_Unknown_compression_is_refused_and_names_the_property()
    {
        var ex = Assert.Throws<ArgumentException>(() => TiffWriteOptions.Default with { Compression = TiffCompression.Unknown });

        Assert.Equal("Compression", ex.ParamName);
        Assert.Contains("Compression", ex.Message, StringComparison.Ordinal);
        Assert.IsNotType<ArgumentOutOfRangeException>(ex);
    }

    [Fact]
    public void A_refused_compression_never_reaches_a_save_so_no_file_is_created_or_changed()
    {
        string folder = Path.Combine(Path.GetTempPath(), $"unsupported-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        try
        {
            string created = Path.Combine(folder, "created.tif");
            string existing = Path.Combine(folder, "existing.tif");
            byte[] before = UnsupportedFixtures.All["compression_34713.tif"];
            File.WriteAllBytes(existing, before);
            var pages = new TiffFile { TiffImage.Create<byte>(new byte[Width * Height], Width, Height) };

            // The options cannot be built: the exception comes before any call to Save, so nothing can be created or changed.
            Assert.Throws<ArgumentException>(() => TiffWriteOptions.Default with { Compression = TiffCompression.Unknown });
            pages.Save(existing, TiffWriteOptions.Default);
            Assert.NotEqual(before, File.ReadAllBytes(existing));
            File.WriteAllBytes(existing, before);

            Assert.False(File.Exists(created));
            Assert.Equal(before, File.ReadAllBytes(existing));
            Assert.Equal(["existing.tif"], Directory.GetFiles(folder).Select(Path.GetFileName));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void Options_made_from_an_unknown_storage_write_Deflate()
    {
        using var file = Open("compression_34713.tif", strict: false);
        var options = TiffWriteOptions.From(file.Primary.Storage!);

        Assert.Equal(TiffCompression.Deflate, options.Compression);
    }

    // The fuzz campaign.

    [Theory]
    [InlineData("open")]
    [InlineData("save")]
    [InlineData("read")]
    public void The_fuzz_targets_run_on_every_fixture(string target)
    {
        foreach (var (name, bytes) in UnsupportedFixtures.All)
        {
            Targets.All[target](bytes);
            Assert.True(bytes.Length > 8, name);
        }
    }

    [Fact]
    public void The_fixtures_are_seeds_of_the_fuzz_campaign()
    {
        var corpus = Corpus.Load([FixtureFolder()]);

        Assert.Equal(UnsupportedFixtures.All.Count, corpus.Count);
    }

    private static string FixtureFolder()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Eleris.TiffNet.slnx")))
            {
                return Path.Combine(dir.FullName, "tests", "Fixtures", "unsupported");
            }
        }

        throw new DirectoryNotFoundException("Eleris.TiffNet.slnx was not found above " + AppContext.BaseDirectory);
    }
}
