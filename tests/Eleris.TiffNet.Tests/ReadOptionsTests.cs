using Eleris.TiffNet.Internal;

namespace Eleris.TiffNet.Tests;

public class ReadOptionsTests
{
    [Fact]
    public void The_defaults_are_those_of_the_design()
    {
        var o = TiffReadOptions.Default;

        Assert.False(o.Strict);
        Assert.Equal(2_147_483_591L, o.MaxInputBytes);
        Assert.Equal(2_147_483_591L, o.MaxPixelBytes);
        Assert.Equal(10_000, o.MaxDirectories);
        Assert.Equal(4_096, o.MaxTagsPerDirectory);
        Assert.Equal(64L << 20, o.MaxTagBytes);
        Assert.Equal(100, o.MaxJpegScans);
        Assert.Equal(-1, o.MaxDegreeOfParallelism);
    }

    [Fact]
    public void A_limit_is_changed_with_a_with_expression()
    {
        var mobile = TiffReadOptions.Default with { MaxPixelBytes = 256L << 20, MaxDegreeOfParallelism = 2 };

        Assert.Equal(256L << 20, mobile.MaxPixelBytes);
        Assert.Equal(2, mobile.MaxDegreeOfParallelism);
        Assert.Equal(2_147_483_591L, TiffReadOptions.Default.MaxPixelBytes);
        Assert.NotEqual(TiffReadOptions.Default, mobile);
    }

    [Fact]
    public void Limits_must_be_positive()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => TiffReadOptions.Default with { MaxPixelBytes = 0 });
        Assert.Throws<ArgumentOutOfRangeException>(() => TiffReadOptions.Default with { MaxInputBytes = -1 });
        Assert.Throws<ArgumentOutOfRangeException>(() => TiffReadOptions.Default with { MaxDirectories = 0 });
        Assert.Throws<ArgumentOutOfRangeException>(() => TiffReadOptions.Default with { MaxTagsPerDirectory = -5 });
        Assert.Throws<ArgumentOutOfRangeException>(() => TiffReadOptions.Default with { MaxTagBytes = 0 });
        Assert.Throws<ArgumentOutOfRangeException>(() => TiffReadOptions.Default with { MaxJpegScans = 0 });
        Assert.Throws<ArgumentOutOfRangeException>(() => TiffReadOptions.Default with { MaxDegreeOfParallelism = 0 });
        Assert.Throws<ArgumentOutOfRangeException>(() => TiffReadOptions.Default with { MaxDegreeOfParallelism = -2 });
    }
}

public class ResourceBudgetTests
{
    private static ResourceBudget Budget(TiffReadOptions? options = null) => new(options ?? TiffReadOptions.Default, "m42.tif");

