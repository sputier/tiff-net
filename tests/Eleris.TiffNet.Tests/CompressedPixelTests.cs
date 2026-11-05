using System.IO.Compression;
using Eleris.TiffNet.Tests.Codecs;
using Eleris.TiffNet.Tests.Fixtures;
using Eleris.TiffNet.Tests.Interop;

namespace Eleris.TiffNet.Tests;

/// <summary>Images whose strips or tiles are compressed with PackBits, LZW or Deflate.</summary>
public class CompressedPixelTests
{
    private static byte[] Zlib(byte[] data)
    {
        using var output = new MemoryStream();
        using (var zlib = new ZLibStream(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            zlib.Write(data);
        }

        return output.ToArray();
    }

    private static byte[] PackBitsLiterals(byte[] data) =>
        [.. data.Chunk(128).SelectMany(chunk => (byte[])[(byte)(chunk.Length - 1), .. chunk])];

    /// <summary>A grey 8-bit image of one strip holding <paramref name="strip"/>, compressed with <paramref name="compression"/>.</summary>
    private static TiffBuilder OneStrip(int width, int height, ushort compression, byte[] strip)
    {
        var builder = new TiffBuilder { Data = strip };
        builder.AddDirectory().Image(width, height).Remove(259).Add(259, TiffTagType.Short, compression)
            .Remove(279).Add(279, TiffTagType.Long, strip.Length).Sorted();
        return builder;
    }

    private static TiffFile Open(TiffBuilder builder, TiffReadOptions? options = null) => TiffFile.Read(new MemoryStream(builder.Build()), options);

    public static TheoryData<ushort> Codes() => [5, 8, 32946, 32773];

    private static byte[] Encode(ushort compression, byte[] data) => compression switch
    {
        5 => LzwTests.Encode(data),
        32773 => PackBitsLiterals(data),
        _ => Zlib(data),
    };

    [Theory]
    [MemberData(nameof(Codes))]
    public void A_compressed_strip_gives_its_samples(ushort compression)
    {
        byte[] samples = LzwTests.Sample("random")[..(100 * 70)];
        using var file = Open(OneStrip(100, 70, compression, Encode(compression, samples)));

        Assert.Equal(samples, file.Primary.ReadRawPixels<byte>());
        Assert.Equal(compression == 32946 ? TiffCompression.Deflate : (TiffCompression)compression, file.Primary.Storage!.Compression);

        // A region decodes the strip up to its last row only, and gives the same samples.
        var region = file.Primary.ReadRegion<byte>(10, 5, 30, 20);
        for (int y = 0; y < 20; y++)
        {
            Assert.Equal(samples.AsSpan((5 + y) * 100 + 10, 30).ToArray(), region.AsSpan(y * 30, 30).ToArray());
        }
    }

    [Fact]
    public void Old_style_LZW_is_decoded()
    {
        byte[] samples = LzwTests.Sample("text")[..(50 * 40)];
        using var file = Open(OneStrip(50, 40, 5, LzwTests.Encode(samples, oldStyle: true)));

        Assert.Equal(samples, file.Primary.ReadRawPixels<byte>());
    }

    [Theory]
    [MemberData(nameof(Codes))]
    public void A_strip_that_decodes_short_is_a_damaged_file(ushort compression)
    {
        byte[] samples = LzwTests.Sample("random")[..(100 * 70)];
        using var file = Open(OneStrip(100, 71, compression, Encode(compression, samples)));   // one row more than the data

        Assert.Contains("decodes to", Assert.Throws<TiffException>(() => file.Primary.ReadPixels<byte>()).Message, StringComparison.Ordinal);
        Assert.Equal(100 * 70, file.Primary.ReadRegion<byte>(0, 0, 100, 70).Length);   // the rows that are there can be read
    }

    [Fact]
    public void Damaged_Deflate_data_is_a_TiffException()
    {
        byte[] compressed = Zlib(LzwTests.Sample("random")[..(100 * 70)]);
        compressed[10] ^= 0xFF;
        compressed[11] ^= 0x55;
        using var file = Open(OneStrip(100, 70, 8, compressed));

        Assert.Contains("cannot be decoded", Assert.Throws<TiffException>(() => file.Primary.ReadPixels<byte>()).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_undefined_LZW_code_is_a_TiffException()
    {
        using var file = Open(OneStrip(4, 4, 5, LzwTests.Pack(256, 65, 300)));

        Assert.IsType<TiffException>(Assert.ThrowsAny<Exception>(() => file.Primary.ReadPixels<byte>()));
    }

    [Theory]
    [MemberData(nameof(Codes))]
    public void Data_beyond_the_strip_is_ignored_unless_the_read_is_strict(ushort compression)
    {
        byte[] samples = LzwTests.Sample("random")[..(100 * 71)];
        var builder = OneStrip(100, 70, compression, Encode(compression, samples));   // one row more than the image

        using var lenient = Open(builder);
        Assert.Equal(samples[..(100 * 70)], lenient.Primary.ReadRawPixels<byte>());
        using var strict = Open(builder, TiffReadOptions.Default with { Strict = true });
        Assert.Contains("more data", Assert.Throws<TiffException>(() => strict.Primary.ReadRawPixels<byte>()).Message, StringComparison.Ordinal);
        Assert.Equal(100 * 10, strict.Primary.ReadRegion<byte>(0, 0, 100, 10).Length);   // a part of the strip is not checked
    }

    [Fact]
    public void Tiled_compressed_regions_are_the_same_samples_as_the_whole_image()
    {
        if (!Directory.Exists(InteropCorpus.Folder))
        {
            Assert.True(InteropCorpus.Skipped);
            return;
        }

        int checkedFiles = 0;
        foreach (var f in InteropCorpus.Files.Where(f => f.Tiled && f.Compression is "lzw" or "zlib" or "packbits").Take(60))
        {
            using var file = TiffFile.Open(Path.Combine(InteropCorpus.Folder, f.Name));
            var image = file.Primary;
            var whole = image.ReadPixels<double>();
            int c = image.Channels, x = image.Width / 3, y = image.Height / 4, w = image.Width - x - 1, h = image.Height - y - 2;
            var region = image.ReadRegion<double>(x, y, w, h);
            for (int row = 0; row < h; row++)
            {
                Assert.True(whole.AsSpan(((y + row) * image.Width + x) * c, w * c).SequenceEqual(region.AsSpan(row * w * c, w * c)), f.Name);
            }

            checkedFiles++;
        }

        Assert.True(checkedFiles > 50, $"only {checkedFiles} files");
    }

    [Theory]
    [InlineData(5)]
    [InlineData(8)]
    [InlineData(32773)]
    public void A_compressed_tiled_region_does_not_depend_on_the_number_of_threads(ushort compression)
    {
        // 9 x 7 tiles of 16 x 16, with partial tiles at the right and the bottom.
        const int width = 140, height = 100, across = 9, down = 7;
        var random = new Random(compression);
        var blocks = Enumerable.Range(0, across * down).Select(_ =>
        {
            byte[] tile = new byte[256];
            random.NextBytes(tile);
            return Encode(compression, tile);
        }).ToList();
        var builder = new TiffBuilder { Data = [.. blocks.SelectMany(b => b)] };
        var offsets = new List<long>();
        long at = builder.DataOffset;
        foreach (var b in blocks)
        {
            offsets.Add(at);
            at += b.Length;
        }

        builder.AddDirectory().Image(width, height).Remove(259).Add(259, TiffTagType.Short, compression).Remove(273).Remove(278).Remove(279)
            .Add(274, TiffTagType.Short, 6).Add(322, TiffTagType.Short, 16).Add(323, TiffTagType.Short, 16)
            .Add(324, TiffTagType.Long, [.. offsets]).Add(325, TiffTagType.Long, [.. blocks.Select(b => (long)b.Length)]);
        using var one = Open(builder, TiffReadOptions.Default with { MaxDegreeOfParallelism = 1 });
        using var many = Open(builder, TiffReadOptions.Default with { MaxDegreeOfParallelism = 8 });

        Assert.Equal(one.Primary.ReadPixels<byte>(), many.Primary.ReadPixels<byte>());
        Assert.Equal(one.Primary.ReadRegion<float>(7, 21, 60, 100, order: TiffPixelOrder.Planar),
            many.Primary.ReadRegion<float>(7, 21, 60, 100, order: TiffPixelOrder.Planar));
    }

    [Fact]
    public void A_predictor_is_ignored_without_LZW_or_Deflate_and_horizontal_applies_to_floating_point()
    {
        // Uncompressed 12-bit samples with a stray predictor tag: libtiff ignores the tag, and so does the library.
        var stray = new TiffBuilder { Data = [0xAB, 0xC1, 0x23] };
        stray.AddDirectory().Image(2, 1, bits: 12).Add(317, TiffTagType.Short, 2);
        using (var file = Open(stray))
        {
            Assert.Null(file.Primary.UnsupportedReason);
            Assert.Equal(new ushort[] { 0xABC, 0x123 }, file.Primary.ReadRawPixels<ushort>());
        }

        // Horizontal differencing on 32-bit floating point samples, as libtiff writes it: on the bits of the samples.
        float[] values = [1.5f, -2.25f, 1e30f];
        uint[] bits = [.. values.Select(BitConverter.SingleToUInt32Bits)];
        byte[] differenced = [.. bits.Select((b, i) => i == 0 ? b : b - bits[i - 1]).SelectMany(BitConverter.GetBytes)];
        byte[] strip = Zlib(differenced);
        var builder = new TiffBuilder { Data = strip };
        builder.AddDirectory().Image(3, 1, bits: 32).Remove(259).Add(259, TiffTagType.Short, 8).Remove(279).Add(279, TiffTagType.Long, strip.Length)
            .Add(317, TiffTagType.Short, 2).Add(339, TiffTagType.Short, 3);
        using var floats = Open(builder);
        Assert.Equal(values, floats.Primary.ReadRawPixels<float>());
    }
}

/// <summary>Compressed data that decodes to far more than it is: a read costs what it delivers, not what the data could give.</summary>
[Collection(nameof(Measurement))]
public class HostileCompressionTests
{
    [Theory]
    [InlineData(5)]
    [InlineData(8)]
    [InlineData(32773)]
    public void A_bomb_in_a_small_image_decodes_only_what_the_image_needs(ushort compression)
    {
        // 64 MB of zeros in a few kilobytes, in the strip of an image of 16 x 16.
        byte[] zeros = new byte[64 << 20];
        byte[] bomb = compression switch
        {
            5 => LzwTests.Encode(zeros.AsSpan(0, 4 << 20)),
            32773 => [.. Enumerable.Repeat(new byte[] { 0x81, 0 }, 1 << 19).SelectMany(b => b)],
            _ => Compress(zeros),
        };
        var builder = new TiffBuilder { Data = bomb };
        builder.AddDirectory().Image(16, 16).Remove(259).Add(259, TiffTagType.Short, compression).Remove(279).Add(279, TiffTagType.Long, bomb.Length);
        using var file = TiffFile.Read(new MemoryStream(builder.Build()));

        HostileAssert.FailsWithinBounds(() => file.Primary.ReadPixels<byte>(), "TiffException", TimeSpan.FromSeconds(1), 4L << 20, "bomb",
            successAllowed: true);
    }

    [Theory]
    [InlineData(5)]
    [InlineData(8)]
    [InlineData(32773)]
    public void A_tiny_compressed_file_declaring_a_huge_image_fails_before_allocating_it(ushort compression)
    {
        // 10 000 x 10 000 pixels in one strip of 20 bytes: no codec expands 20 bytes to 100 MB, which is known before allocating.
        byte[] strip = Compress(new byte[16]);
        var builder = new TiffBuilder { Data = strip };
        builder.AddDirectory().Image(10_000, 10_000).Remove(259).Add(259, TiffTagType.Short, compression).Remove(279)
            .Add(279, TiffTagType.Long, strip.Length);
        using var file = TiffFile.Read(new MemoryStream(builder.Build()), TiffReadOptions.Default with { MaxPixelBytes = 1L << 30 });

        HostileAssert.FailsWithinBounds(() => file.Primary.ReadPixels<double>(), "TiffException", TimeSpan.FromSeconds(1), 1 << 20, "huge");
        Assert.Contains("cannot decode to", Assert.Throws<TiffException>(() => file.Primary.ReadPixels<byte>()).Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(TiffCompression.PackBits, 2, 128)]
    [InlineData(TiffCompression.Deflate, 1, 1032)]
    [InlineData(TiffCompression.Lzw, 9, 8 * 4096)]
    public void The_bound_of_what_a_block_decodes_to_is_above_what_a_codec_can_give(TiffCompression compression, long count, long most)
    {
        Assert.True(Internal.Pixels.PixelPlan.MostDecoded(compression, count) >= most);
    }

    [Fact]
    public void The_most_expanding_data_of_each_codec_is_within_the_bound()
    {
        byte[] zeros = new byte[1 << 22];
        byte[] deflate = Compress(zeros);
        byte[] lzw = Codecs.LzwTests.Encode(zeros);
        Assert.True(zeros.Length <= Internal.Pixels.PixelPlan.MostDecoded(TiffCompression.Deflate, deflate.Length));
        Assert.True(zeros.Length <= Internal.Pixels.PixelPlan.MostDecoded(TiffCompression.Lzw, lzw.Length));
        Assert.True(128 <= Internal.Pixels.PixelPlan.MostDecoded(TiffCompression.PackBits, 2));
    }

    [Fact]
    public void Tiles_sharing_one_stream_that_declare_the_whole_file_cost_what_their_pixels_need()
    {
        // 20 000 tiles of 16 x 16, all pointing at one small Deflate stream and each declaring the whole file as its bytes:
        // every tile reads only what its pixels can need, not the whole file each time.
        const int tiles = 20_000;
        byte[] stream = Compress(new byte[256]);
        var builder = new TiffBuilder { Data = [.. stream, .. new byte[2_000_000]] };
        long length = builder.DataOffset + stream.Length + 2_000_000;
        builder.AddDirectory().Image(16 * 200, 16 * 100).Remove(259).Add(259, TiffTagType.Short, 8).Remove(273).Remove(278).Remove(279)
            .Add(322, TiffTagType.Short, 16).Add(323, TiffTagType.Short, 16)
            .Add(324, TiffTagType.Long, [.. Enumerable.Repeat(builder.DataOffset, tiles)])
            .Add(325, TiffTagType.Long, [.. Enumerable.Repeat(length, tiles)]);
        using var file = TiffFile.Read(new MemoryStream(builder.Build()));

        HostileAssert.FailsWithinBounds(() => file.Primary.ReadPixels<byte>(), "TiffException", TimeSpan.FromSeconds(2), 32L << 20, "shared tiles",
            successAllowed: true);
    }

    private static byte[] Compress(byte[] data)
    {
        using var output = new MemoryStream();
        using (var zlib = new ZLibStream(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            zlib.Write(data);
        }

        return output.ToArray();
    }
}
