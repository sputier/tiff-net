using System.Security.Cryptography;
using System.Text.Json;
using Eleris.TiffNet.Internal;
using Eleris.TiffNet.Internal.Jpeg;

namespace Eleris.TiffNet.Tests.Jpeg;

/// <summary>The JPEG fixtures of tools/make_jpeg_fixtures.py and what libjpeg-turbo decodes them to.</summary>
internal static class JpegFixtures
{
    public static string Folder => Path.Combine(AppContext.BaseDirectory, "Fixtures", "jpeg");

    private static readonly Lazy<JsonElement> Manifest = new(() =>
        JsonDocument.Parse(File.ReadAllText(Path.Combine(Folder, "manifest.json"))).RootElement.GetProperty("files"));

    public static IEnumerable<(string Name, JsonElement Entry)> Files => Manifest.Value.EnumerateObject().Select(p => (p.Name, p.Value));

    public static TheoryData<string> Names() => [.. Files.Select(f => f.Name)];

    public static JsonElement Entry(string name) => Manifest.Value.GetProperty(name);

    public static byte[] Bytes(string name) => File.ReadAllBytes(Path.Combine(Folder, name));

    public static JpegFrame Decode(byte[] data, TiffReadOptions? options = null, CancellationToken cancellation = default) =>
        new JpegDecoder(new ResourceBudget(options ?? TiffReadOptions.Default, "test.jpg", cancellation)).Decode(data);

    /// <summary>The samples of a component within its own size, row after row (the plane is padded to whole MCUs).</summary>
    public static byte[] Samples(JpegComponent c) =>
        [.. Enumerable.Range(0, c.Height).SelectMany(y => c.Plane.AsSpan(y * c.Stride, c.Width).ToArray())];
}

public class JpegDecoderTests
{
    [Theory]
    [MemberData(nameof(JpegFixtures.Names), MemberType = typeof(JpegFixtures))]
    public void Every_fixture_decodes_to_the_samples_of_libjpeg_turbo(string name)
    {
        var entry = JpegFixtures.Entry(name);
        var frame = JpegFixtures.Decode(JpegFixtures.Bytes(name));

        Assert.Equal((entry.GetProperty("width").GetInt32(), entry.GetProperty("height").GetInt32()), (frame.Width, frame.Height));
        Assert.Equal(entry.GetProperty("components").GetInt32(), frame.Components.Length);
        var sampling = entry.GetProperty("sampling").EnumerateArray().Select(s => (s[0].GetInt32(), s[1].GetInt32())).ToArray();
        Assert.Equal(sampling, frame.Components.Select(c => (c.H, c.V)));
        var planes = entry.GetProperty("planes").EnumerateArray().Select(p => p.GetString()).ToArray();
        if (entry.TryGetProperty("truncated", out _))
        {
            Truncated(name, frame, planes.Length);
            return;
        }

        for (int i = 0; i < planes.Length; i++)
        {
            Assert.True(planes[i] == Convert.ToHexString(SHA256.HashData(JpegFixtures.Samples(frame.Components[i]))), $"{name}, component {i}");
        }
    }

    /// <summary>
    /// A cut stream decodes as libjpeg decodes it: every sample is the same, except in the one MCU where the data runs out, whose
    /// samples are decoded from zero bits (noise in any decoder); the MCUs after it are grey.
    /// </summary>
    private static void Truncated(string name, JpegFrame frame, int planes)
    {
        byte[] expected = File.ReadAllBytes(Path.Combine(JpegFixtures.Folder, Path.ChangeExtension(name, ".planes")));
        int hMax = frame.Components.Max(c => c.H), vMax = frame.Components.Max(c => c.V);
        var mcus = new HashSet<(int, int)>();
        int offset = 0;
        for (int i = 0; i < planes; i++)
        {
            var c = frame.Components[i];
            byte[] actual = JpegFixtures.Samples(c);
            for (int k = 0; k < actual.Length; k++)
            {
                if (actual[k] != expected[offset + k])
                {
                    mcus.Add((k % c.Width / (8 * c.H), k / c.Width / (8 * c.V)));
                }
            }

            offset += actual.Length;
        }

        Assert.True(mcus.Count <= 1, $"{name}: {mcus.Count} MCUs differ from libjpeg-turbo");
        Assert.True(hMax * vMax > 0);
    }

    public static TheoryData<string> Colour() =>
        [.. JpegFixtures.Files.Where(f => f.Entry.TryGetProperty("rgb", out _) && !f.Entry.TryGetProperty("truncated", out _)).Select(f => f.Name)];

    [Theory]
    [MemberData(nameof(Colour))]
    public void Colour_images_upsample_and_convert_to_the_RGB_of_libjpeg_turbo(string name)
    {
        var frame = JpegFixtures.Decode(JpegFixtures.Bytes(name));
        byte[] rgb = new byte[frame.Width * frame.Height * 3];

        JpegColour.ToSamples(frame, frame.Height, ycbcr: true, rgb, frame.Width * 3, new ResourceBudget(TiffReadOptions.Default, "test"));

        Assert.Equal(JpegFixtures.Entry(name).GetProperty("rgb").GetString(), Convert.ToHexString(SHA256.HashData(rgb)));
    }

