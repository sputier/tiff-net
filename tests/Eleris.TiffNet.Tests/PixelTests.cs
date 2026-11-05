using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Eleris.TiffNet.Tests.Fixtures;
using Eleris.TiffNet.Tests.Interop;

namespace Eleris.TiffNet.Tests;

public class PixelTests
{
    /// <summary>The raw samples of an image, as little-endian bytes, whatever their type.</summary>
    internal static byte[] RawBytes(TiffImage image, TiffPixelOrder order = TiffPixelOrder.Interleaved) => image.SampleType switch
    {
        TiffSampleType.UInt8 => image.ReadRawPixels<byte>(order),
        TiffSampleType.Int8 => Bytes(image.ReadRawPixels<sbyte>(order)),
        TiffSampleType.UInt16 => Bytes(image.ReadRawPixels<ushort>(order)),
        TiffSampleType.Int16 => Bytes(image.ReadRawPixels<short>(order)),
        TiffSampleType.UInt32 => Bytes(image.ReadRawPixels<uint>(order)),
        TiffSampleType.Int32 => Bytes(image.ReadRawPixels<int>(order)),
        TiffSampleType.UInt64 => Bytes(image.ReadRawPixels<ulong>(order)),
        TiffSampleType.Int64 => Bytes(image.ReadRawPixels<long>(order)),
        TiffSampleType.Float16 => Bytes(image.ReadRawPixels<Half>(order)),
        TiffSampleType.Float32 => Bytes(image.ReadRawPixels<float>(order)),
        _ => Bytes(image.ReadRawPixels<double>(order)),
    };

    private static byte[] Bytes<T>(T[] values) where T : unmanaged => MemoryMarshal.AsBytes(values.AsSpan()).ToArray();

