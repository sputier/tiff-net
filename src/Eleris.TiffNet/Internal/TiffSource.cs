using Microsoft.Win32.SafeHandles;

namespace Eleris.TiffNet.Internal;

/// <summary>
/// The bytes of a TIFF file, read at explicit offsets: no shared position, so that several threads can read strips at once.
/// Offset 0 is the first byte of the TIFF data (the "II" or "MM" of the header).
/// </summary>
/// <remarks>
/// Every read checks its bounds against the size of the data first: reading past the end is a defect of the file (a
/// <see cref="TiffException"/>), never an exception of the runtime. A failure of the caller's own file or stream is not
/// caught: its exception reaches the caller as it is.
/// </remarks>
internal abstract class TiffSource : IDisposable
{
    /// <summary>The size of the segments a stream is held in: no segment is a large object, and nothing is copied twice.</summary>
    private const int SegmentSize = 64 * 1024;

    protected TiffSource(string name, long length)
    {
        Name = ResourceBudget.NameOf(name);
        Length = length;
    }

    /// <summary>The name of the file in messages: its file name, never a path.</summary>
    public string Name { get; }

    /// <summary>The number of bytes of the TIFF data.</summary>
    public long Length { get; }

    protected bool Disposed { get; private set; }

    /// <summary>Fills <paramref name="destination"/> with the bytes at <paramref name="offset"/>.</summary>
    /// <exception cref="TiffException">The bytes are not all in the data: the file is truncated or an offset is wrong.</exception>
    /// <exception cref="ObjectDisposedException">The source is disposed.</exception>
    public void ReadExactly(long offset, Span<byte> destination)
    {
        ObjectDisposedException.ThrowIf(Disposed, this);
        if (offset < 0 || offset > Length || destination.Length > Length - offset)
        {
            throw new TiffException(
                $"{Name}: {destination.Length} bytes at offset {offset} are beyond the end of the data ({Length} bytes); the file is truncated or damaged.");
        }

        if (destination.Length > 0)
        {
            ReadCore(offset, destination);
        }
    }

    /// <summary>The bytes from <paramref name="offset"/> to <paramref name="offset"/> + <paramref name="length"/>, as a stream read in order.</summary>
    public RangeStream OpenRange(long offset, long length) => new(this, offset, length);

    /// <summary>Reads exactly <paramref name="destination"/>.Length bytes at <paramref name="offset"/>, which are within bounds.</summary>
    protected abstract void ReadCore(long offset, Span<byte> destination);

    public void Dispose()
    {
        if (!Disposed)
        {
            Disposed = true;
            Dispose(true);
        }

        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
    }

