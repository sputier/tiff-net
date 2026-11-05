using System.Buffers;
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
    private const int ChunkSize = 1 << 20;

    protected TiffSource(string name, long length)
    {
        Name = name;
        Length = length;
    }

    /// <summary>The name of the file in messages: its file name, or "stream".</summary>
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
    public static TiffSource Open(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        return new FileSource(path);
    }

    /// <summary>
    /// The remaining bytes of a stream, held in memory: from the current position of a seekable stream, or everything a stream
    /// that cannot seek gives. At most <see cref="TiffReadOptions.MaxInputBytes"/> bytes are taken.
    /// </summary>
    /// <exception cref="TiffException">The stream holds more than <see cref="TiffReadOptions.MaxInputBytes"/> (with that limit named).</exception>
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

        var chunks = new List<byte[]>();
        try
        {
            long total = 0;
            while (true)
            {
                byte[] chunk = ArrayPool<byte>.Shared.Rent(ChunkSize);
                chunks.Add(chunk);
                int filled = stream.ReadAtLeast(chunk.AsSpan(0, ChunkSize), ChunkSize, throwOnEndOfStream: false);
                total += filled;
                CheckInput(total, options, name);
                if (filled < ChunkSize)
                {
                    return new MemorySource(Concatenate(chunks, total), name);
                }
            }
        }
        finally
        {
            foreach (byte[] chunk in chunks)
            {
                ArrayPool<byte>.Shared.Return(chunk);
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

        var chunks = new List<byte[]>();
        try
        {
            long total = 0;
            while (true)
            {
                byte[] chunk = ArrayPool<byte>.Shared.Rent(ChunkSize);
                chunks.Add(chunk);
                int filled = await stream.ReadAtLeastAsync(chunk.AsMemory(0, ChunkSize), ChunkSize, throwOnEndOfStream: false,
                    cancellationToken).ConfigureAwait(false);
                total += filled;
                CheckInput(total, options, name);
                if (filled < ChunkSize)
                {
                    return new MemorySource(Concatenate(chunks, total), name);
                }
            }
        }
        finally
        {
            foreach (byte[] chunk in chunks)
            {
                ArrayPool<byte>.Shared.Return(chunk);
            }
        }
    }

    private static long RemainingLength(Stream stream, TiffReadOptions options, string name)
    {
        long remaining = Math.Max(0, stream.Length - stream.Position);
        CheckInput(remaining, options, name);
        return remaining;
    }

    private static void CheckInput(long total, TiffReadOptions options, string name)
    {
        if (total > options.MaxInputBytes)
        {
            throw new TiffException(
                $"{name}: the stream holds more than the {options.MaxInputBytes} bytes allowed by MaxInputBytes.", nameof(TiffReadOptions.MaxInputBytes));
        }
    }

    private static byte[] Concatenate(List<byte[]> chunks, long total)
    {
        byte[] data = new byte[total];
        long at = 0;
        foreach (byte[] chunk in chunks)
        {
            int count = (int)Math.Min(ChunkSize, total - at);
            chunk.AsSpan(0, count).CopyTo(data.AsSpan((int)at));
            at += count;
        }

        return data;
    }

    private sealed class FileSource : TiffSource
    {
        private readonly SafeFileHandle _handle;

        public FileSource(string path) : this(File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.Read, FileOptions.RandomAccess), path)
        {
        }

        private FileSource(SafeFileHandle handle, string path) : base(ResourceBudget.NameOf(path), RandomAccess.GetLength(handle))
        {
            _handle = handle;
        }

        protected override void ReadCore(long offset, Span<byte> destination)
        {
            while (destination.Length > 0)
            {
                int read = RandomAccess.Read(_handle, destination, offset);
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
                _handle.Dispose();
            }

            base.Dispose(disposing);
        }
    }

    private sealed class MemorySource(ReadOnlyMemory<byte> data, string name) : TiffSource(name, data.Length)
    {
        protected override void ReadCore(long offset, Span<byte> destination) =>
            data.Span.Slice((int)offset, destination.Length).CopyTo(destination);
    }

    /// <summary>A source over bytes already in memory (tests, and images built in memory).</summary>
    public static TiffSource FromMemory(ReadOnlyMemory<byte> data, string name = "memory") => new MemorySource(data, name);
}
