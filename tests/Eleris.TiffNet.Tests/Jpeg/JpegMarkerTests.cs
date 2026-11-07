using System.Security.Cryptography;
using Eleris.TiffNet.Internal;
using Eleris.TiffNet.Internal.Jpeg;

namespace Eleris.TiffNet.Tests.Jpeg;

public class JpegMarkerTests
{
    /// <summary>The segments of a stream before its first scan, then the scan to the end: (marker, offset, length with the marker).</summary>
    private static List<(byte Marker, int Offset, int Length)> Segments(byte[] data)
    {
        var segments = new List<(byte, int, int)>();
        int p = 2;
        while (p + 4 <= data.Length && data[p] == 0xFF && data[p + 1] != 0xDA)
        {
            int length = data[p + 2] << 8 | data[p + 3];
            segments.Add((data[p + 1], p, length + 2));
            p += length + 2;
        }

        segments.Add((0xDA, p, data.Length - p));
        return segments;
    }

    private static byte[] Keep(byte[] data, Func<byte, bool> keep) =>
        [.. data[..2], .. Segments(data).Where(s => keep(s.Marker)).SelectMany(s => data.AsSpan(s.Offset, s.Length).ToArray())];

    private static byte[] Planes(JpegFrame frame) => [.. frame.Components.SelectMany(JpegFixtures.Samples)];

    private static JpegDecoder Decoder() => new(new ResourceBudget(TiffReadOptions.Default, "test"));

    [Fact]
    public void An_abbreviated_stream_decodes_with_the_tables_read_first()
    {
        // As TIFF stores JPEG: the tables in JPEGTables (SOI, DQT, DHT, EOI), each strip without them.
        byte[] data = JpegFixtures.Bytes("ycc420_q85_45x33.jpg");
        byte[] tables = [.. Keep(data, m => m is 0xDB or 0xC4), 0xFF, 0xD9];
        byte[] image = Keep(data, m => m is not (0xDB or 0xC4));

        var decoder = Decoder();
        decoder.ReadTables(tables);

        Assert.Equal(Planes(JpegFixtures.Decode(data)), Planes(decoder.Decode(image)));
        Assert.Throws<InvalidDataException>(() => Decoder().ReadTables(data));   // a whole image is not a table stream
    }

    [Fact]
    public void A_stream_without_Huffman_tables_uses_those_of_annex_K()
    {
        // libjpeg writes the tables of annex K unless optimised: without them, the stream decodes the same.
        byte[] data = JpegFixtures.Bytes("ycc444_q90_37x29.jpg");

        Assert.Equal(Planes(JpegFixtures.Decode(data)), Planes(JpegFixtures.Decode(Keep(data, m => m != 0xC4))));
    }

    [Fact]
    public void A_missing_quantization_table_is_damaged_data() =>
        Assert.Throws<InvalidDataException>(() => JpegFixtures.Decode(Keep(JpegFixtures.Bytes("grey_q90_37x29.jpg"), m => m != 0xDB)));

    [Theory]
    [InlineData(0xC3)]   // lossless
    [InlineData(0xC5)]   // hierarchical
    [InlineData(0xC9)]   // arithmetic
    public void Other_processes_are_not_supported(byte sof)
    {
        byte[] data = JpegFixtures.Bytes("grey_q90_37x29.jpg");
        var frame = Segments(data).Single(s => s.Marker is 0xC0 or 0xC1);
        data[frame.Offset + 1] = sof;

        Assert.Throws<NotSupportedException>(() => JpegFixtures.Decode(data));
    }