    [Fact]
    public void Pixel_memory_is_counted_until_the_limit()
    {
        var budget = Budget(TiffReadOptions.Default with { MaxPixelBytes = 1000 });

        budget.ReservePixels(600, "the image");
        budget.ReservePixels(400, "the coefficients");
        var ex = Assert.Throws<TiffException>(() => budget.ReservePixels(1, "one more byte"));

        Assert.Equal("MaxPixelBytes", ex.Limit);
        Assert.StartsWith("m42.tif: one more byte needs 1 bytes, beyond the 0 bytes left", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Sizes_in_messages_do_not_depend_on_the_culture()
    {
        var previous = System.Globalization.CultureInfo.CurrentCulture;
        System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("fr-FR");
        try
        {
            var budget = Budget(TiffReadOptions.Default with { MaxPixelBytes = 1L << 30 });
            var ex = Assert.Throws<TiffException>(() => budget.ReservePixels(3L << 29, "the image"));

            Assert.Contains("needs 1.5 GB (1610612736 bytes), beyond the 1 GB (1073741824 bytes) left", ex.Message, StringComparison.Ordinal);
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void A_refused_reservation_is_not_counted()
    {
        var budget = Budget(TiffReadOptions.Default with { MaxPixelBytes = 1000 });

        Assert.Throws<TiffException>(() => budget.ReservePixels(5000, "a huge image"));
        budget.ReservePixels(1000, "an image that fits");
    }

    [Fact]
    public void Reservations_that_overflow_are_refused()
    {
        var budget = Budget();

        budget.ReservePixels(1L << 30, "half");
        Assert.Throws<TiffException>(() => budget.ReservePixels(long.MaxValue, "everything"));
    }

    [Fact]
    public void A_refused_reservation_does_not_make_a_concurrent_one_fail()
    {
        var budget = Budget(TiffReadOptions.Default with { MaxPixelBytes = 1_000_000 });
        int refusedSmall = 0;

        Parallel.For(0, 20_000, new ParallelOptions { MaxDegreeOfParallelism = 8 }, i =>
        {
            try
            {
                budget.ReservePixels(i % 2 == 0 ? 10_000_000 : 1, i % 2 == 0 ? "a huge image" : "a byte");
            }
            catch (TiffException) when (i % 2 == 1)
            {
                Interlocked.Increment(ref refusedSmall);
            }
            catch (TiffException)
            {
            }
        });

        Assert.Equal(0, refusedSmall);   // 10 000 single bytes always fit in 1 000 000, whatever the huge ones do
    }

    [Fact]
    public void Reservations_from_parallel_decoders_are_all_counted()
    {
        var budget = Budget(TiffReadOptions.Default with { MaxPixelBytes = 100_000 });
        int refused = 0;

        Parallel.For(0, 1000, new ParallelOptions { MaxDegreeOfParallelism = 8 }, _ =>
        {
            try
            {
                budget.ReservePixels(200, "a strip");
            }
            catch (TiffException)
            {
                Interlocked.Increment(ref refused);
            }
        });

        Assert.Equal(500, refused);   // exactly 500 strips of 200 bytes fit in 100 000
    }

    [Fact]
    public void Each_limit_is_named()
    {
        var budget = Budget(TiffReadOptions.Default with { MaxDirectories = 2, MaxTagsPerDirectory = 10, MaxTagBytes = 100, MaxJpegScans = 5 });

        budget.CountDirectory();
        budget.CountDirectory();
        Assert.Equal("MaxDirectories", Assert.Throws<TiffException>(budget.CountDirectory).Limit);

        budget.CheckTagsPerDirectory(10);
        Assert.Equal("MaxTagsPerDirectory", Assert.Throws<TiffException>(() => budget.CheckTagsPerDirectory(11)).Limit);

        budget.CheckTagBytes(34675, 100);
        Assert.Equal("MaxTagBytes", Assert.Throws<TiffException>(() => budget.CheckTagBytes(34675, 101)).Limit);

        budget.CheckJpegScans(5);
        Assert.Equal("MaxJpegScans", Assert.Throws<TiffException>(() => budget.CheckJpegScans(6)).Limit);
    }

    [Fact]
    public void A_cancelled_read_stops()
    {
        using var source = new CancellationTokenSource();
        var budget = new ResourceBudget(TiffReadOptions.Default, "m42.tif", source.Token);

        budget.ThrowIfCancelled();
        source.Cancel();

        Assert.Throws<OperationCanceledException>(budget.ThrowIfCancelled);
    }

    [Fact]
    public void Messages_name_the_file_by_its_name_only()
    {
        string path = Path.Combine(Path.GetTempPath(), "private", "observations", "m42.tif");
        var budget = new ResourceBudget(TiffReadOptions.Default, ResourceBudget.NameOf(path));

        var ex = budget.Invalid("the directory is truncated.");

        Assert.Equal("m42.tif: the directory is truncated.", ex.Message);
        Assert.DoesNotContain("private", ex.Message, StringComparison.Ordinal);
        Assert.Null(ex.Limit);
        Assert.Equal("stream", ResourceBudget.NameOf(null));
        // A path given by a caller (FileStream.Name, for example) is reduced to the file name too.
        Assert.Equal("m42.tif", new ResourceBudget(TiffReadOptions.Default, path).SourceName);
    }
}
