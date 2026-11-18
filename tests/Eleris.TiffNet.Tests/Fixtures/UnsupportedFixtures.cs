namespace Eleris.TiffNet.Tests.Fixtures;

/// <summary>
/// The images the library cannot decode, built byte by byte (tests/Fixtures/unsupported holds them as files, which the fuzz
/// campaign takes as seeds; a test checks that the files are what this class builds).
/// </summary>
internal static class UnsupportedFixtures
{
    public const int Width = 6, Height = 4, TileSize = 16;

    private const ushort CfaRepeatPatternDim = 33421, CfaPattern = 33422, ActiveArea = 50829, DefaultCropOrigin = 50719,
        DefaultCropSize = 50720, BlackLevel = 50714, WhiteLevel = 50717;

    /// <summary>The fixtures by file name.</summary>
    public static IReadOnlyDictionary<string, byte[]> All { get; } = new SortedDictionary<string, byte[]>(StringComparer.Ordinal)
    {
        ["compression_34713.tif"] = Grey(compression: 34713),
        ["compression_34713_big_endian_bigtiff.tif"] = Grey(compression: 34713, bigTiff: true, littleEndian: false),
        ["photometric_32803_cfa.tif"] = Cfa(16),
        ["photometric_32803_cfa_12bit.tif"] = Cfa(12),
        ["photometric_32803_cfa_14bit.tif"] = Cfa(14),
        ["photometric_34892_linear_raw.tif"] = Grey(photometric: 34892, bits: 16),
        ["compression_34713_tiles.tif"] = Tiled(),
        ["two_samples_12_and_16_bits.tif"] = TwoSamples(),
        ["cfa_with_sub_image.tif"] = WithSubImage(),
    }.AsReadOnly();

    /// <summary>The names of the fixtures that hold a CFA description (tags 33421 and 33422).</summary>
    public static IReadOnlyList<string> CfaNames { get; } = [.. All.Keys.Where(n => n.Contains("_cfa", StringComparison.Ordinal))];

    /// <summary>The 8-bit pixels of the fixtures, uncompressed in the file (what a decoder would find in the strip).</summary>
    private static byte[] Samples(int bytesPerPixel) => [.. Enumerable.Range(0, Width * Height * bytesPerPixel).Select(i => (byte)(i * 7))];

    private static byte[] Grey(int compression = 1, int photometric = 1, int bits = 8, bool bigTiff = false, bool littleEndian = true)
    {
        var builder = new TiffBuilder(bigTiff, littleEndian) { Data = Samples(Math.Max(1, bits / 8)) };
        builder.AddDirectory().Image(Width, Height, (ushort)bits, photometric: (ushort)photometric)
            .Remove(259).Add(259, TiffTagType.Short, compression).Sorted();
        return builder.Build();
    }

    private static byte[] Cfa(int bits)
    {
        var builder = new TiffBuilder { Data = Samples(2) };
        var directory = builder.AddDirectory().Image(Width, Height, (ushort)bits, photometric: 32803)
            .Remove(279).Add(279, TiffTagType.Long, Width * Height * 2);
        CfaTags(directory, bits);
        return builder.Build();
    }

    private static void CfaTags(TiffBuilder.Directory directory, int bits) => directory
        .Add(CfaRepeatPatternDim, TiffTagType.Short, 2, 2)
        .Add(CfaPattern, TiffTagType.Byte, 0, 1, 1, 2)
        .Add(ActiveArea, TiffTagType.Long, 0, 0, Height, Width)
        .Add(DefaultCropOrigin, TiffTagType.Long, 1, 1)
        .Add(DefaultCropSize, TiffTagType.Long, Width - 2, Height - 2)
        .Add(BlackLevel, TiffTagType.Short, 256)
        .Add(WhiteLevel, TiffTagType.Short, (1 << bits) - 1)
        .Sorted();

    private static byte[] Tiled()
    {
        var builder = new TiffBuilder { Data = new byte[TileSize * TileSize] };
        builder.AddDirectory().Image(Width, Height).Remove(273).Remove(278).Remove(279).Remove(259)
            .Add(259, TiffTagType.Short, 34713)
            .Add(322, TiffTagType.Long, TileSize).Add(323, TiffTagType.Long, TileSize)
            .Add(324, TiffTagType.Long, builder.DataOffset).Add(325, TiffTagType.Long, TileSize * TileSize)
            .Sorted();
        return builder.Build();
    }

    private static byte[] TwoSamples()
    {
        var builder = new TiffBuilder { Data = new byte[Width * Height * 4] };
        builder.AddDirectory().Image(Width, Height, 16, samples: 2).Remove(258).Add(258, TiffTagType.Short, 12, 16)
            .Remove(279).Add(279, TiffTagType.Long, Width * Height * 4)
            .Add(338, TiffTagType.Short, 0).Sorted();
        return builder.Build();
    }

    private static byte[] WithSubImage()
    {
        var builder = new TiffBuilder { Data = Samples(2) };
        var main = builder.AddDirectory().Image(Width, Height, 16, photometric: 32803).Remove(279).Add(279, TiffTagType.Long, Width * Height * 2);
        CfaTags(main, 16);
        main.Add(330, TiffTagType.Long, 0);
        builder.AddDirectory().Image(Width / 2, Height / 2, 16, photometric: 32803).Remove(279).Add(279, TiffTagType.Long, Width * Height / 2)
            .Add(254, TiffTagType.Long, 1).Remove(259).Add(259, TiffTagType.Short, 34713).Sorted();
        main.Sorted();
        long at = builder.OffsetOf(1);
        main.Remove(330).Add(330, TiffTagType.Long, at).Sorted();
        main.NextOverride = 0;
        return builder.Build();
    }
}