    [Fact]
    public void Frames_the_library_does_not_handle_are_not_supported()
    {
        byte[] Frame(Action<byte[], int> change)
        {
            byte[] data = JpegFixtures.Bytes("grey_q90_37x29.jpg");
            change(data, Segments(data).Single(s => s.Marker is 0xC0 or 0xC1).Offset);
            return data;
        }

        // The frame header: FF C0, length (2), precision, height (2), width (2), components.
        Assert.Throws<NotSupportedException>(() => JpegFixtures.Decode(Frame((d, f) => d[f + 4] = 12)));                    // 12-bit
        Assert.Throws<NotSupportedException>(() => JpegFixtures.Decode(Frame((d, f) => (d[f + 5], d[f + 6]) = (0, 0))));     // DNL
        Assert.Throws<NotSupportedException>(() => JpegFixtures.Decode([0xFF, 0xD8, 0xFF, 0xC0, 0, 20, 8, 0, 8, 0, 8, 4,
            1, 0x11, 0, 2, 0x11, 0, 3, 0x11, 0, 4, 0x11, 0]));                                                              // CMYK
    }

    [Fact]
    public void Damaged_structures_are_invalid_data()
    {
        byte[] data = JpegFixtures.Bytes("grey_q90_37x29.jpg");
        Assert.Throws<InvalidDataException>(() => JpegFixtures.Decode([0x00, 0x01, .. data[2..]]));   // no SOI
        Assert.Throws<InvalidDataException>(() => JpegFixtures.Decode(data[..20]));                  // a cut segment
        Assert.Throws<InvalidDataException>(() => JpegFixtures.Decode([0xFF, 0xD8, 0xFF, 0xD9]));    // no image
        byte[] sampling = (byte[])data.Clone();
        sampling[Segments(data).Single(s => s.Marker is 0xC0 or 0xC1).Offset + 11] = 0x55;           // sampling 5 x 5
        Assert.Throws<InvalidDataException>(() => JpegFixtures.Decode(sampling));
    }

    [Fact]
    public void Mutated_streams_fail_only_as_damaged_or_unsupported_data()
    {
        var random = new Random(30);
        var names = JpegFixtures.Files.Select(f => f.Name).ToArray();
        int decoded = 0;
        for (int i = 0; i < 3000; i++)
        {
            byte[] data = JpegFixtures.Bytes(names[i % names.Length]);
            for (int m = random.Next(1, 6); m > 0; m--)
            {
                data[random.Next(data.Length)] = (byte)random.Next(256);
            }

            try
            {
                var frame = JpegFixtures.Decode(data, TiffReadOptions.Default with { MaxPixelBytes = 64L << 20 });
                _ = SHA256.HashData(frame.Components[0].Plane);
                decoded++;
            }
            catch (Exception ex) when (ex is InvalidDataException or NotSupportedException or TiffException)
            {
            }
        }

        Assert.True(decoded > 1000, $"only {decoded} mutated streams decoded");
    }

    [Fact]
    public void A_component_that_no_scan_holds_is_grey()
    {
        // A frame of three components and a scan of the first only, as a multi-scan stream cut after its first scan.
        byte[] data = JpegFixtures.Bytes("ycc444_q90_37x29.jpg");
        var scan = Segments(data).Single(s => s.Marker == 0xDA);
        byte[] single = [.. data[..scan.Offset], 0xFF, 0xDA, 0, 8, 1, data[scan.Offset + 5], data[scan.Offset + 6], 0, 63, 0, 0xFF, 0xD9];

        var frame = JpegFixtures.Decode(single);

        Assert.All(frame.Components.Skip(1), c => Assert.All(c.Plane[..(c.Stride * c.PlaneRows)], b => Assert.Equal(128, b)));
    }

    [Fact]
    public void A_progressive_stream_cut_anywhere_after_its_first_scan_gives_what_it_holds()
    {
        // libjpeg also smooths the blocks of an incomplete progressive image; the library gives the coefficients as they are.
        byte[] data = JpegFixtures.Bytes("prog_ycc420_restart_256x192.jpg");
        int firstScan = Segments(data).Single(s => s.Marker == 0xDA).Offset;
        for (int length = firstScan + 20; length < data.Length; length += 97)
        {
            var frame = JpegFixtures.Decode(data[..length]);
            Assert.Equal((256, 192), (frame.Width, frame.Height));
        }
    }
}
