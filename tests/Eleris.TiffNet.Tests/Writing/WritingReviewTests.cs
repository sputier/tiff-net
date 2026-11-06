using System.Diagnostics;
using Eleris.TiffNet.Internal.Codecs;
using Eleris.TiffNet.Tests.Fixtures;

namespace Eleris.TiffNet.Tests.Writing;

/// <summary>The writer against the cases its review found: sources decoded once, bounds, saves that race with changes.</summary>
[Collection(nameof(Measurement))]
public class WritingReviewTests
{
    private static byte[] Save(TiffFile file, TiffWriteOptions? options = null)
    {
        using var stream = new MemoryStream();
        file.Save(stream, options);
        return stream.ToArray();
    }

    [Theory]
    [InlineData(1)]   // one compressed strip: each output strip used to decode it again
    [InlineData(6)]   // stored on its side: each output strip used to decode every source strip
    public void A_source_is_decoded_once_whatever_its_blocks_and_orientation(int orientation)
    {
        // 1000 x 800 u16 in one Deflate strip (or several strips on their side).
        var image = DirectoryWriterTests.Sample<ushort>(1000, 800, 1, 4);
        byte[] source = Save(new TiffFile().Add(image), TiffWriteOptions.Default with { Layout = TiffLayout.Tiles, TileSize = 1008 });
        if (orientation != 1)
        {
            source = WithOrientation(Save(new TiffFile().Add(image)), orientation);
        }

        using var file = TiffFile.Read(new MemoryStream(source));
        var read = Stopwatch.StartNew();
        _ = file.Primary.ReadRawPixels<ushort>();
        var once = read.Elapsed;

        GC.Collect();
        long before = GC.GetTotalAllocatedBytes(precise: true);
        var save = Stopwatch.StartNew();
        byte[] written = Save(file, TiffWriteOptions.Default with { Compression = TiffCompression.None, MaxDegreeOfParallelism = 1 });
        long allocated = GC.GetTotalAllocatedBytes(precise: true) - before;

        Assert.True(save.Elapsed < once * 8 + TimeSpan.FromMilliseconds(200), $"saved in {save.Elapsed.TotalMilliseconds} ms, read in {once.TotalMilliseconds} ms");
        Assert.True(allocated < 12L * 1000 * 800 * 2, $"{allocated} bytes allocated");
        using var back = TiffFile.Read(new MemoryStream(written));
        Assert.Equal(file.Primary.ReadRawPixels<ushort>(), back.Primary.ReadRawPixels<ushort>());
    }

    /// <summary>The same file with another Orientation tag (in place: the tag holds a SHORT inline).</summary>
    private static byte[] WithOrientation(byte[] file, int orientation)
    {
        uint ifd = BitConverter.ToUInt32(file, 4);
        int count = BitConverter.ToUInt16(file, (int)ifd);
        for (int i = 0; i < count; i++)
        {
            int entry = (int)ifd + 2 + i * 12;
            if (BitConverter.ToUInt16(file, entry) == 274)
            {
                file[entry + 8] = (byte)orientation;
            }
        }

        return file;
    }

    [Fact]
    public void The_bound_of_Deflate_holds_at_the_fastest_level_on_incompressible_data()
    {
        byte[] noise = new byte[256 * 1024];
        new Random(5).NextBytes(noise);

        foreach (var level in new[] { TiffCompressionLevel.Fastest, TiffCompressionLevel.Optimal, TiffCompressionLevel.SmallestSize })
        {
            Assert.True(DeflateEncoder.Encode(noise, level).Length <= DeflateEncoder.MaxEncoded(noise.Length), level.ToString());
        }
    }

    [Fact]
    public async Task SaveAsync_writes_the_pages_of_the_call_and_copies_into_a_stream_that_cannot_seek()
    {
        var a = DirectoryWriterTests.Sample<float>(400, 300, 3, 1);
        var b = DirectoryWriterTests.Sample<byte>(10, 10, 1, 2);
        var file = new TiffFile().Add(a).Add(b);
        using var target = new MemoryStream();

        var saving = file.SaveAsync(new AsyncOnly(target));
        file.RemoveAt(0);
        await saving;

        using var back = TiffFile.Read(new MemoryStream(target.ToArray()));
        Assert.Equal(2, back.Count);
        Assert.Equal(a.ReadRawPixels<float>(), back[0].ReadRawPixels<float>());
    }

    /// <summary>A stream that cannot seek and refuses synchronous writes, as the body of a web response.</summary>
    private sealed class AsyncOnly(Stream inner) : Stream
    {
        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override void Flush() => throw new InvalidOperationException("Synchronous operations are not allowed.");

        public override Task FlushAsync(CancellationToken cancellationToken) => inner.FlushAsync(cancellationToken);

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new InvalidOperationException("Synchronous operations are not allowed.");

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) =>
            inner.WriteAsync(buffer, cancellationToken);
    }

    [Fact]
    public void LONG8_tags_become_LONG_in_classic_files_or_are_dropped()
    {
        var image = TiffImage.Create<byte>(new byte[4], 2, 2);
        image.Metadata.Set(TiffTag.FromNumbers<ulong>(65020, [7UL, 8UL]));
        image.Metadata.Set(TiffTag.FromNumbers<ulong>(65021, [ulong.MaxValue]));
        image.Metadata.Set(TiffTag.FromNumbers<long>(65022, [-3L]));

        using var classic = TiffFile.Read(new MemoryStream(Save(new TiffFile().Add(image))));
        using var big = TiffFile.Read(new MemoryStream(Save(new TiffFile().Add(image), TiffWriteOptions.Default with { BigTiff = TiffBigTiff.Always })));

        Assert.Equal(TiffTagType.Long, classic.Primary.Metadata[65020]!.Type);
        Assert.Equal(new uint[] { 7, 8 }, classic.Primary.Metadata[65020]!.AsNumbers<uint>());
        Assert.Null(classic.Primary.Metadata[65021]);
        Assert.Equal(TiffTagType.SLong, classic.Primary.Metadata[65022]!.Type);
        Assert.Equal(TiffTagType.Long8, big.Primary.Metadata[65021]!.Type);
    }

    [Fact]
    public void A_page_with_sub_images_is_saved_without_them()
    {
        using var file = TiffFile.Open(DomainFixtures.PathOf("pyramid_subifds.tif"));
        Assert.NotEmpty(file.Primary.SubImages);

        using var back = TiffFile.Read(new MemoryStream(Save(file)));

        Assert.Equal(file.Count, back.Count);
        Assert.Empty(back.Primary.SubImages);
    }
}
