using Eleris.TiffNet.Internal;
using Eleris.TiffNet.Internal.Jpeg;

namespace Eleris.TiffNet.Tests.Jpeg;

public class ProgressiveTests
{
    /// <summary>The scans of a stream: (offset of the SOS marker, length up to the next scan or the end of the image).</summary>
    private static List<(int Offset, int Length)> Scans(byte[] data)
    {
        var starts = new List<int>();
        for (int i = 0; i + 1 < data.Length; i++)
        {
            if (data[i] == 0xFF && data[i + 1] == 0xDA)
            {
                starts.Add(i);
            }
        }

        int end = data.Length - 2;   // before EOI
        return [.. starts.Select((s, i) => (s, (i + 1 < starts.Count ? starts[i + 1] : end) - s))];
    }

    /// <summary>A scan as written, with what precedes it up to the previous scan (DHT segments of optimised streams).</summary>
    private static byte[] WithScan(byte[] data, int index, int times)
    {
        var scans = Scans(data);
        var (offset, length) = scans[index];
        byte[] scan = data.AsSpan(offset, length).ToArray();
        int after = offset + length;
        return [.. data[..after], .. Enumerable.Repeat(scan, times).SelectMany(s => s), .. data[after..]];
    }

    [Fact]
    public void A_scan_that_refines_nothing_is_refused()
    {
        // The first scan (the first bits of DC) repeated: it adds no bit, only time.
        byte[] data = JpegFixtures.Bytes("prog_grey_q90_61x47.jpg");

        Assert.Contains("refines no coefficient", Assert.Throws<InvalidDataException>(() => JpegFixtures.Decode(WithScan(data, 0, 1))).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Scans_beyond_MaxJpegScans_are_refused()
    {
        byte[] data = JpegFixtures.Bytes("prog_ycc444_q85_53x31.jpg");
        int scans = Scans(data).Count;

        Assert.Equal("MaxJpegScans", Assert.Throws<TiffException>(() =>
            JpegFixtures.Decode(data, TiffReadOptions.Default with { MaxJpegScans = scans - 1 })).Limit);
        Assert.NotNull(JpegFixtures.Decode(data, TiffReadOptions.Default with { MaxJpegScans = scans }));
    }

    [Theory]
    [InlineData(0, 5, 0, 0)]    // DC with an AC band
    [InlineData(5, 3, 0, 0)]    // an empty band
    [InlineData(1, 64, 0, 0)]   // beyond the block
    [InlineData(1, 5, 3, 1)]    // a refinement of more than one bit
    [InlineData(1, 5, 0, 14)]   // Al beyond 13
    public void Scan_parameters_libjpeg_refuses_are_damaged_data(byte ss, byte se, int ah, int al)
    {
        byte[] data = JpegFixtures.Bytes("prog_grey_q90_61x47.jpg");
        var (offset, _) = Scans(data)[1];
        int count = data[offset + 4];
        int at = offset + 5 + 2 * count;
        (data[at], data[at + 1], data[at + 2]) = (ss, se, (byte)(ah << 4 | al));

        Assert.Throws<InvalidDataException>(() => JpegFixtures.Decode(data));
    }

    [Fact]
    public void The_coefficients_are_counted_in_the_budget_before_they_are_allocated()
    {
        // 3 bytes per sample (the plane and 2 bytes of coefficients) for a progressive image, 1 for a sequential one.
        byte[] data = JpegFixtures.Bytes("prog_grey_q100_64x64.jpg");
        var budget = TiffReadOptions.Default with { MaxPixelBytes = 64 * 64 * 3 - 1 };

        Assert.Equal("MaxPixelBytes", Assert.Throws<TiffException>(() => JpegFixtures.Decode(data, budget)).Limit);
        Assert.NotNull(JpegFixtures.Decode(data, TiffReadOptions.Default with { MaxPixelBytes = 64 * 64 * 3 }));
    }

    [Fact]
    public void A_progressive_frame_in_tables_is_refused()
    {
        byte[] tables = [0xFF, 0xD8, 0xFF, 0xC2, 0, 11, 8, 0, 8, 0, 8, 1, 1, 0x11, 0, 0xFF, 0xD9];

        Assert.Throws<InvalidDataException>(() => new JpegDecoder(new ResourceBudget(TiffReadOptions.Default, "t")).ReadTables(tables));
    }
}
