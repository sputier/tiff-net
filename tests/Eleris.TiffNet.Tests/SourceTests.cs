using Eleris.TiffNet.Internal;

namespace Eleris.TiffNet.Tests;

public sealed class SourceTests : IDisposable
{
    private static readonly byte[] Data = Enumerable.Range(0, 3_000_000).Select(i => (byte)(i * 31 % 251)).ToArray();
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.tif");

    public SourceTests() => File.WriteAllBytes(_path, Data);

    public void Dispose() => File.Delete(_path);

    public static TheoryData<string> Kinds() => new() { "file", "seekable stream", "forward-only stream", "memory" };

    private TiffSource Open(string kind) => kind switch
    {
        "file" => TiffSource.Open(_path),
        "seekable stream" => TiffSource.FromStream(new MemoryStream(Data), TiffReadOptions.Default),
        "forward-only stream" => TiffSource.FromStream(new ForwardOnlyStream(new MemoryStream(Data)), TiffReadOptions.Default),
        _ => TiffSource.FromMemory(Data),
    };

    [Theory]
    [MemberData(nameof(Kinds))]
    public void Bytes_are_read_at_any_offset(string kind)
    {
        using var source = Open(kind);
        var buffer = new byte[1000];

        Assert.Equal(Data.Length, source.Length);
        foreach (long offset in new long[] { 0, 1, 1_048_575, 1_048_576, 2_999_000 })
        {
            source.ReadExactly(offset, buffer);
            Assert.True(Data.AsSpan((int)offset, 1000).SequenceEqual(buffer), $"offset {offset}");
        }

        source.ReadExactly(Data.Length, []);   // an empty read at the end is fine
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public void Reading_beyond_the_data_is_a_defect_of_the_file(string kind)
    {
        using var source = Open(kind);
        var buffer = new byte[100];

        Assert.Throws<TiffException>(() => source.ReadExactly(Data.Length - 99, buffer));
        Assert.Throws<TiffException>(() => source.ReadExactly(-1, buffer));
        Assert.Throws<TiffException>(() => source.ReadExactly(long.MaxValue, buffer));
        Assert.Throws<TiffException>(() => source.ReadExactly(Data.Length + 1, []));
        var ex = Assert.Throws<TiffException>(() => source.ReadExactly(long.MaxValue - 50, buffer));
        Assert.Null(ex.Limit);
    }

    [Fact]
    public void A_file_is_named_by_its_file_name_only()
    {
        using var source = TiffSource.Open(_path);

        Assert.Equal(Path.GetFileName(_path), source.Name);
        var ex = Assert.Throws<TiffException>(() => source.ReadExactly(Data.Length, new byte[1]));
        Assert.StartsWith(Path.GetFileName(_path) + ":", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(Path.GetDirectoryName(_path)!, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Threads_read_a_file_at_once()
    {
        using var source = TiffSource.Open(_path);

        Parallel.For(0, 400, new ParallelOptions { MaxDegreeOfParallelism = 8 }, i =>
        {
            int offset = i * 7_000;
            var buffer = new byte[5_000];
            source.ReadExactly(offset, buffer);
            Assert.True(Data.AsSpan(offset, 5_000).SequenceEqual(buffer));
        });
    }

    [Fact]
    public void A_seekable_stream_is_read_from_its_position()
    {
        var stream = new MemoryStream(Data) { Position = 1000 };

        using var source = TiffSource.FromStream(stream, TiffReadOptions.Default);
        var buffer = new byte[10];
        source.ReadExactly(0, buffer);

        Assert.Equal(Data.Length - 1000, source.Length);
        Assert.Equal(Data[1000..1010], buffer);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_stream_larger_than_MaxInputBytes_is_refused(bool seekable)
    {
        var options = TiffReadOptions.Default with { MaxInputBytes = 2_000_000 };
        Stream stream = seekable ? new MemoryStream(Data) : new ForwardOnlyStream(new MemoryStream(Data));

        var ex = Assert.Throws<TiffException>(() => TiffSource.FromStream(stream, options));

        Assert.Equal("MaxInputBytes", ex.Limit);
    }

    [Fact]
    public void An_endless_stream_stops_at_the_limit()
    {
        var options = TiffReadOptions.Default with { MaxInputBytes = 5_000_000 };
        var endless = new EndlessStream();

        Assert.Equal("MaxInputBytes", Assert.Throws<TiffException>(() => TiffSource.FromStream(endless, options)).Limit);
        Assert.True(endless.Given <= 5_000_000 + (1 << 20), $"{endless.Given} bytes taken");
    }

    [Fact]
    public void A_limit_above_what_an_array_holds_is_reduced_to_it()
    {
        var unlimited = TiffReadOptions.Default with { MaxInputBytes = long.MaxValue };

        var ex = Assert.Throws<TiffException>(() => TiffSource.FromStream(new LengthOnlyStream(3L << 30), unlimited));

        Assert.Equal("MaxInputBytes", ex.Limit);
        Assert.Contains(Array.MaxLength.ToString(System.Globalization.CultureInfo.InvariantCulture), ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_name_given_by_the_caller_is_reduced_to_a_file_name()
    {
        using var source = TiffSource.FromStream(new MemoryStream(Data), TiffReadOptions.Default, _path);

        Assert.Equal(Path.GetFileName(_path), source.Name);
    }

    [Fact]
    public void An_open_file_can_be_moved_aside_and_deleted()
    {
        // What a save to the path of an open file needs: on Windows, a file open without FileShare.Delete can be neither
        // renamed nor deleted. (Replacing it in one move is refused by Windows whatever the sharing; the save moves it aside.)
        using var source = TiffSource.Open(_path);
        string aside = _path + ".old";

        File.Move(_path, aside);
        File.WriteAllBytes(_path, [1, 2, 3]);
        File.Delete(aside);

        Assert.Equal(3, new FileInfo(_path).Length);
        var buffer = new byte[10];
        source.ReadExactly(0, buffer);   // the open source still reads the old content
        Assert.Equal(Data[..10], buffer);
    }

    [Fact]
    public void A_stream_that_fails_rethrows_its_own_exception()
    {
        var failing = new FailingStream(new InvalidOperationException("The network is down."));

        var ex = Assert.Throws<InvalidOperationException>(() => TiffSource.FromStream(failing, TiffReadOptions.Default));

        Assert.Equal("The network is down.", ex.Message);
        Assert.Throws<OperationCanceledException>(() =>
            TiffSource.FromStream(new FailingStream(new OperationCanceledException()), TiffReadOptions.Default));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_stream_is_read_asynchronously(bool seekable)
    {
        Stream stream = seekable ? new MemoryStream(Data) : new ForwardOnlyStream(new MemoryStream(Data));

        using var source = await TiffSource.FromStreamAsync(stream, TiffReadOptions.Default);
        var buffer = new byte[100];
        source.ReadExactly(2_500_000, buffer);

        Assert.Equal(Data.AsSpan(2_500_000, 100).ToArray(), buffer);
    }

    [Fact]
    public async Task An_asynchronous_read_is_cancelled_even_if_the_stream_ignores_the_token()
    {
        using var later = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        var endless = new EndlessStream();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            TiffSource.FromStreamAsync(endless, TiffReadOptions.Default, cancellationToken: later.Token));
        Assert.True(endless.Given < Array.MaxLength, "the endless stream was read to the limit");
    }

    [Fact]
    public async Task An_asynchronous_read_is_cancelled()
    {
        using var cancel = new CancellationTokenSource();
        await cancel.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            TiffSource.FromStreamAsync(new ForwardOnlyStream(new MemoryStream(Data)), TiffReadOptions.Default, cancellationToken: cancel.Token));
    }

    [Fact]
    public void A_disposed_source_cannot_be_read()
    {
        var source = TiffSource.Open(_path);
        source.Dispose();
        source.Dispose();

        Assert.Throws<ObjectDisposedException>(() => source.ReadExactly(0, new byte[1]));
        File.Delete(_path);   // the handle is closed
    }

    private sealed class ForwardOnlyStream(Stream inner) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, Math.Min(count, 70_000));
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    /// <summary>A seekable stream that announces a length and gives nothing.</summary>
    private sealed class LengthOnlyStream(long length) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => length;
        public override long Position { get; set; }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => 0;
        public override long Seek(long offset, SeekOrigin origin) => Position = offset;
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class EndlessStream : Stream
    {
        public long Given { get; private set; }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count)
        {
            Given += count;
            return count;
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class FailingStream(Exception failure) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw failure;
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
