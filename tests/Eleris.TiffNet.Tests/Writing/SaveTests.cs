using System.Buffers.Binary;
using Eleris.TiffNet.Tests.Fixtures;

namespace Eleris.TiffNet.Tests.Writing;

public class SaveTests
{
    /// <summary>A path in a folder of its own, where the temporary file of a save is made: other tests save at the same time.</summary>
    internal static string Temporary()
    {
        string folder = Path.Combine(Path.GetTempPath(), $"tiffnet-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        return Path.Combine(folder, "image.tif");
    }

    internal static void Delete(string path) => Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);

    private static byte[] SaveToBytes(TiffFile file, TiffWriteOptions? options = null)
    {
        using var stream = new MemoryStream();
        file.Save(stream, options);
        return stream.ToArray();
    }

    private static TiffFile Reload(byte[] bytes) => TiffFile.Read(new MemoryStream(bytes));

    private static TiffImage Sample<T>(int width, int height, int channels, int seed) where T : unmanaged, System.Numerics.INumber<T> =>
        DirectoryWriterTests.Sample<T>(width, height, channels, seed);

    [Fact]
    public void A_file_with_no_page_cannot_be_saved()
    {
        Assert.Throws<InvalidOperationException>(() => SaveToBytes(new TiffFile()));
        Assert.Throws<InvalidOperationException>(() => new TiffFile().Primary);
    }

    [Fact]
    public void A_read_file_is_saved_again_with_its_pixels_and_extra_channels()
    {
        using var file = TiffFile.Open(DomainFixtures.PathOf("rgba_unassalpha.tif"));

        using var back = Reload(SaveToBytes(file));

        Assert.Equal(file.Primary.ReadRawPixels<ushort>(), back.Primary.ReadRawPixels<ushort>());
        Assert.Equal(file.Primary.ExtraChannels, back.Primary.ExtraChannels);
    }

    [Fact]
    public void Saving_over_the_file_that_was_read_replaces_it()
    {
        string path = Temporary();
        File.Copy(DomainFixtures.PathOf("siril_m42_grey16_astro.tif"), path);
        try
        {
            ushort[] expected;
            using (var file = TiffFile.Open(path))
            {
                expected = file.Primary.ReadRawPixels<ushort>();
                file.Save(path, TiffWriteOptions.Default with { Compression = TiffCompression.Lzw });
            }

            using var back = TiffFile.Open(path);
            Assert.Equal(TiffCompression.Lzw, back.Primary.Storage!.Compression);
            Assert.Equal(expected, back.Primary.ReadRawPixels<ushort>());
            Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(path)!, ".tiff-*.tmp*"));
        }
        finally
        {
            Delete(path);
        }
    }

    [Fact]
    public void A_failed_save_leaves_the_target_and_no_temporary_file()
    {
        string path = Temporary();
        File.WriteAllBytes(path, [1, 2, 3]);
        try
        {
            // A page whose pixels cannot be read: the save fails after it started writing.
            using var complex = TiffFile.Open(DomainFixtures.PathOf("complex64.tif"));
            var file = new TiffFile().Add(Sample<byte>(4, 4, 1, 1)).Add(complex.Primary);

            Assert.Throws<NotSupportedException>(() => file.Save(path));
            Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(path));
            Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(path)!, ".tiff-*.tmp*"));
        }
        finally
        {
            Delete(path);
        }
    }

    /// <summary>A stream that only writes forward, as a network or a pipe.</summary>
    private sealed class ForwardOnly(Stream inner) : Stream
    {
        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override void Flush() => inner.Flush();

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => inner.Write(buffer, offset, count);
    }

    [Fact]
    public void A_stream_that_cannot_seek_is_written_through_a_temporary_file()
    {
        var image = Sample<ushort>(40, 30, 1, 5);
        using var target = new MemoryStream();

        new TiffFile().Add(image).Save(new ForwardOnly(target));

        using var back = Reload(target.ToArray());
        Assert.Equal(image.ReadRawPixels<ushort>(), back.Primary.ReadRawPixels<ushort>());
    }

    [Fact]
    public void A_stream_is_written_from_its_position_and_left_open()
    {
        var image = Sample<byte>(8, 8, 1, 6);
        using var stream = new MemoryStream();
        stream.Write([9, 9, 9]);

        new TiffFile().Add(image).Save(stream);

        Assert.True(stream.CanWrite);
        using var back = Reload(stream.ToArray()[3..]);
        Assert.Equal(image.ReadRawPixels<byte>(), back.Primary.ReadRawPixels<byte>());
    }

    [Fact]
    public async Task SaveAsync_writes_the_same_file_and_can_be_cancelled()
    {
        var file = new TiffFile().Add(Sample<float>(64, 64, 3, 7));
        using var stream = new MemoryStream();

        await file.SaveAsync(stream);

        Assert.Equal(SaveToBytes(file), stream.ToArray());
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => file.SaveAsync(new MemoryStream(), cancellationToken: cancelled.Token));
    }

    [Fact]
    public void Pages_can_be_inserted_and_removed()
    {
        var a = Sample<byte>(2, 2, 1, 1);
        var b = Sample<byte>(3, 3, 1, 2);
        var c = Sample<byte>(4, 4, 1, 3);
        var file = new TiffFile().Add(a).Add(c).Insert(1, b);

        Assert.Equal([a, b, c], file);
        Assert.True(file.Remove(b));
        file.RemoveAt(0);
        Assert.Equal([c], file);
    }
}
