using System.Numerics;
using System.Runtime.InteropServices;
using Eleris.TiffNet.Internal;
using Eleris.TiffNet.Internal.Pixels;
using Eleris.TiffNet.Internal.Writing;

namespace Eleris.TiffNet;

/// <summary>
/// Writes one image piece by piece, for mosaics and other images that do not fit in one array, without holding the image in
/// memory: rows in order (<see cref="TiffLayout.Strips"/>) or tiles in any order (<see cref="TiffLayout.Tiles"/>), then
/// <see cref="Complete"/>.
/// </summary>
/// <remarks>
/// <para>
/// The image is up to 2 147 483 647 pixels wide and high (what the library reads back); the file is BigTIFF when its size could exceed 4 GB
/// (<see cref="TiffBigTiff.Auto"/> decides from the geometry, before the first byte is written). Strips or tiles are encoded on
/// the cores as they come, at most 2 × <see cref="TiffWriteOptions.MaxDegreeOfParallelism"/> at once, which bounds the memory.
/// </para>
/// <para>
/// The path overload writes a temporary file that replaces the target on <see cref="Complete"/>, as <see cref="TiffFile.Save(string, TiffWriteOptions?, CancellationToken)"/>
/// does; <see cref="Dispose"/> without <see cref="Complete"/> abandons it. A writer is used from one thread.
/// </para>
/// </remarks>
public sealed class TiffImageWriter : IDisposable
{
    private readonly Stream _output;
    private readonly TemporaryFile? _temporary;
    private readonly FileWriter _writer;
    private readonly TiffMetadata _metadata;
    private readonly int _channels;
    private readonly int _bytesPerSample;
    private readonly TiffSampleType _sampleType;
    private readonly TiffColorModel _colorModel;
    private readonly TiffWriteOptions _options;
    private readonly TiffPredictor _predictor;
    private readonly bool _tiled;
    private readonly bool _planar;
    private readonly int _blockWidth;
    private readonly int _blockHeight;
    private readonly long _across;
    private readonly long _down;
    private readonly long[] _offsets;
    private readonly long[] _counts;
    private readonly bool[] _written;
    private readonly Queue<PendingBlock> _pending = new();
    private readonly object _sync = new();
    private int _helpers;

    // Strips: the rows of the strip being filled.
    private byte[]? _strip;
    private int _stripRows;
    private long _rowsWritten;
    private bool _completed;
    private bool _disposed;

    // A write failed (an encoding, the output): what is written is inconsistent, the writer only disposes.
    private bool _faulted;

    private TiffImageWriter(Stream output, TemporaryFile? temporary, long width, long height, TiffSampleType sampleType, int channels,
        TiffColorModel? colorModel, TiffMetadata? metadata, TiffWriteOptions? options)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(width, int.MaxValue);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(height, int.MaxValue);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(channels);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(channels, ushort.MaxValue);
        if (!Enum.IsDefined(sampleType))
        {
            throw new ArgumentOutOfRangeException(nameof(sampleType), sampleType, "Not a sample type.");
        }

        _colorModel = colorModel ?? (channels <= 2
            ? TiffColorModel.Grey
            : throw new ArgumentException($"{channels} channels need a colour model: Rgb, or Grey with extra channels.", nameof(colorModel)));
        if (!Enum.IsDefined(_colorModel) || (_colorModel == TiffColorModel.Rgb && channels < 3))
        {
            throw new ArgumentException($"The colour model {_colorModel} does not fit {channels} channels.", nameof(colorModel));
        }

        _options = options ?? TiffWriteOptions.Default;
        _output = output;
        _temporary = temporary;
        Width = width;
        Height = height;
        _sampleType = sampleType;
        _channels = channels;
        _bytesPerSample = SampleConversion.SizeOf(sampleType);
        _metadata = metadata ?? new TiffMetadata();
        _predictor = ImageEncoder.ChoosePredictor(_options.Predictor, _options.Compression, sampleType);
        _tiled = _options.Layout == TiffLayout.Tiles;
        _planar = _options.PlanarConfiguration == TiffPixelOrder.Planar && channels > 1;
        long pixelBytes = (long)channels * _bytesPerSample;

        // A strip in the file holds one channel when planar: its rows are counted with one sample.
        long stripRowBytes = width * (_planar ? 1 : channels) * _bytesPerSample;
        if (_tiled)
        {
            _blockWidth = _blockHeight = _options.TileSize;
        }
        else
        {
            if (width * pixelBytes > Array.MaxLength)
            {
                throw new ArgumentException($"Rows of {width} pixels are larger than an array: write the image in tiles.", nameof(width));
            }

            _blockWidth = (int)width;
            _blockHeight = (int)Math.Clamp(ImageEncoder.StripBytes / stripRowBytes, 1, height);
        }

