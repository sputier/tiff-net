using Eleris.TiffNet.Internal;
using Eleris.TiffNet.Internal.Jpeg;

namespace Eleris.TiffNet.Tests.Jpeg;

/// <summary>JPEG streams in TIFF strips (Technical Note 2), built around the fixtures.</summary>
public class JpegInTiffTests
{
    /// <summary>A grey 8-bit TIFF of one JPEG strip holding <paramref name="stream"/>.</summary>
    private static TiffFile Strip(byte[] stream, int width, int height, int rowsPerStrip, byte[]? tables = null, ushort photometric = 1, ushort samples = 1)
    {
        var builder = new TiffBuilder { Data = stream };
        var d = builder.AddDirectory().Image(width, height, samples: samples, photometric: photometric).Remove(259).Add(259, TiffTagType.Short, 7)
            .Remove(278).Add(278, TiffTagType.Long, rowsPerStrip).Remove(279).Add(279, TiffTagType.Long, stream.Length);
        if (tables is not null)
        {
            d.Raw(347, TiffTagType.Undefined, tables);
        }

        d.Sorted();
        return TiffFile.Read(new MemoryStream(builder.Build()));
    }

    private static byte[] Decoded(string name, int rows)
    {
        var frame = JpegFixtures.Decode(JpegFixtures.Bytes(name));
        int channels = JpegColour.Channels(frame);
        byte[] samples = new byte[frame.Width * frame.Height * channels];
        JpegColour.ToSamples(frame, rows, ycbcr: channels == 3, samples, frame.Width * channels, new ResourceBudget(TiffReadOptions.Default, "t"));
        return samples[..(frame.Width * rows * channels)];
    }

    [Fact]
    public void A_strip_gives_the_samples_of_its_stream()
    {
        using var file = Strip(JpegFixtures.Bytes("grey_q90_37x29.jpg"), 37, 29, 29);

        Assert.Equal(Decoded("grey_q90_37x29.jpg", 29), file.Primary.ReadRawPixels<byte>());
    }

    [Fact]
    public void YCbCr_is_delivered_as_RGB_and_RGB_as_it_is()
    {
        byte[] stream = JpegFixtures.Bytes("ycc420_q85_45x33.jpg");
        using var ycbcr = Strip(stream, 45, 33, 33, photometric: 6, samples: 3);
        using var rgb = Strip(stream, 45, 33, 33, photometric: 2, samples: 3);

        Assert.Equal(Decoded("ycc420_q85_45x33.jpg", 33), ycbcr.Primary.ReadRawPixels<byte>());
        Assert.Equal(TiffColorModel.Rgb, ycbcr.Primary.ColorModel);
        Assert.NotEqual(ycbcr.Primary.ReadRawPixels<byte>(), rgb.Primary.ReadRawPixels<byte>());   // no conversion
    }

    [Fact]
    public void A_last_strip_may_be_higher_than_its_rows_as_some_writers_make_it()
    {
        // The image has 20 rows, its single strip a frame of 29 (RowsPerStrip 29): the first 20 rows are the image.
        using var file = Strip(JpegFixtures.Bytes("grey_q90_37x29.jpg"), 37, 20, 29);

        Assert.Equal(Decoded("grey_q90_37x29.jpg", 20), file.Primary.ReadRawPixels<byte>());
    }

    [Theory]
    [InlineData(36, 29, 29)]   // the frame is wider than the strip
    [InlineData(38, 29, 29)]   // narrower
    [InlineData(37, 30, 30)]   // lower than its rows
    public void A_frame_that_does_not_fit_its_strip_is_damaged(int width, int height, int rowsPerStrip)
    {
        using var file = Strip(JpegFixtures.Bytes("grey_q90_37x29.jpg"), width, height, rowsPerStrip);

        Assert.Contains("JPEG", Assert.Throws<TiffException>(() => file.Primary.ReadRawPixels<byte>()).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_frame_higher_than_a_strip_that_is_not_the_last_is_damaged()
    {
        // Two strips of 28 rows, both holding the frame of 29 rows: the first may not be higher than its rows.
        byte[] stream = JpegFixtures.Bytes("grey_q90_37x29.jpg");
        var builder = new TiffBuilder { Data = stream };
        builder.AddDirectory().Image(37, 56).Remove(259).Add(259, TiffTagType.Short, 7).Remove(273).Remove(278).Remove(279)
            .Add(273, TiffTagType.Long, builder.DataOffset, builder.DataOffset).Add(278, TiffTagType.Long, 28)
            .Add(279, TiffTagType.Long, stream.Length, stream.Length).Sorted();
        using var file = TiffFile.Read(new MemoryStream(builder.Build()));

        Assert.Throws<TiffException>(() => file.Primary.ReadRawPixels<byte>());
        Assert.Equal(37 * 10, file.Primary.ReadRegion<byte>(0, 46, 37, 10).Length);   // the last strip, higher, is read
    }

    [Fact]
    public void Strips_share_the_tables_of_JPEGTables()
    {
        // The tables (DQT, DHT) of the stream moved to JPEGTables, as libtiff writes them.
        byte[] data = JpegFixtures.Bytes("grey_q90_37x29.jpg");
        var segments = new List<(byte Marker, int Offset, int Length)>();
        int p = 2;
        while (data[p + 1] != 0xDA)
        {
            int length = data[p + 2] << 8 | data[p + 3];
            segments.Add((data[p + 1], p, length + 2));
            p += length + 2;
        }

        byte[] tables = [0xFF, 0xD8, .. segments.Where(s => s.Marker is 0xDB or 0xC4).SelectMany(s => data.AsSpan(s.Offset, s.Length).ToArray()), 0xFF, 0xD9];
        byte[] stream = [0xFF, 0xD8, .. segments.Where(s => s.Marker is not (0xDB or 0xC4)).SelectMany(s => data.AsSpan(s.Offset, s.Length).ToArray()), .. data[p..]];
        using var file = Strip(stream, 37, 29, 29, tables);

        Assert.Equal(Decoded("grey_q90_37x29.jpg", 29), file.Primary.ReadRawPixels<byte>());
    }

    [Fact]
    public void Tiles_too_large_for_a_JPEG_frame_are_damaged()
    {
        // Found by fuzzing: a tile of 16 x 224 700 414 pixels, which no JPEG frame (at most 65535 x 65535) can fill.
        byte[] stream = JpegFixtures.Bytes("grey_q90_37x29.jpg");
        var builder = new TiffBuilder { Data = stream };
        builder.AddDirectory().Image(16, 16).Remove(259).Add(259, TiffTagType.Short, 7).Remove(273).Remove(278).Remove(279)
            .Add(322, TiffTagType.Short, 16).Add(323, TiffTagType.Long, 224_700_414)
            .Add(324, TiffTagType.Long, builder.DataOffset).Add(325, TiffTagType.Long, stream.Length).Sorted();
        using var file = TiffFile.Read(new MemoryStream(builder.Build()));

        Assert.Contains("cannot hold a JPEG frame", Assert.Throws<TiffException>(() => file.Primary.ReadRawPixels<byte>()).Message, StringComparison.Ordinal);
    }
}