    /// <summary>Opens a file for reading at explicit offsets.</summary>
    /// <remarks>
    /// The file is shared for reading, writing and deletion, so that an application can save over a file it has open (the save
    /// replaces it through a temporary file) and other programs can read it meanwhile.
    /// </remarks>
    public static TiffSource Open(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        var handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, FileOptions.RandomAccess);
        try
        {
            return new FileSource(handle, path, RandomAccess.GetLength(handle));
        }
        catch
        {
            // GetLength fails on what is not a regular file (a pipe, a device): the handle must not wait for its finalizer.
            handle.Dispose();
            throw;
        }
    }

    /// <summary>A source over bytes already in memory.</summary>
    public static TiffSource FromMemory(ReadOnlyMemory<byte> data, string name = "memory") => new MemorySource(data, name);

    /// <summary>
    /// A source read on demand from a stream that seeks, from its current position: through the handle of a file stream (reads at
    /// explicit offsets, from several threads at once), otherwise one read at a time.
    /// </summary>
    public static TiffSource FromOpenStream(Stream stream, bool leaveOpen, string name)
    {
        long start = stream.Position;
        return stream is FileStream { SafeFileHandle: { IsInvalid: false } handle }
            ? new FileStreamSource(stream, handle, start, leaveOpen, name)
            : new SeekingSource(stream, start, leaveOpen, name);
    }

    /// <summary>
    /// The remaining bytes of a stream, held in memory: from the current position of a seekable stream, or everything a stream
    /// that cannot seek gives. At most <see cref="TiffReadOptions.MaxInputBytes"/> bytes are taken, and never more than one .NET
    /// array can hold.
    /// </summary>
    /// <exception cref="TiffException">The stream holds more than the limit (with <c>MaxInputBytes</c> named).</exception>
    public static TiffSource FromStream(Stream stream, TiffReadOptions options, string name = "stream")
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(options);
        if (stream.CanSeek)
        {
            byte[] data = new byte[RemainingLength(stream, options, name)];
            stream.ReadExactly(data);
            return new MemorySource(data, name);
        }

        var segments = new List<byte[]>();
        long total = 0;
        while (true)
        {
            byte[] segment = new byte[SegmentSize];
            int filled = stream.ReadAtLeast(segment, SegmentSize, throwOnEndOfStream: false);
            total += filled;
            CheckInput(total, options, name);
            if (filled > 0)
            {
                segments.Add(segment);
            }

            if (filled < SegmentSize)
            {
                return new SegmentedSource(segments, total, name);
            }
        }
    }

    /// <inheritdoc cref="FromStream"/>
    public static async Task<TiffSource> FromStreamAsync(Stream stream, TiffReadOptions options, string name = "stream",
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(options);
        if (stream.CanSeek)
        {
            byte[] data = new byte[RemainingLength(stream, options, name)];
            await stream.ReadExactlyAsync(data, cancellationToken).ConfigureAwait(false);
            return new MemorySource(data, name);
        }

        var segments = new List<byte[]>();
        long total = 0;
        while (true)
        {
            // A stream that ignores the token must still stop when the caller cancels.
            cancellationToken.ThrowIfCancellationRequested();
            byte[] segment = new byte[SegmentSize];
            int filled = await stream.ReadAtLeastAsync(segment, SegmentSize, throwOnEndOfStream: false, cancellationToken).ConfigureAwait(false);
            total += filled;
            CheckInput(total, options, name);
            if (filled > 0)
            {
                segments.Add(segment);
            }

            if (filled < SegmentSize)
            {
                return new SegmentedSource(segments, total, name);
            }
        }
    }

    /// <summary>The limit of a stream held in memory: the option, and never more than one .NET array.</summary>
    private static long InputLimit(TiffReadOptions options) => Math.Min(options.MaxInputBytes, Array.MaxLength);

    private static long RemainingLength(Stream stream, TiffReadOptions options, string name)
    {
        long remaining = Math.Max(0, stream.Length - stream.Position);
        CheckInput(remaining, options, name);
        return remaining;
    }

    private static void CheckInput(long total, TiffReadOptions options, string name)
    {
        long limit = InputLimit(options);
        if (total > limit)
        {
            throw new TiffException(
                $"{ResourceBudget.NameOf(name)}: the stream holds more than the {limit} bytes allowed by MaxInputBytes.",
                nameof(TiffReadOptions.MaxInputBytes));
        }
    }

    private sealed class FileSource(SafeFileHandle handle, string path, long length) : TiffSource(path, length)
    {
        protected override void ReadCore(long offset, Span<byte> destination)
        {
            while (destination.Length > 0)
            {
                int read = RandomAccess.Read(handle, destination, offset);
                if (read == 0)
                {
                    // Within the length measured at opening: the file has been shortened by someone else since.
                    throw new IOException($"{Name} became shorter while it was being read.");
                }

                destination = destination[read..];
                offset += read;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                handle.Dispose();
            }

            base.Dispose(disposing);
        }
    }

    /// <summary>A file stream of the application, read through its handle at explicit offsets (thread-safe), from a start.</summary>
    private sealed class FileStreamSource(Stream stream, SafeFileHandle handle, long start, bool leaveOpen, string name)
        : TiffSource(name, RandomAccess.GetLength(handle) - start)
    {
        protected override void ReadCore(long offset, Span<byte> destination)
        {
            offset += start;
            while (destination.Length > 0)
            {
                int read = RandomAccess.Read(handle, destination, offset);
                if (read == 0)
                {
                    throw new IOException($"{Name} became shorter while it was being read.");
                }

                destination = destination[read..];
                offset += read;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && !leaveOpen)
            {
                stream.Dispose();
            }

            base.Dispose(disposing);
        }
    }

    /// <summary>Another stream that seeks: one read at a time (a stream has one position), from a start.</summary>
    private sealed class SeekingSource(Stream stream, long start, bool leaveOpen, string name) : TiffSource(name, stream.Length - start)
    {
        private readonly Lock _sync = new();

        protected override void ReadCore(long offset, Span<byte> destination)
        {
            lock (_sync)
            {
                stream.Position = start + offset;
                stream.ReadExactly(destination);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && !leaveOpen)
            {
                stream.Dispose();
            }

            base.Dispose(disposing);
        }
    }

    private sealed class MemorySource(ReadOnlyMemory<byte> data, string name) : TiffSource(name, data.Length)
    {
        protected override void ReadCore(long offset, Span<byte> destination) =>
            data.Span.Slice((int)offset, destination.Length).CopyTo(destination);
    }

    /// <summary>A stream that could not seek, held in the segments it was read into: no copy into one large array.</summary>
    private sealed class SegmentedSource(List<byte[]> segments, long length, string name) : TiffSource(name, length)
    {
        protected override void ReadCore(long offset, Span<byte> destination)
        {
            while (destination.Length > 0)
            {
                int index = (int)(offset / SegmentSize);
                int within = (int)(offset % SegmentSize);
                int count = Math.Min(destination.Length, SegmentSize - within);
                segments[index].AsSpan(within, count).CopyTo(destination);
                destination = destination[count..];
                offset += count;
            }
        }
    }
}

/// <summary>A range of a source read in order (a compressed block inflated as a stream), in reads of what the caller asks.</summary>
internal sealed class RangeStream(TiffSource source, long offset, long length) : Stream
{
    private long _position;

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => length;

    public override long Position
    {
        get => _position;
        set => throw new NotSupportedException();
    }

    /// <summary>
    /// The failure of the source (the file or the stream of the application) during the last read: kept so that the decoder can
    /// give it to the caller as it is, not as damaged data.
    /// </summary>
    public Exception? Failure { get; private set; }

    public override int Read(Span<byte> buffer)
    {
        int count = (int)Math.Min(buffer.Length, length - _position);
        try
        {
            source.ReadExactly(offset + _position, buffer[..count]);
        }
        catch (Exception ex)
        {
            Failure = ex;
            throw;
        }

        _position += count;
        return count;
    }

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