        if ((long)_blockWidth * _blockHeight * pixelBytes > Array.MaxLength)
        {
            throw new ArgumentException("A tile of these pixels is larger than an array: use smaller tiles.", nameof(options));
        }

        _across = (width + _blockWidth - 1) / _blockWidth;
        _down = (height + _blockHeight - 1) / _blockHeight;
        long blocks = _across * _down * (_planar ? channels : 1);
        if (blocks > Array.MaxLength / 8)
        {
            throw new ArgumentException($"The image would have {blocks} strips or tiles: use larger tiles.", nameof(options));
        }

        _offsets = new long[blocks];
        _counts = new long[blocks];
        _written = new bool[_across * _down];

        // The most the blocks can take once encoded (compression can expand), as the writer of a whole image decides.
        long blockRaw = (long)_blockWidth * _blockHeight * (_planar ? 1 : channels) * _bytesPerSample;
        long encoded = BlockCodec.MaxEncoded(_options.Compression, blockRaw, _blockHeight) * blocks;
        bool bigTiff = _options.BigTiff switch
        {
            TiffBigTiff.Always => true,
            TiffBigTiff.Never => false,
            _ => encoded + blocks * 34 + _metadata.Sum(t => (long)t.AsBytes().Length + 32) + 65536 > uint.MaxValue,
        };
        _writer = new FileWriter(output, bigTiff);
    }

    /// <summary>The width of the image, in pixels.</summary>
    public long Width { get; }

    /// <summary>The height of the image, in pixels.</summary>
    public long Height { get; }

    /// <summary>Starts writing an image into a file, through a temporary file that replaces it on <see cref="Complete"/>.</summary>
    /// <param name="path">The path of the file.</param>
    /// <param name="width">The width, in pixels (up to 2 147 483 647).</param>
    /// <param name="height">The height, in pixels (up to 2 147 483 647).</param>
    /// <param name="sampleType">The type of the samples given to <see cref="WriteRows{T}"/> or <see cref="WriteTile{T}"/>.</param>
    /// <param name="channels">The channels per pixel: the colour channels (1 for grey, 3 for RGB), then extra channels (of kind Data).</param>
    /// <param name="colorModel">What the colour channels are; required with 3 channels or more.</param>
    /// <param name="metadata">The tags to write with the image (description, ICC profile…), or none.</param>
    /// <param name="options">How to write; <see cref="TiffWriteOptions.Default"/> when <see langword="null"/>. The layout chooses between rows and tiles.</param>
    /// <returns>The writer, to dispose.</returns>
    /// <exception cref="ArgumentException">The sizes, the colour model or the layout do not fit the image.</exception>
    /// <exception cref="IOException">The file cannot be written.</exception>
    public static TiffImageWriter Create(string path, long width, long height, TiffSampleType sampleType, int channels = 1,
        TiffColorModel? colorModel = null, TiffMetadata? metadata = null, TiffWriteOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        Platform.EnsureSupported();
        var temporary = TemporaryFile.Next(path);
        try
        {
            return new TiffImageWriter(temporary.Stream, temporary, width, height, sampleType, channels, colorModel, metadata, options);
        }
        catch
        {
            temporary.Dispose();
            throw;
        }
    }

    /// <summary>Starts writing an image into a stream, from its current position; the stream must seek and is left open.</summary>
    /// <param name="stream">The stream.</param>
    /// <param name="width">The width, in pixels (up to 2 147 483 647).</param>
    /// <param name="height">The height, in pixels (up to 2 147 483 647).</param>
    /// <param name="sampleType">The type of the samples.</param>
    /// <param name="channels">The channels per pixel.</param>
    /// <param name="colorModel">What the colour channels are; required with 3 channels or more.</param>
    /// <param name="metadata">The tags to write with the image, or none.</param>
    /// <param name="options">How to write; <see cref="TiffWriteOptions.Default"/> when <see langword="null"/>.</param>
    /// <returns>The writer, to dispose.</returns>
    /// <exception cref="ArgumentException">The stream cannot seek or be written, or the sizes do not fit the image.</exception>
    public static TiffImageWriter Create(Stream stream, long width, long height, TiffSampleType sampleType, int channels = 1,
        TiffColorModel? colorModel = null, TiffMetadata? metadata = null, TiffWriteOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(stream);
        Platform.EnsureSupported();
        if (!stream.CanSeek || !stream.CanWrite)
        {
            throw new ArgumentException("The stream must seek and be written: the directory is written last, and linked from the header.", nameof(stream));
        }

        return new TiffImageWriter(stream, null, width, height, sampleType, channels, colorModel, metadata, options);
    }

    /// <summary>Writes the next rows of the image, top to bottom (<see cref="TiffLayout.Strips"/>).</summary>
    /// <typeparam name="T">The type of the samples, as declared.</typeparam>
    /// <param name="pixels">Exactly <c>rowCount × Width × channels</c> samples, interleaved.</param>
    /// <param name="rowCount">The number of rows.</param>
    /// <exception cref="InvalidOperationException">The writer writes tiles, the rows go beyond the image, or it is completed.</exception>
    /// <exception cref="ArgumentException">The samples are not of the declared type, or not as many as the rows hold.</exception>
    public void WriteRows<T>(ReadOnlySpan<T> pixels, int rowCount) where T : unmanaged, INumber<T>
    {
        CheckOpen<T>();
        if (_tiled)
        {
            throw new InvalidOperationException("The writer writes tiles (Layout = Tiles): use WriteTile.");
        }

        ArgumentOutOfRangeException.ThrowIfNegative(rowCount);
        if (_rowsWritten + rowCount > Height)
        {
            throw new InvalidOperationException($"{rowCount} rows after {_rowsWritten} go beyond the {Height} rows of the image.");
        }

        long rowSamples = Width * _channels;
        if (pixels.Length != rowCount * rowSamples)
        {
            throw new ArgumentException($"{rowCount} rows are {rowCount * rowSamples} samples, not {pixels.Length}.", nameof(pixels));
        }

        var bytes = MemoryMarshal.AsBytes(pixels);
        int rowBytes = (int)(rowSamples * _bytesPerSample);
        try
        {
            for (int r = 0; r < rowCount; r++)
            {
                int rowsOfStrip = (int)Math.Min(_blockHeight, Height - _rowsWritten + _stripRows);
                _strip ??= System.Buffers.ArrayPool<byte>.Shared.Rent(rowBytes * rowsOfStrip);
                bytes.Slice(r * rowBytes, rowBytes).CopyTo(_strip.AsSpan(_stripRows * rowBytes));
                _stripRows++;
                _rowsWritten++;
                if (_stripRows == rowsOfStrip)
                {
                    // The buffer is handed over before it is submitted: whatever fails then, it is not returned twice.
                    byte[] strip = _strip;
                    _strip = null;
                    _stripRows = 0;
                    Submit((_rowsWritten - 1) / _blockHeight, strip, rowsOfStrip, (int)Width);
                }
            }
        }
        catch
        {
            _faulted = true;
            throw;
        }
    }

    /// <summary>Writes a tile (<see cref="TiffLayout.Tiles"/>), in any order, each once.</summary>
    /// <typeparam name="T">The type of the samples, as declared.</typeparam>
    /// <param name="tileX">The column of the tile (0 for the first <see cref="TiffWriteOptions.TileSize"/> pixels).</param>
    /// <param name="tileY">The row of the tile.</param>
    /// <param name="pixels">The samples of the tile, interleaved: a whole tile, or at the right and the bottom edges only the part
    /// within the image.</param>
    /// <exception cref="InvalidOperationException">The writer writes rows, the tile is written already, or it is completed.</exception>
    /// <exception cref="ArgumentOutOfRangeException">There is no such tile.</exception>
    /// <exception cref="ArgumentException">The samples are not of the declared type, or not as many as the tile holds.</exception>
    public void WriteTile<T>(long tileX, long tileY, ReadOnlySpan<T> pixels) where T : unmanaged, INumber<T>
    {
        CheckOpen<T>();
        if (!_tiled)
        {
            throw new InvalidOperationException("The writer writes rows (Layout = Strips): use WriteRows.");
        }

        ArgumentOutOfRangeException.ThrowIfNegative(tileX);
        ArgumentOutOfRangeException.ThrowIfNegative(tileY);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(tileX, _across);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(tileY, _down);
        long index = tileY * _across + tileX;
        if (_written[index])
        {
            throw new InvalidOperationException($"The tile {tileX}, {tileY} is written already.");
        }

        int width = (int)Math.Min(_blockWidth, Width - tileX * _blockWidth);
        int height = (int)Math.Min(_blockHeight, Height - tileY * _blockHeight);
        int pixelBytes = _channels * _bytesPerSample;
        var bytes = MemoryMarshal.AsBytes(pixels);
        int tileBytes = _blockWidth * _blockHeight * pixelBytes;
        byte[] tile;
        if (pixels.Length == (long)_blockWidth * _blockHeight * _channels)
        {
            tile = System.Buffers.ArrayPool<byte>.Shared.Rent(tileBytes);
            bytes.CopyTo(tile);
        }
        else if (pixels.Length == (long)width * height * _channels)
        {
            tile = System.Buffers.ArrayPool<byte>.Shared.Rent(tileBytes);
            tile.AsSpan(0, tileBytes).Clear();
            for (int r = 0; r < height; r++)
            {
                bytes.Slice(r * width * pixelBytes, width * pixelBytes).CopyTo(tile.AsSpan(r * _blockWidth * pixelBytes));
            }
        }
        else
        {
            throw new ArgumentException($"The tile {tileX}, {tileY} holds {width} x {height} pixels (or {_blockWidth} x {_blockHeight} whole), "
                + $"not {pixels.Length} samples of {_channels} channels.", nameof(pixels));
        }

        _written[index] = true;
        try
        {
            Submit(index, tile, _blockHeight, _blockWidth);
        }
        catch
        {
            _faulted = true;
            throw;
        }
    }

    /// <summary>Writes what is left and the directory; for a path, puts the file in place of the target.</summary>
    /// <exception cref="InvalidOperationException">Rows or tiles are missing, or an earlier write failed.</exception>
    public void Complete()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_completed)
        {
            return;
        }

        ThrowIfFaulted();

        if (!_tiled && _rowsWritten != Height)
        {
            throw new InvalidOperationException($"{_rowsWritten} rows of {Height} are written.");
        }

        long missing = Array.IndexOf(_written, false);
        if (_tiled && missing >= 0)
        {
            throw new InvalidOperationException($"The tile {missing % _across}, {missing / _across} is not written.");
        }

        try
        {
            Drain(0);
            _writer.WriteDirectory(Entries());
            _output.Flush();
            _temporary?.Commit();
        }
        catch
        {
            _faulted = true;
            throw;
        }

        _completed = true;
    }

    /// <summary>Ends the writer; without <see cref="Complete"/>, the output is abandoned (the temporary file of a path is removed).</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        lock (_sync)
        {
            // Abandoned blocks: those not started are released, those being encoded are waited for (they use their buffers).
            foreach (var item in _pending)
            {
                if (item.State == PendingBlock.Waiting)
                {
                    item.State = PendingBlock.Done;
                    System.Buffers.ArrayPool<byte>.Shared.Return(item.Raw);
                    continue;
                }

                while (item.State != PendingBlock.Done)
                {
                    Monitor.Wait(_sync);
                }

                if (item.Error is null)
                {
                    item.Result.Return();
                }
            }

            _pending.Clear();
        }

        if (_strip is not null)
        {
            System.Buffers.ArrayPool<byte>.Shared.Return(_strip);
            _strip = null;
        }

        _temporary?.Dispose();
    }

    private void CheckOpen<T>() where T : unmanaged
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_completed)
        {
            throw new InvalidOperationException("The image is completed.");
        }

        ThrowIfFaulted();
        if (SampleConversion.ClrType(_sampleType) != typeof(T))
        {
            throw new ArgumentException($"The samples are declared {_sampleType}: give them as {SampleConversion.ClrType(_sampleType).Name}, not {typeof(T).Name}.");
        }
    }

    private void ThrowIfFaulted()
    {
        if (_faulted)
        {
            throw new InvalidOperationException("An earlier write failed: the image is incomplete; dispose the writer.");
        }
    }

    /// <summary>
    /// Encodes a block of interleaved rows (a strip, or a whole tile) on a thread of the pool: one block, or one per channel when
    /// planar. The blocks are written in the order they are given; at most 2 × the degree are in flight.
    /// </summary>
    private void Submit(long index, byte[] raw, int rows, int width)
    {
        long perPlane = _across * _down;
        if (!_planar)
        {
            Enqueue(index, raw, rows, width * _channels * _bytesPerSample, _channels);
            return;
        }

        int pixelBytes = _channels * _bytesPerSample;
        try
        {
            for (int c = 0; c < _channels; c++)
            {
                byte[] plane = System.Buffers.ArrayPool<byte>.Shared.Rent(rows * width * _bytesPerSample);
                for (int p = 0; p < rows * width; p++)
                {
                    raw.AsSpan(p * pixelBytes + c * _bytesPerSample, _bytesPerSample).CopyTo(plane.AsSpan(p * _bytesPerSample));
                }

                Enqueue(c * perPlane + index, plane, rows, width * _bytesPerSample, 1);
            }
        }
        finally
        {
            System.Buffers.ArrayPool<byte>.Shared.Return(raw);
        }
    }

    /// <summary>
    /// Queues a block (taking its buffer) and starts a helper on the pool when one is free. Helpers encode the waiting blocks
    /// and stop when there is none; the calling thread encodes, when it needs it, a block no helper took, so that a busy pool
    /// slows the write down but never stops it.
    /// </summary>
    private void Enqueue(long block, byte[] raw, int rows, int rowBytes, int samples)
    {
        Drain(2 * _options.Degree - 1);
        bool start = false;
        lock (_sync)
        {
            _pending.Enqueue(new PendingBlock(block, raw, rows, rowBytes, samples));
            if (_helpers < _options.Degree - 1)
            {
                _helpers++;
                start = true;
            }
        }

        if (start)
        {
            ThreadPool.UnsafeQueueUserWorkItem(static writer => writer.Help(), this, preferLocal: false);
        }
    }

    private void Help()
    {
        while (true)
        {
            PendingBlock? item = null;
            lock (_sync)
            {
                foreach (var waiting in _pending)
                {
                    if (waiting.State == PendingBlock.Waiting)
                    {
                        item = waiting;
                        item.State = PendingBlock.Encoding;
                        break;
                    }
                }

                if (item is null)
                {
                    _helpers--;
                    return;
                }
            }

            Encode(item);
        }
    }

    private void Encode(PendingBlock item)
    {
        try
        {
            item.Result = BlockCodec.Encode(item.Raw, item.Rows, item.RowBytes, item.Samples, _bytesPerSample, _predictor,
                _options.Compression, _options.Level);
        }
        catch (Exception ex)
        {
            item.Error = System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex);
        }

        lock (_sync)
        {
            item.State = PendingBlock.Done;
            Monitor.PulseAll(_sync);
        }
    }

    /// <summary>Writes the encoded blocks, in order, until at most <paramref name="keep"/> are in flight.</summary>
    private void Drain(int keep)
    {
        while (_pending.Count > keep)
        {
            PendingBlock item;
            bool mine = false;
            lock (_sync)
            {
                item = _pending.Peek();
                if (item.State == PendingBlock.Waiting)
                {
                    item.State = PendingBlock.Encoding;
                    mine = true;
                }
                else
                {
                    while (item.State != PendingBlock.Done)
                    {
                        Monitor.Wait(_sync);
                    }
                }
            }

            if (mine)
            {
                Encode(item);
            }

            lock (_sync)
            {
                _pending.Dequeue();
            }

            item.Error?.Throw();
            try
            {
                _offsets[item.Block] = _writer.Append(item.Result.Span);
                _counts[item.Block] = item.Result.Length;
            }
            finally
            {
                item.Result.Return();
            }
        }
    }

    /// <summary>A block queued for encoding: its raw rows (owned until encoded), then its encoding or its failure.</summary>
    private sealed class PendingBlock(long block, byte[] raw, int rows, int rowBytes, int samples)
    {
        public const int Waiting = 0, Encoding = 1, Done = 2;

        public long Block { get; } = block;

        public byte[] Raw { get; } = raw;

        public int Rows { get; } = rows;

        public int RowBytes { get; } = rowBytes;

        public int Samples { get; } = samples;

        /// <summary>Changed under the lock of the writer.</summary>
        public int State { get; set; }

        public EncodedBlock Result { get; set; }

        public System.Runtime.ExceptionServices.ExceptionDispatchInfo? Error { get; set; }
    }

    private List<DirectoryEntry> Entries()
    {
        int colours = _colorModel == TiffColorModel.Rgb ? 3 : 1;
        var shape = new ImageShape(Width, Height, _channels, _sampleType, _colorModel, [.. Enumerable.Repeat(TiffExtraChannelKind.Data, _channels - colours)],
            0, _tiled, _planar, _blockWidth, _blockHeight, _options.Compression, _predictor);
        return TiffFileWriter.Entries(shape, _metadata, _offsets, _counts, _writer.BigTiff);
    }
}