    internal static string Sha(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    /// <summary>Whether the codecs the library has so far decode the image (JPEG comes later).</summary>
    internal static bool Decodable(TiffStorage storage) =>
        storage.Compression is TiffCompression.None or TiffCompression.Lzw or TiffCompression.Deflate or TiffCompression.PackBits;

    [Fact]
    public void Every_decodable_page_of_the_domain_fixtures_gives_the_expected_samples()
    {
        int checkedPages = 0;
        foreach (var (name, entry) in DomainFixtures.Files)
        {
            using var file = TiffFile.Open(DomainFixtures.PathOf(name));
            for (int i = 0; i < file.Count; i++)
            {
                var image = file[i];
                if (image.UnsupportedReason is not null || !Decodable(image.Storage!))
                {
                    continue;
                }

                var page = entry.GetProperty("pages")[i];
                // Orientation expectations are given by the generator; palettes and MinIsWhite are computed from what tifffile reads.
                string expected = i == 0 && DomainFixtures.Expect(name) is { } e && e.TryGetProperty("delivered", out var delivered)
                    ? delivered.GetString()!
                    : page.TryGetProperty("deliveredSamplesSha256", out var computed)
                        ? computed.GetString()!
                        : page.GetProperty("storedSamplesSha256").GetString()!;
                Assert.True(expected == Sha(RawBytes(image)), $"{name}, page {i}");
                checkedPages++;
            }
        }

        Assert.True(checkedPages > 35, $"only {checkedPages} pages checked");
    }

    /// <summary>The compressions of the corpus the library refuses by design (coverage.md): their files must say so.</summary>
    private static readonly string[] RefusedByDesign = ["lzma", "zstd"];

    [Fact]
    public void Every_file_of_the_interop_corpus_gives_the_samples_of_tifffile_or_is_refused_by_design()
    {
        if (!Directory.Exists(InteropCorpus.Folder))
        {
            Assert.True(InteropCorpus.Skipped, $"The interop corpus is not generated; set {InteropCorpus.SkipVariable}=1 to skip.");
            return;
        }

        var counts = new Dictionary<TiffCompression, int>();
        var predicted = new Dictionary<TiffPredictor, int>();
        int refused = 0;
        foreach (var f in InteropCorpus.Files)
        {
            using var file = TiffFile.Open(Path.Combine(InteropCorpus.Folder, f.Name));
            var image = file.Primary;
            if (RefusedByDesign.Contains(f.Compression))
            {
                Assert.True(image.UnsupportedReason?.Contains("is not supported", StringComparison.Ordinal) == true, f.Name);
                Assert.Throws<NotSupportedException>(() => image.ReadPixels<double>());
                refused++;
                continue;
            }

            // Anything else is read, and gives exactly the samples of tifffile: nothing is skipped.
            Assert.True(image.UnsupportedReason is null, $"{f.Name}: {image.UnsupportedReason}");
            Assert.True(f.SamplesSha256 == Sha(RawBytes(image)), f.Name);
            var storage = image.Storage!;
            counts[storage.Compression] = counts.GetValueOrDefault(storage.Compression) + 1;
            predicted[storage.Predictor] = predicted.GetValueOrDefault(storage.Predictor) + 1;
        }

        Assert.Equal(InteropCorpus.Files.Count, refused + counts.Values.Sum());
        Assert.True(predicted.GetValueOrDefault(TiffPredictor.Horizontal) > 300, $"only {predicted.GetValueOrDefault(TiffPredictor.Horizontal)} horizontal");
        Assert.True(predicted.GetValueOrDefault(TiffPredictor.FloatingPoint) > 100, $"only {predicted.GetValueOrDefault(TiffPredictor.FloatingPoint)} floating point");
        Assert.All([TiffCompression.None, TiffCompression.Lzw, TiffCompression.Deflate, TiffCompression.PackBits],
            c => Assert.True(counts.GetValueOrDefault(c) > 300, $"only {counts.GetValueOrDefault(c)} {c} files"));
    }

    [Theory]
    [InlineData("siril_m42_grey8.tif")]
    [InlineData("siril_m42_grey16.tif")]
    [InlineData("siril_m42_grey16_astro.tif")]
    [InlineData("siril_m42_grey16_deflate.tif")]
    [InlineData("siril_m42_grey32_astro.tif")]
    [InlineData("siril_m42_rgb8.tif")]
    [InlineData("siril_m42_rgb16_astro.tif")]
    [InlineData("siril_m42_rgb32_astro.tif")]
    public void Every_Siril_file_is_read_with_its_metadata(string name)
    {
        using var file = TiffFile.Open(DomainFixtures.PathOf(name));
        var image = file.Primary;
        var page = DomainFixtures.Page(name);

        Assert.Equal(page.GetProperty("storedSamplesSha256").GetString(), Sha(RawBytes(image)));
        Assert.StartsWith("siril", image.Metadata.Software, StringComparison.Ordinal);
        if (name.Contains("astro", StringComparison.Ordinal))
        {
            // The FITS header Siril writes in the description (Astro-TIFF).
            Assert.Equal(page.GetProperty("description").GetString(), image.Metadata.Description);
            Assert.Contains("OBJECT  = 'M42", image.Metadata.Description, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("orientation_6.tif")]
    [InlineData("orientation_7.tif")]
    [InlineData("rgba_unassalpha.tif")]
    [InlineData("grey_3_planes.tif")]
    [InlineData("palette_8bit.tif")]
    [InlineData("grey_12bit_packed.tif")]
    [InlineData("siril_m42_rgb16_astro.tif")]
    public void A_region_is_the_same_samples_as_the_whole_image(string name)
    {
        using var file = TiffFile.Open(DomainFixtures.PathOf(name));
        var image = file.Primary;
        int c = image.Channels;
        var whole = image.ReadPixels<double>();
        var random = new Random(name.Length);
        for (int k = 0; k < 20; k++)
        {
            int x = random.Next(image.Width), y = random.Next(image.Height);
            int w = random.Next(1, image.Width - x + 1), h = random.Next(1, image.Height - y + 1);

            var region = image.ReadRegion<double>(x, y, w, h);

            for (int row = 0; row < h; row++)
            {
                Assert.True(whole.AsSpan(((y + row) * image.Width + x) * c, w * c).SequenceEqual(region.AsSpan(row * w * c, w * c)),
                    $"{name}: region {x},{y} {w}x{h}, row {row}");
            }
        }
    }

    [Theory]
    [InlineData("rgba_assocalpha.tif")]
    [InlineData("grey_3_planes.tif")]
    [InlineData("orientation_8.tif")]
    public void Planar_order_and_channels_are_the_interleaved_samples_rearranged(string name)
    {
        using var file = TiffFile.Open(DomainFixtures.PathOf(name));
        var image = file.Primary;
        int n = image.Width * image.Height, c = image.Channels;
        var interleaved = image.ReadPixels<double>();
        var planar = image.ReadPixels<double>(order: TiffPixelOrder.Planar);

        for (int ch = 0; ch < c; ch++)
        {
            var channel = image.ReadChannel<double>(ch);
            for (int p = 0; p < n; p++)
            {
                Assert.Equal(interleaved[p * c + ch], planar[ch * n + p]);
                Assert.Equal(interleaved[p * c + ch], channel[p]);
            }
        }
    }

    [Fact]
    public void Normalised_samples_are_divided_by_the_largest_value_of_their_depth()
    {
        double MaxOf(string name)
        {
            using var file = TiffFile.Open(DomainFixtures.PathOf(name));
            var raw = file.Primary.ReadPixels<double>();
            var normalised = file.Primary.ReadPixels<double>(TiffPixelScale.Normalized);
            int i = Array.FindIndex(raw, v => v > 0);
            return raw[i] / normalised[i];
        }

        Assert.Equal(65535, MaxOf("pillow_grey16.tif"), 6);
        Assert.Equal(4095, MaxOf("grey_12bit_packed.tif"), 6);
        Assert.Equal(1, MaxOf("grey_1bit.tif"), 6);
        Assert.Equal(65535, MaxOf("palette_8bit.tif"), 6);
        using var floats = TiffFile.Open(DomainFixtures.PathOf("pillow_float32.tif"));
        Assert.Equal(floats.Primary.ReadPixels<double>(), floats.Primary.ReadPixels<double>(TiffPixelScale.Normalized));
    }

    [Fact]
    public void Conversions_that_could_lose_values_are_refused()
    {
        using var file = TiffFile.Open(DomainFixtures.PathOf("pillow_grey16.tif"));   // UInt16
        var image = file.Primary;

        Assert.Equal(image.ReadRawPixels<ushort>().Select(v => (int)v), image.ReadPixels<int>());
        Assert.Equal(image.ReadRawPixels<ushort>().Select(v => (ulong)v), image.ReadPixels<ulong>());
        Assert.Equal(image.ReadRawPixels<ushort>().Select(v => (float)v), image.ReadPixels<float>());
        Assert.Throws<InvalidOperationException>(() => image.ReadPixels<byte>());
        Assert.Throws<InvalidOperationException>(() => image.ReadPixels<short>());
        Assert.Throws<InvalidOperationException>(() => image.ReadRawPixels<uint>());
        Assert.Throws<InvalidOperationException>(() => image.ReadPixels<int>(TiffPixelScale.Normalized));

        using var floats = TiffFile.Open(DomainFixtures.PathOf("pillow_float32.tif"));
        Assert.Throws<InvalidOperationException>(() => floats.Primary.ReadPixels<long>());
    }

    [Fact]
    public void The_result_does_not_depend_on_the_number_of_threads()
    {
        string path = DomainFixtures.PathOf("siril_m42_rgb32_astro.tif");
        using var one = TiffFile.Open(path, TiffReadOptions.Default with { MaxDegreeOfParallelism = 1 });
        using var many = TiffFile.Open(path, TiffReadOptions.Default with { MaxDegreeOfParallelism = 8 });

        Assert.Equal(Sha(RawBytes(one.Primary)), Sha(RawBytes(many.Primary)));
        Assert.Equal(Sha(RawBytes(one.Primary, TiffPixelOrder.Planar)), Sha(RawBytes(many.Primary, TiffPixelOrder.Planar)));
    }

    [Fact]
    public void A_span_of_the_caller_is_filled()
    {
        using var file = TiffFile.Open(DomainFixtures.PathOf("rgb_12bit_packed.tif"));
        var image = file.Primary;
        var buffer = new float[image.Width * image.Height * image.Channels];

        image.ReadPixels<float>(buffer);

        Assert.Equal(image.ReadPixels<float>(), buffer);
        Assert.Throws<ArgumentException>(() => image.ReadPixels<float>(new float[3]));
    }

    [Fact]
    public async Task An_asynchronous_read_gives_the_same_samples_and_can_be_cancelled()
    {
        using var file = TiffFile.Open(DomainFixtures.PathOf("siril_m42_grey32_astro.tif"));

        Assert.Equal(file.Primary.ReadPixels<float>(), await file.Primary.ReadPixelsAsync<float>());
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => file.Primary.ReadPixelsAsync<float>(cancellationToken: cancelled.Token));
    }

    [Fact]
    public void Pixels_beyond_MaxPixelBytes_are_refused_before_they_are_allocated()
    {
        using var file = TiffFile.Open(DomainFixtures.PathOf("siril_m42_rgb32_astro.tif"),
            TiffReadOptions.Default with { MaxPixelBytes = 100_000 });

        Assert.Equal("MaxPixelBytes", Assert.Throws<TiffException>(() => file.Primary.ReadPixels<float>()).Limit);
        Assert.Single(file.Primary.ReadRegion<float>(0, 0, 1, 1).Chunk(3));   // a small region still fits
    }

    [Fact]
    public void A_truncated_strip_is_a_damaged_file()
    {
        var builder = new TiffBuilder { Data = new byte[10] };
        builder.AddDirectory().Image(4, 3).Remove(279).Add(279, TiffTagType.Long, 10);   // 12 bytes needed

        using var file = TiffFile.Read(new MemoryStream(builder.Build()));

        Assert.Contains("truncated", Assert.Throws<TiffException>(() => file.Primary.ReadPixels<byte>()).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Arguments_are_checked()
    {
        using var file = TiffFile.Open(DomainFixtures.PathOf("rgba_unassalpha.tif"));
        var image = file.Primary;

        Assert.Throws<ArgumentOutOfRangeException>(() => image.ReadRegion<float>(-1, 0, 1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => image.ReadRegion<float>(0, 0, 0, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => image.ReadRegion<float>(image.Width - 1, 0, 2, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => image.ReadChannel<float>(4));
        using var complex = TiffFile.Open(DomainFixtures.PathOf("complex64.tif"));
        Assert.Throws<NotSupportedException>(() => complex.Primary.ReadPixels<double>());
    }

    [Fact]
    public void The_pixels_of_a_disposed_file_cannot_be_read()
    {
        var file = TiffFile.Open(DomainFixtures.PathOf("pillow_grey16.tif"));
        var image = file.Primary;
        file.Dispose();

        Assert.Throws<ObjectDisposedException>(() => image.ReadPixels<float>());
        Assert.NotNull(image.Metadata);   // the metadata stays available
    }
}
