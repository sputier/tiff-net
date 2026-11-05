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
        for (int i = 0; i < planes.Length; i++)
        {
            Assert.True(planes[i] == Convert.ToHexString(SHA256.HashData(JpegFixtures.Samples(frame.Components[i]))), $"{name}, component {i}");
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
    public void A_truncated_scan_decodes_to_the_end_of_its_blocks()
    {
        byte[] data = JpegFixtures.Bytes("ycc420_q95_256x192.jpg");
        var frame = JpegFixtures.Decode(data[..(data.Length / 2)]);

        Assert.Equal(256, frame.Components[0].Width);
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
