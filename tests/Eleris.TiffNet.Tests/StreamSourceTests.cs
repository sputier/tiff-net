using Eleris.TiffNet.Tests.Fixtures;
using Eleris.TiffNet.Tests.Writing;

namespace Eleris.TiffNet.Tests;

/// <summary>Files opened from a stream of the application or read from its memory, without a copy of their bytes.</summary>
[Collection(nameof(Measurement))]
public class StreamSourceTests
{
    [Fact]
    public void A_file_stream_is_read_through_its_handle_from_its_position_on_every_core()
    {
        var image = BandWritingTests.Gradient(2000, 1500, 3);
        byte[] tiff = Bytes(image);
        string path = SaveTests.Temporary();
        try
        {
            // Some bytes before the TIFF data: the file starts where the stream stands.
            File.WriteAllBytes(path, [.. new byte[100], .. tiff]);
            var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            stream.Position = 100;
            using (var file = TiffFile.Open(stream, TiffReadOptions.Default with { MaxDegreeOfParallelism = 4 }))
            {
                Assert.Equal(image.ReadRawPixels<byte>(), file.Primary.ReadRawPixels<byte>());
            }

            Assert.False(stream.CanRead);   // disposed with the file
        }
        finally
        {
            SaveTests.Delete(path);
        }
    }

    [Fact]
    public void Other_streams_that_seek_are_read_one_read_at_a_time_and_can_be_left_open()
    {
        var image = BandWritingTests.Gradient(300, 200, 1);
        var stream = new MemoryStream(Bytes(image));
        using (var file = TiffFile.Open(stream, leaveOpen: true))
        {
            Assert.Equal(image.ReadRawPixels<byte>(), file.Primary.ReadRawPixels<byte>());
            Assert.Equal(image.ReadRawPixels<byte>().Skip(300 * 5 + 7).Take(10), file.Primary.ReadRawRegion<byte>(7, 5, 10, 1));
        }

        Assert.True(stream.CanRead);
        Assert.Throws<ArgumentException>(() => TiffFile.Open(new NonSeekable(stream)));
    }

    [Fact]
    public void Bytes_in_memory_are_read_without_a_copy()
    {
        var image = BandWritingTests.Gradient(2000, 1500, 1);
        byte[] tiff = Bytes(image, TiffCompression.None);

        GC.Collect();
        long before = GC.GetTotalAllocatedBytes(precise: true);
        using var file = TiffFile.Read(tiff.AsMemory());
        long allocated = GC.GetTotalAllocatedBytes(precise: true) - before;

        Assert.True(allocated < tiff.Length / 10, $"{allocated} bytes allocated to open {tiff.Length} bytes");
        Assert.Equal(image.ReadRawPixels<byte>(), file.Primary.ReadRawPixels<byte>());
    }

    private static byte[] Bytes(TiffImage image, TiffCompression compression = TiffCompression.Deflate)
    {
        using var stream = new MemoryStream();
        new TiffFile().Add(image).Save(stream, TiffWriteOptions.Default with { Compression = compression });
        return stream.ToArray();
    }

    private sealed class NonSeekable(Stream inner) : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
