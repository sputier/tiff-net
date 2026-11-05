using Xunit.Sdk;

namespace Eleris.TiffNet.Tests.Fixtures;

// The assertion the readers will be held to: it must accept a quick, frugal failure of the expected kind and reject everything
// else. The hostile files themselves are read by the tests of the readers.
[Collection(nameof(Measurement))]
public class HostileAssertTests
{
    private static readonly TimeSpan Second = TimeSpan.FromSeconds(1);
    private const long Megabytes64 = 64L << 20;

    [Fact]
    public void A_quick_frugal_failure_of_the_expected_kind_passes()
    {
        HostileAssert.FailsWithinBounds(() => throw new TiffException("damaged"), "TiffException", Second, Megabytes64, "case");
        HostileAssert.FailsWithinBounds(() => throw new TiffException("too large", "MaxPixelBytes"), "TiffException:MaxPixelBytes",
            Second, Megabytes64, "case");
        HostileAssert.FailsWithinBounds(() => throw new NotSupportedException("palette"), "NotSupportedException", Second, Megabytes64,
            "case");
        HostileAssert.FailsWithinBounds(() => throw new TiffException("big", "MaxJpegScans"), "TiffException:*", Second, Megabytes64, "case");
        HostileAssert.FailsWithinBounds(() => throw new TiffException("damaged"), "TiffException:*", Second, Megabytes64, "case");
        HostileAssert.FailsWithinBounds(() => throw new NotSupportedException("CMYK"), "TiffException:*|NotSupportedException", Second,
            Megabytes64, "case");
    }

    [Fact]
    public void A_damaged_file_is_not_a_file_too_large()
    {
        // "TiffException" alone means damaged: a refusal for a limit is a different outcome.
        var ex = Assert.Throws<XunitException>(() => HostileAssert.FailsWithinBounds(
            () => throw new TiffException("big", "MaxPixelBytes"), "TiffException", Second, Megabytes64, "case"));
        Assert.Contains("TiffException:MaxPixelBytes", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Success_is_a_failure()
    {
        var ex = Assert.Throws<XunitException>(() => HostileAssert.FailsWithinBounds(() => { }, "TiffException", Second, Megabytes64, "case"));
        Assert.Contains("no exception", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Another_exception_is_a_failure()
    {
        var ex = Assert.Throws<XunitException>(() => HostileAssert.FailsWithinBounds(
            () => throw new InvalidDataException("not a TiffException"), "TiffException", Second, Megabytes64, "case"));
        Assert.Contains("InvalidDataException", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_wrong_limit_is_a_failure()
    {
        Assert.Throws<XunitException>(() => HostileAssert.FailsWithinBounds(
            () => throw new TiffException("damaged"), "TiffException:MaxPixelBytes", Second, Megabytes64, "case"));
        Assert.Throws<XunitException>(() => HostileAssert.FailsWithinBounds(
            () => throw new TiffException("too many", "MaxJpegScans"), "TiffException:MaxPixelBytes", Second, Megabytes64, "case"));
    }

    [Fact]
    public void Too_slow_is_a_failure()
    {
        var ex = Assert.Throws<XunitException>(() => HostileAssert.FailsWithinBounds(() =>
        {
            Thread.Sleep(200);
            throw new TiffException("damaged");
        }, "TiffException", TimeSpan.FromMilliseconds(50), Megabytes64, "case"));
        Assert.Contains("failed after", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Allocating_too_much_is_a_failure_even_on_another_thread()
    {
        var ex = Assert.Throws<XunitException>(() => HostileAssert.FailsWithinBounds(() =>
        {
            Task.Run(() => GC.KeepAlive(new byte[100 << 20])).Wait();
            throw new TiffException("damaged");
        }, "TiffException", Second * 10, Megabytes64, "case"));
        Assert.Contains("MB allocated", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_hostile_file_is_described_with_bounds()
    {
        var cases = HostileAssert.Cases;

        Assert.Equal(35, cases.Count);   // 20 from the review, 15 crafted by tools/make_hostile.py
        Assert.All(cases, c =>
        {
            Assert.True(File.Exists(c.Path), $"{c} is missing");
            Assert.Matches("^SEC-0[1-6]$", c.Finding);
            Assert.All(c.Outcome.Split('|'), o => Assert.Matches(@"^(TiffException(:(Max[A-Za-z]+|\*))?|NotSupportedException)$", o));
            Assert.InRange(c.MaxTime, TimeSpan.FromMilliseconds(100), TimeSpan.FromSeconds(10));
            Assert.InRange(c.MaxAllocatedBytes, 1L << 20, 256L << 20);
        });
        foreach (string finding in new[] { "SEC-01", "SEC-02", "SEC-03", "SEC-04", "SEC-05", "SEC-06" })
        {
            Assert.Contains(cases, c => c.Finding == finding);
        }

        // Files that are only too large for a small device are read with its limit; the default limit is tested too.
        Assert.Contains(cases, c => c.Outcome == "TiffException:MaxPixelBytes" && c.Options.MaxPixelBytes == 64L << 20);
        Assert.Contains(cases, c => c.Outcome == "TiffException:MaxPixelBytes" && c.Options == TiffReadOptions.Default);
    }
}
