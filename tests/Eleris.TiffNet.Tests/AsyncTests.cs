using System.Collections.Concurrent;
using Eleris.TiffNet.Tests.Writing;

namespace Eleris.TiffNet.Tests;

/// <summary>Opening, reading and saving asynchronously, with progress and cancellation.</summary>
public class AsyncTests
{
    [Fact]
    public async Task Reads_report_their_progress_up_to_the_whole()
    {
        string path = SaveTests.Temporary();
        try
        {
            var image = BandWritingTests.Gradient(1000, 900, 3);
            await new TiffFile().Add(image).SaveAsync(path, TiffWriteOptions.Default with { Layout = TiffLayout.Tiles, TileSize = 64 });
            using var file = await TiffFile.OpenAsync(path);

            var whole = new Reports();
            byte[] pixels = await file.Primary.ReadPixelsAsync<byte>(progress: whole);
            var region = new Reports();
            byte[] part = await file.Primary.ReadRegionAsync<byte>(100, 100, 300, 200, progress: region);
            var channel = new Reports();
            byte[] green = await file.Primary.ReadChannelAsync<byte>(1, progress: channel);

            Assert.Equal(image.ReadRawPixels<byte>(), pixels);
            Assert.Equal(image.ReadRegion<byte>(100, 100, 300, 200), part);
            Assert.Equal(image.ReadChannel<byte>(1), green);
            foreach (var reports in new[] { whole, region, channel })
            {
                reports.AssertComplete();
            }
        }
        finally
        {
            SaveTests.Delete(path);
        }
    }

    [Fact]
    public async Task Saves_report_their_progress_and_leave_the_target_when_cancelled()
    {
        string path = SaveTests.Temporary();
        File.WriteAllBytes(path, [1, 2, 3]);
        try
        {
            var file = new TiffFile().Add(BandWritingTests.Gradient(800, 700, 1)).Add(BandWritingTests.Gradient(300, 200, 3));
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => file.SaveAsync(path, cancellationToken: cancelled.Token));
            Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(path));

            var toPath = new Reports();
            await file.SaveAsync(path, TiffWriteOptions.Default with { Layout = TiffLayout.Tiles, TileSize = 32 }, toPath);
            var toStream = new Reports();
            using var stream = new MemoryStream();
            await file.SaveAsync(stream, progress: toStream);

            toPath.AssertComplete();
            toStream.AssertComplete();
            using var back = await TiffFile.OpenAsync(path);
            Assert.Equal(2, back.Count);
        }
        finally
        {
            SaveTests.Delete(path);
        }
    }

    [Fact]
    public async Task Opening_asynchronously_reports_failures_by_the_task()
    {
        string path = SaveTests.Temporary();
        File.WriteAllBytes(path, [1, 2, 3, 4, 5, 6, 7, 8, 9]);
        try
        {
            Assert.Throws<ArgumentException>(() => { _ = TiffFile.OpenAsync(""); });
            await Assert.ThrowsAsync<TiffException>(() => TiffFile.OpenAsync(path));
        }
        finally
        {
            SaveTests.Delete(path);
        }
    }

    /// <summary>The fractions reported, in the order they came (from any thread).</summary>
    private sealed class Reports : IProgress<double>
    {
        private readonly ConcurrentQueue<double> _values = new();

        public void Report(double value) => _values.Enqueue(value);

        public void AssertComplete()
        {
            double[] values = [.. _values];
            Assert.NotEmpty(values);
            Assert.Equal(1.0, values[^1]);
            Assert.Equal(values.Order(), values);   // increasing, never back
            Assert.Equal(values.Length, values.Distinct().Count());
            Assert.All(values, v => Assert.InRange(v, 0.0, 1.0));
            Assert.True(values.Length <= 101, $"{values.Length} reports");
        }
    }
}
