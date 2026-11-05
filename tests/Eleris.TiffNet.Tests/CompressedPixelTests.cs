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
        foreach (var f in InteropCorpus.Files.Where(f => f.Tiled && !f.Predictor && f.Compression is "lzw" or "zlib" or "packbits").Take(60))
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

    [Fact]
    public void Images_with_a_predictor_are_not_read_yet()
    {
        byte[] strip = Zlib(new byte[100]);
        var builder = new TiffBuilder { Data = strip };
        builder.AddDirectory().Image(10, 10).Remove(259).Add(259, TiffTagType.Short, 8).Remove(279).Add(279, TiffTagType.Long, strip.Length)
            .Add(317, TiffTagType.Short, 2);
        using var file = Open(builder);

        Assert.Contains("predictor", Assert.Throws<NotSupportedException>(() => file.Primary.ReadPixels<float>()).Message, StringComparison.Ordinal);
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

    [Fact]
    public void A_tiny_compressed_file_declaring_a_huge_image_fails_within_its_budget()
    {
        // 30 000 x 30 000 pixels in one Deflate strip of 20 bytes: the destination and one decoding buffer of 900 MB each exceed
        // the budget; with a larger budget, the read fails as soon as the strip is decoded.
        byte[] strip = Compress(new byte[16]);
        var builder = new TiffBuilder { Data = strip };
        builder.AddDirectory().Image(30_000, 30_000).Remove(259).Add(259, TiffTagType.Short, 8).Remove(279).Add(279, TiffTagType.Long, strip.Length);
        using var file = TiffFile.Read(new MemoryStream(builder.Build()), TiffReadOptions.Default with { MaxPixelBytes = 1L << 30 });

        HostileAssert.FailsWithinBounds(() => file.Primary.ReadPixels<byte>(), "TiffException:MaxPixelBytes", TimeSpan.FromSeconds(1), 1 << 20, "huge");
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