    [Fact]
    public void Components_that_are_not_YCbCr_are_interleaved_as_they_are()
    {
        var frame = JpegFixtures.Decode(JpegFixtures.Bytes("ycc444_q90_37x29.jpg"));
        byte[] samples = new byte[37 * 29 * 3 + 5];

        JpegColour.ToSamples(frame, frame.Height, ycbcr: false, samples, 37 * 3, new ResourceBudget(TiffReadOptions.Default, "test"));

        for (int c = 0; c < 3; c++)
        {
            byte[] plane = JpegFixtures.Samples(frame.Components[c]);
            Assert.Equal(plane, Enumerable.Range(0, 37 * 29).Select(i => samples[i * 3 + c]));
        }
    }

    [Fact]
    public void Subsampled_components_have_their_own_size()
    {
        var frame = JpegFixtures.Decode(JpegFixtures.Bytes("ycc420_q85_45x33.jpg"));

        Assert.Equal([(45, 33), (23, 17), (23, 17)], frame.Components.Select(c => (c.Width, c.Height)));
        var frame422 = JpegFixtures.Decode(JpegFixtures.Bytes("ycc422_q85_45x33.jpg"));
        Assert.Equal([(45, 33), (23, 33), (23, 33)], frame422.Components.Select(c => (c.Width, c.Height)));
    }

    [Fact]
    public void A_scan_whose_data_runs_out_costs_a_fill_not_a_decoding()
    {
        // A few bytes of scan for 4000 x 4000 pixels: libjpeg decodes the first MCU and leaves the others grey.
        byte[] data = JpegFixtures.Bytes("grey_q90_37x29.jpg");
        int sof = data.AsSpan().IndexOf([(byte)0xFF, (byte)0xC0]);
        (data[sof + 5], data[sof + 6], data[sof + 7], data[sof + 8]) = (0x0F, 0xA0, 0x0F, 0xA0);
        int sos = data.AsSpan().IndexOf([(byte)0xFF, (byte)0xDA]);
        byte[] cut = data[..(sos + 20)];

        var clock = System.Diagnostics.Stopwatch.StartNew();
        var frame = JpegFixtures.Decode(cut);

        Assert.True(clock.ElapsedMilliseconds < 1000, $"{clock.ElapsedMilliseconds} ms");
        Assert.Equal(128, frame.Components[0].Plane[^1]);
    }

    [Fact]
    public void A_frame_without_a_scan_is_damaged_data()
    {
        byte[] data = JpegFixtures.Bytes("grey_q90_37x29.jpg");
        int sos = data.AsSpan().IndexOf([(byte)0xFF, (byte)0xDA]);

        Assert.Throws<InvalidDataException>(() => JpegFixtures.Decode([.. data[..sos], 0xFF, 0xD9]));
    }

    [Fact]
    public void A_frame_too_large_for_an_array_is_not_supported_even_within_the_budget()
    {
        byte[] frame = [0xFF, 0xD8, 0xFF, 0xC0, 0, 11, 8, 0xB5, 0x04, 0xB5, 0x04, 1, 1, 0x11, 0, 0xFF, 0xD9];   // 46340 x 46340

        Assert.Throws<NotSupportedException>(() => JpegFixtures.Decode(frame, TiffReadOptions.Default with { MaxPixelBytes = 4L << 30 }));
    }

    [Fact]
    public void Tables_holding_a_frame_are_refused_before_the_frame_is_allocated()
    {
        byte[] tables = [0xFF, 0xD8, 0xFF, 0xC0, 0, 11, 8, 0x7F, 0xFF, 0x7F, 0xFF, 1, 1, 0x11, 0, 0xFF, 0xD9];
        var decoder = new JpegDecoder(new ResourceBudget(TiffReadOptions.Default with { MaxPixelBytes = 1000 }, "test"));

        Assert.Throws<InvalidDataException>(() => decoder.ReadTables(tables));
    }

    [Fact]
    public void A_cancelled_read_stops_between_rows_of_MCUs()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.ThrowsAny<OperationCanceledException>(() =>
            JpegFixtures.Decode(JpegFixtures.Bytes("ycc420_q95_256x192.jpg"), cancellation: cancellation.Token));
    }

    [Fact]
    public void The_planes_are_counted_in_the_budget_before_they_are_allocated()
    {
        var limited = TiffReadOptions.Default with { MaxPixelBytes = 1000 };

        Assert.Equal("MaxPixelBytes", Assert.Throws<TiffException>(() => JpegFixtures.Decode(JpegFixtures.Bytes("grey_q90_37x29.jpg"), limited)).Limit);
    }
}
