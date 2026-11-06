using Eleris.TiffNet.Internal.Writing;

namespace Eleris.TiffNet.Tests.Writing;

/// <summary>Writing band after band: the file does not depend on the threads, bands end on source blocks, failures stop cleanly.</summary>
public class BandWritingTests
{
    [Theory]
    [InlineData(TiffPixelOrder.Planar)]
    [InlineData(TiffPixelOrder.Interleaved)]
    public void A_file_of_several_bands_does_not_depend_on_the_number_of_threads(TiffPixelOrder order)
    {
        // Strips of 64 rows per plane, bands of 16 strips: three bands, the last one partial.
        var image = Gradient(4096, 2100, 3);
        var options = TiffWriteOptions.Default with { PlanarConfiguration = order, Level = TiffCompressionLevel.Fastest };

        byte[] one = Bytes(image, options with { MaxDegreeOfParallelism = 1 });
        byte[] two = Bytes(image, options with { MaxDegreeOfParallelism = 2 });
        byte[] eight = Bytes(image, options with { MaxDegreeOfParallelism = 8 });

        Assert.Equal(one, two);
        Assert.Equal(one, eight);
        using var read = TiffFile.Read(new MemoryStream(one));
        Assert.Equal(image.ReadRawPixels<byte>(), read.Primary!.ReadRawPixels<byte>());
    }

    [Theory]
    [InlineData(256, 1, 256)]      // no source block: the target
    [InlineData(256, 100, 200)]    // short source strips: a multiple of them
    [InlineData(256, 300, 150)]    // long source strips: a divisor of them
    [InlineData(256, 257, 256)]    // a prime: no divisor near the target
    [InlineData(256, 5000, 256)]   // the whole image: nothing to align on
    public void Output_strips_are_aligned_on_the_source_when_they_can_be(int target, long source, int expected)
    {
        Assert.Equal(expected, ImageEncoder.StripRows(target, source, 5000));
    }

    [Theory]
    [InlineData(TiffLayout.Strips)]
    [InlineData(TiffLayout.Tiles)]
    public void Bands_end_on_the_compressed_strips_of_the_source(TiffLayout layout)
    {
        // Strips of 262 rows in the source (256 KB of 1000 bytes).
        string path = Path.Combine(Path.GetTempPath(), $"tiffnet-band-{Guid.NewGuid():N}.tif");
        try
        {
            new TiffFile().Add(Gradient(1000, 3000, 1)).Save(path);
            using var file = TiffFile.Open(path);
            var source = file.Primary!;
            Assert.Equal(262, source.Storage!.RowsPerStrip);

            var encoder = new ImageEncoder(source, TiffWriteOptions.Default with { Layout = layout, TileSize = 128 });
            int band = encoder.BandRows();

            Assert.Equal(0, band % encoder.BlockHeight);
            if (layout == TiffLayout.Strips)
            {
                // The output strips are aligned on the source, and so are the bands.
                Assert.True(band % 262 == 0 || band >= 3000, $"a band of {band} rows splits the source strips of 262 rows");
            }
            else
            {
                // Tiles of 128 rows cannot be: a band covers 8 source strips at least, so that few are decoded twice.
                Assert.True(band >= 8 * 262 || band >= 3000, $"a band of {band} rows covers too few source strips");
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Writing_from_a_file_gives_its_pixels_whatever_the_bands()
    {
        string path = Path.Combine(Path.GetTempPath(), $"tiffnet-band-{Guid.NewGuid():N}.tif");
        try
        {
            var image = Gradient(1000, 3000, 1);
            new TiffFile().Add(image).Save(path);
            using var file = TiffFile.Open(path);
            foreach (var options in new[]
            {
                TiffWriteOptions.Default with { Compression = TiffCompression.Lzw },
                TiffWriteOptions.Default with { Layout = TiffLayout.Tiles, TileSize = 128, MaxDegreeOfParallelism = 3 },
            })
            {
                using var read = TiffFile.Read(new MemoryStream(Bytes(file.Primary!, options)));
                Assert.Equal(image.ReadRawPixels<byte>(), read.Primary!.ReadRawPixels<byte>());
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Many_writes_at_once_do_not_wait_for_the_thread_pool()
    {
        // Every pool thread runs a write: each must go on at the pace of its own thread rather than wait for helpers.
        int writes = Environment.ProcessorCount * 4;
        var all = Enumerable.Range(0, writes).Select(w => Task.Run(() =>
        {
            int sum = 0;
            BlockPipeline.Run(64, 8, block =>
            {
                Thread.SpinWait(20_000);
                return block;
            }, (_, block) => sum += block, _ => { }, CancellationToken.None);
            return sum;
        })).ToArray();

        int[] sums = await Task.WhenAll(all).WaitAsync(TimeSpan.FromSeconds(60));
        Assert.All(sums, sum => Assert.Equal(64 * 63 / 2, sum));
    }

    [Fact]
    public void A_failed_write_leaves_the_writer_faulted()
    {
        using var output = new FailingStream(100_000);
        using var writer = TiffImageWriter.Create(output, 1000, 2000, TiffSampleType.UInt8,
            options: TiffWriteOptions.Default with { Compression = TiffCompression.None, MaxDegreeOfParallelism = 1 });
        byte[] rows = new byte[1000 * 100];

        Assert.Throws<IOException>(() =>
        {
            for (int i = 0; i < 20; i++)
            {
                writer.WriteRows<byte>(rows, 100);
            }
        });

        Assert.Throws<InvalidOperationException>(() => writer.WriteRows<byte>(rows, 100));
        Assert.Throws<InvalidOperationException>(writer.Complete);
        writer.Dispose();
    }

    [Fact]
    public void A_stream_that_cannot_be_written_is_refused_before_the_task()
    {
        var file = new TiffFile().Add(Gradient(10, 10, 1));
        using var stream = new MemoryStream(new byte[10], writable: false);

        Task? task = null;
        Assert.Throws<ArgumentException>(() => { task = file.SaveAsync(stream); });
        Assert.Null(task);
    }

    internal static TiffImage Gradient(int width, int height, int channels)
    {
        byte[] values = new byte[width * height * channels];
        for (int i = 0; i < values.Length; i++)
        {
            values[i] = (byte)(i / channels % width + i / (width * channels) * 3 + i % channels * 50);
        }

        return TiffImage.Create<byte>(values, width, height, channels, channels >= 3 ? TiffColorModel.Rgb : null);
    }

    private static byte[] Bytes(TiffImage image, TiffWriteOptions options)
    {
        using var stream = new MemoryStream();
        new TiffFile().Add(image).Save(stream, options);
        return stream.ToArray();
    }

    /// <summary>A stream that fails once more than a number of bytes are written to it.</summary>
    private sealed class FailingStream(long limit) : MemoryStream
    {
        public override void Write(byte[] buffer, int offset, int count)
        {
            Check(count);
            base.Write(buffer, offset, count);
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            Check(buffer.Length);
            base.Write(buffer);
        }

        private void Check(int count)
        {
            if (Position + count > limit)
            {
                throw new IOException("The disk is full.");
            }
        }
    }
}
