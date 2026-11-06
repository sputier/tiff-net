using System.IO.Compression;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using Eleris.TiffNet.Internal.Codecs;
using Eleris.TiffNet.Internal.Jpeg;
using Eleris.TiffNet.Internal.Structure;

namespace Eleris.TiffNet.Internal.Pixels;

/// <summary>What a read asks for: a region of the delivered (oriented) image, its channels, their order and scale.</summary>
internal readonly record struct PixelRequest(int X, int Y, int Width, int Height, int? Channel, TiffPixelOrder Order, TiffPixelScale Scale)
{
    /// <summary>The number of samples the read delivers; more than <see cref="long.MaxValue"/> saturates rather than wraps.</summary>
    public long Count(int channels)
    {
        Int128 count = (Int128)Width * Height * (Channel is null ? channels : 1);
        return count > long.MaxValue ? long.MaxValue : (long)count;
    }
}

/// <summary>
/// The strips or tiles a read needs, checked before anything is allocated: their byte counts hold the rows the read takes from
/// them, and the buffers of the workers fit in the budget. The units of work are produced as they are consumed, never listed.
/// </summary>
internal sealed class PixelPlan
{
    /// <summary>The bytes of stored rows read at once from an uncompressed strip or tile.</summary>
    private const int ChunkBytes = 1 << 20;

    /// <summary>
    /// The decoded bytes beyond which a Deflate block is inflated as a stream, rows by rows, straight to the destination, instead of
    /// whole into a buffer: an image of one large strip costs its pixels and about a megabyte, not twice its pixels.
    /// </summary>
    internal const long StreamBytes = 4 << 20;

    /// <summary>Whether the rows of a block that a read needs are inflated as a stream (Deflate only: its decoder resumes).</summary>
    internal static bool Streamed(ImageDescriptor d, long needed) => d.Compression == TiffCompression.Deflate && needed > StreamBytes;

    /// <summary>The memory of a streamed block besides its rows: the buffer of its read and the state of zlib.</summary>
    private const int StreamState = 128 * 1024;

    /// <summary>The rows inflated at once by a streamed block.</summary>
    internal static int StreamRows(long rowBytes) => (int)Math.Max(1, ChunkBytes / Math.Max(1, rowBytes));

    private PixelPlan(ImageDescriptor d, PixelRequest request, StoredRect stored, long rowBytes, int rowsPerUnit)
    {
        Compressed = d.Compression != TiffCompression.None;
        Descriptor = d;
        Request = request;
        Stored = stored;
        RowBytes = rowBytes;
        RowsPerUnit = rowsPerUnit;
    }

    public ImageDescriptor Descriptor { get; }

    public PixelRequest Request { get; }

    /// <summary>The rectangle of the stored image under the region.</summary>
    public StoredRect Stored { get; }

    /// <summary>The bytes of one stored row of a block (of one plane for a planar image).</summary>
    public long RowBytes { get; }

    public int RowsPerUnit { get; }

    /// <summary>Whether the blocks are compressed: each is then decoded whole (up to the last row the read needs) by one worker.</summary>
    public bool Compressed { get; }

    /// <summary>The JPEGTables of a JPEG image, read once for the read and shared by the decoders of its blocks, or null.</summary>
    public JpegTables? JpegTables { get; private set; }

    /// <summary>The number of threads that decode (1 for a read into a span).</summary>
    public int Workers { get; private set; }

    /// <summary>The bytes of the buffer a worker reads stored data into: rows of a strip, or the compressed bytes of a block.</summary>
    public long ReadBytes { get; private set; }

    /// <summary>Counts the units read, for a caller that follows the progress of the read; none otherwise.</summary>
    public ProgressCounter? Progress { get; set; }

    /// <summary>The bytes of the buffer a worker decodes a compressed block into (0 for uncompressed data).</summary>
    public long DecodedBytes { get; private set; }

    /// <summary>The samples of the buffer a worker unpacks a row into: a row of a block, two samples more per pixel for a palette.</summary>
    public long RowSamples { get; private set; }

    /// <summary>
    /// The predictor to undo on the decoded rows. As libtiff does, only LZW and Deflate data is predicted: the tag is ignored with
    /// other compressions.
    /// </summary>
    public TiffPredictor Predictor => Descriptor.Compression is TiffCompression.Lzw or TiffCompression.Deflate ? Descriptor.Predictor : TiffPredictor.None;

    /// <summary>The bytes of the row a worker rearranges the bytes of a floating point predictor in (0 without that predictor).</summary>
    public long PredictorBytes => Predictor == TiffPredictor.FloatingPoint ? RowBytes : 0;

    /// <summary>
    /// Checks the blocks under the request and reserves the buffers of the workers. Neither the destination nor any buffer is
    /// allocated yet: a small file declaring a huge image fails here, at the cost of its tables.
    /// </summary>
    /// <param name="d">The image.</param>
    /// <param name="budget">The limits of the read.</param>
    /// <param name="request">The region, channels, order and scale asked for.</param>
    /// <param name="sampleBytes">The size of a delivered sample (the type the stored samples are unpacked to).</param>
    /// <param name="parallel">Whether the read may use several threads.</param>
    /// <param name="maxWorkers">The most threads the caller allows (the degree of a write), below that of the read options.</param>
    /// <exception cref="NotSupportedException">The image is compressed with a codec or a predictor not supported, or one of
    /// its blocks is larger than an array.</exception>
    /// <exception cref="TiffException">A block is truncated, or the buffers exceed <see cref="TiffReadOptions.MaxPixelBytes"/>.</exception>
    public static PixelPlan Create(ImageDescriptor d, ResourceBudget budget, PixelRequest request, int sampleBytes, bool parallel,
        int maxWorkers = int.MaxValue)
    {
        if (d.Compression is not (TiffCompression.None or TiffCompression.Lzw or TiffCompression.Deflate or TiffCompression.PackBits
            or TiffCompression.Jpeg))
        {
            throw new NotSupportedException($"{budget.SourceName}: reading {d.Compression} compressed images is not supported.");
        }

        bool jpeg = d.Compression == TiffCompression.Jpeg;
        if (jpeg && (d.BlockWidth > ushort.MaxValue || (d.Layout == TiffLayout.Tiles && d.BlockHeight > ushort.MaxValue)))
        {
            // A JPEG frame is at most 65535 x 65535: no stream can fill such a strip or tile.
            throw budget.Invalid($"JPEG {(d.Layout == TiffLayout.Tiles ? "tiles" : "strips")} of {d.BlockWidth} x {d.BlockHeight} pixels cannot hold a JPEG frame.");
        }

        JpegTables? jpegTables = null;
        if (jpeg && d.JpegTables is { } tables)
        {
            try
            {
                jpegTables = JpegTables.Read(tables, budget);
            }
            catch (InvalidDataException ex)
            {
                throw budget.Invalid($"the JPEGTables cannot be read: {ex.Message}");
            }
        }

        long rowBytes = ImageDescriptor.BytesPerRow(d.BlockWidth, SampleUnpacker.StoredSamples(d), d.BitsPerSample);
        bool compressed = d.Compression != TiffCompression.None;
        // Uncompressed strips are read by chunks of rows, which can span strips that follow each other in the file, within the
        // rows the request covers.
        var stored = StoredRect.Of(d, request);
        long rowsLimit = d.Layout == TiffLayout.Strips ? stored.Y1 - stored.Y0 : d.BlockHeight;
        int rowsPerUnit = compressed ? (int)d.BlockHeight : (int)Math.Clamp(ChunkBytes / Math.Max(1, rowBytes), 1, Math.Max(1, rowsLimit));
        var plan = new PixelPlan(d, request, stored, rowBytes, rowsPerUnit);
        string kind = d.Layout == TiffLayout.Tiles ? "tile" : "strip";

        long units = 0, rowsRead = 0, largestCompressed = 0, largestDecoded = 0;
        foreach (var (block, _, blockY, first, end) in plan.Blocks())
        {
            // A JPEG stream is decoded whole: its rows are all decoded, whatever the read needs.
            if (jpeg && d.Layout == TiffLayout.Strips && BlockRows(d, blockY) > ushort.MaxValue)
            {
                // Checked before decoding: no JPEG frame covers more than 65535 rows.
                throw budget.Invalid($"a JPEG strip of {BlockRows(d, blockY)} rows cannot hold a JPEG frame.");
            }

            long needed = (jpeg ? BlockRows(d, blockY) : end) * rowBytes;
            if (compressed)
            {
                // What a block decodes to is known once it is decoded, but no codec expands its data beyond a fixed ratio: a
                // block too small to give the rows it must is damaged, found before the destination is allocated. (A JPEG
                // stream whose data runs out decodes to grey, as libjpeg does: its size says nothing of its rows.)
                if (!jpeg && needed > MostDecoded(d.Compression, d.ByteCounts[block]))
                {
                    throw budget.Invalid($"{kind} {block} of {d.ByteCounts[block]} {d.Compression} bytes cannot decode to the {needed} bytes "
                        + "its rows need: the file is damaged or truncated.");
                }

                if (Streamed(d, needed))
                {
                    // Read through a stream and inflated a few rows at a time: no buffer of the block, but the buffer of the read
                    // and the state of zlib (about 128 KB, counted with the rows).
                    largestDecoded = Math.Max(largestDecoded, StreamRows(rowBytes) * rowBytes + StreamState);
                }
                else
                {
                    largestCompressed = Math.Max(largestCompressed, CompressedToRead(d.Compression, d.ByteCounts[block], needed));
                    largestDecoded = Math.Max(largestDecoded, needed);
                }
            }
            else if (needed > d.ByteCounts[block])
            {
                throw budget.Invalid($"{kind} {block} holds {d.ByteCounts[block]} bytes, {needed} are needed: the file is truncated.");
            }

            units += (end - first + rowsPerUnit - 1) / rowsPerUnit;
            rowsRead += end - first;
        }

        if (largestCompressed > Array.MaxLength || largestDecoded > Array.MaxLength)
        {
            throw new NotSupportedException(
                $"{budget.SourceName}: compressed {kind}s of more than {Array.MaxLength} bytes are not supported; read the image by region.");
        }

        if (!compressed && d.Layout == TiffLayout.Strips)
        {
            // Strips that follow each other are read together (Units): about one unit a chunk of rows, not one a strip.
            units = Math.Min(units, (rowsRead + rowsPerUnit - 1) / rowsPerUnit);
        }

        // The scratch buffers of the workers are counted, so that a huge row or tile cannot make them allocate more than allowed.
        int degree = budget.Options.MaxDegreeOfParallelism is -1 ? Environment.ProcessorCount : budget.Options.MaxDegreeOfParallelism;
        // They are allocated once per worker, of exactly these sizes (never rounded up by a pool, never kept after the read).
        plan.Workers = parallel ? (int)Math.Max(1, Math.Min(Math.Min(degree, maxWorkers), units)) : 1;
        plan.JpegTables = jpegTables;
        plan.ReadBytes = compressed ? largestCompressed : rowsPerUnit * rowBytes;
        plan.DecodedBytes = largestDecoded;
        plan.RowSamples = d.BlockWidth * (d.SamplesPerPixel + 2);
        long scratch = plan.ReadBytes + plan.DecodedBytes + plan.RowSamples * sampleBytes + plan.PredictorBytes;
        budget.ReservePixels(plan.Workers * scratch, "the decoding buffers");
        return plan;
    }

    /// <summary>
    /// The compressed bytes of a block read to decode <paramref name="needed"/> bytes: no codec needs more than twice what it gives
    /// (LZW at most 1.5 times, PackBits 2 bytes for a lone byte, Deflate a few bytes per stored block), with some room for headers
    /// and for seeing data beyond the rows; JPEG at most 26 bits per sample, doubled by byte stuffing, and its markers (a frame
    /// with all its tables is about 3 KB). A hostile file whose blocks all declare the whole file costs what its pixels need.
    /// </summary>
    internal static long CompressedToRead(TiffCompression compression, long byteCount, long needed) =>
        Math.Min(byteCount, compression == TiffCompression.Jpeg ? 8 * needed + 16384 : 2 * needed + 4096);

    /// <summary>The rows of a block: the whole tile, or the rows of the strip in the image (what a JPEG frame covers).</summary>
    internal static long BlockRows(ImageDescriptor d, long blockY) =>
        d.Layout == TiffLayout.Tiles ? d.BlockHeight : Math.Min(d.BlockHeight, d.Height - blockY);

    /// <summary>
    /// The most bytes <paramref name="count"/> compressed bytes can decode to. PackBits: 2 bytes give at most 128. Deflate: at
    /// most 1032 bytes per byte (258 bytes per match of at least 2 bits), plus the zlib header and checksum. LZW: a code of at least
    /// 9 bits gives at most a string of 4096 bytes.
    /// </summary>
    internal static long MostDecoded(TiffCompression compression, long count) => compression switch
    {
        TiffCompression.PackBits => count * 64,
        TiffCompression.Deflate => count * 1032 + 1024,
        _ => count * 8 / 9 * 4096 + 4096,
    };

    /// <summary>The units of work covering the stored rectangle, block after block (plane by plane for planar images).</summary>
    /// <remarks>
    /// Uncompressed strips that follow each other in the file are read together, up to a chunk: a file of many small strips (the
    /// 8 KB of libtiff) costs one read a megabyte, not one a strip.
    /// </remarks>
    public IEnumerable<PixelUnit> Units()
    {
        bool merge = !Compressed && Descriptor.Layout == TiffLayout.Strips;
        PixelUnit? open = null;
        long openEnd = 0;   // the offset in the file after the rows of the open unit
        foreach (var (block, plane, blockY, first, end) in Blocks())
        {
            long x0 = block % Across * Descriptor.BlockWidth;
            for (long row = first; row < end;)
            {
                if (merge && open is { } u && u.Plane == plane && u.Y0 + u.Rows == blockY + row && u.Rows < RowsPerUnit
                    && openEnd == Descriptor.Offsets[block] + row * RowBytes)
                {
                    int more = (int)Math.Min(RowsPerUnit - u.Rows, end - row);
                    open = u with { Rows = u.Rows + more };
                    openEnd += more * RowBytes;
                    row += more;
                    continue;
                }

                if (open is { } done)
                {
                    yield return done;
                }

                int rows = (int)Math.Min(RowsPerUnit, end - row);
                open = new PixelUnit(block, plane, x0, blockY + row, (int)row, rows);
                openEnd = Descriptor.Offsets[block] + (row + rows) * RowBytes;
                row += rows;
            }
        }

        if (open is { } last)
        {
            yield return last;
        }
    }

    private long Across => (Descriptor.Width + Descriptor.BlockWidth - 1) / Descriptor.BlockWidth;

    /// <summary>The blocks under the stored rectangle, with the rows [first, end) the read takes from each.</summary>
    private IEnumerable<(int Block, int Plane, long BlockY, long First, long End)> Blocks()
    {
        var d = Descriptor;
        var r = Stored;
        long across = Across;
        long down = (d.Height + d.BlockHeight - 1) / d.BlockHeight;
        foreach (int plane in Planes(d, Request.Channel))
        {
            for (long by = r.Y0 / d.BlockHeight; by * d.BlockHeight < r.Y1; by++)
            {
                long blockY = by * d.BlockHeight;
                long first = Math.Max(r.Y0, blockY) - blockY;
                long end = Math.Min(Math.Min(r.Y1, blockY + d.BlockHeight), d.Height) - blockY;
                for (long bx = r.X0 / d.BlockWidth; bx * d.BlockWidth < r.X1; bx++)
                {
                    yield return ((int)(plane * across * down + by * across + bx), plane, blockY, first, end);
                }
            }
        }
    }

    /// <summary>The planes a request needs: one for an interleaved image; for a planar one, only the plane of the requested channel.</summary>
    private static IEnumerable<int> Planes(ImageDescriptor d, int? channel)
    {
        if (!d.Planar)
        {
            return [0];
        }

        if (channel is { } c)
        {
            // A palette image delivers channels 0 to 2 from plane 0.
            return [d.Photometric == TiffPhotometric.Palette ? Math.Max(0, c - 2) : c];
        }

        return Enumerable.Range(0, d.SamplesPerPixel);
    }
}

/// <summary>One piece of work: rows [FirstRow, FirstRow + Rows) of one strip or tile (of one plane), at (X0, Y0) in the stored image.</summary>
internal readonly record struct PixelUnit(int Block, int Plane, long X0, long Y0, int FirstRow, int Rows);

/// <summary>The rectangle of the stored image that holds a region of the delivered one: [X0, X1) x [Y0, Y1).</summary>
internal readonly record struct StoredRect(long X0, long Y0, long X1, long Y1)
{
    /// <summary>The stored rectangle under a region of the delivered image (its corners mapped back).</summary>
    public static StoredRect Of(ImageDescriptor d, PixelRequest q)
    {
        long w = d.Width, h = d.Height;
        (long, long) Stored(long vx, long vy) => d.Orientation switch
        {
            2 => (w - 1 - vx, vy),
            3 => (w - 1 - vx, h - 1 - vy),
            4 => (vx, h - 1 - vy),
            5 => (vy, vx),
            6 => (vy, h - 1 - vx),
            7 => (w - 1 - vy, h - 1 - vx),
            8 => (w - 1 - vy, vx),
            _ => (vx, vy),
        };
        var (ax, ay) = Stored(q.X, q.Y);
        var (bx, by) = Stored(q.X + q.Width - 1, q.Y + q.Height - 1);
        return new StoredRect(Math.Min(ax, bx), Math.Min(ay, by), Math.Max(ax, bx) + 1, Math.Max(ay, by) + 1);
    }
}

/// <summary>
/// Reads the pixels of an image along a checked <see cref="PixelPlan"/>: decodes its units in parallel into pooled buffers, and
/// puts each sample in its place in the destination (orientation, region, order, channel, conversion), straight from the stored rows.
/// </summary>
/// <remarks>
/// Uncompressed strips are read in chunks of rows, never whole, so that an image stored in one strip costs its destination and a
/// few buffers, not twice its size. The writes of the workers are disjoint: the result does not depend on the number of threads.
/// </remarks>
internal static class PixelReader
{
    public static void Read<TD, TOut>(PixelPlan plan, TiffSource source, ResourceBudget budget, int channels, bool littleEndian,
        Span<TOut> destination, Memory<TOut>? memory, ScratchCache? cache = null)
        where TD : unmanaged, INumber<TD>
        where TOut : unmanaged, INumber<TOut>
    {
        // The buffers of the workers come from a cache: the read's own, or the caller's (the writer reads band after band with
        // the same buffers). Parallel.ForEach starts new tasks as it goes; each takes buffers from the cache and puts them back,
        // so that there are never more than one set per worker running at once (as reserved by the plan).
        cache ??= new ScratchCache();
        var context = new Context<TD, TOut>(plan, source, budget, channels, littleEndian);
        if (memory is not { } array || plan.Workers == 1)
        {
            var scratch = Scratch<TD>.Take(cache, plan);
            try
            {
                foreach (var unit in plan.Units())
                {
                    budget.ThrowIfCancelled();
                    context.Process(unit, destination, scratch);
                    plan.Progress?.Add();
                }
            }
            finally
            {
                cache.Put(scratch);
            }

            return;
        }

        try
        {
            Parallel.ForEach(plan.Units(), new ParallelOptions { MaxDegreeOfParallelism = plan.Workers }, () => Scratch<TD>.Take(cache, plan),
                (unit, _, scratch) =>
                {
                    budget.ThrowIfCancelled();
                    context.Process(unit, array.Span, scratch);
                    plan.Progress?.Add();
                    return scratch;
                },
                cache.Put);
        }
        catch (AggregateException ex) when (ex.InnerExceptions.Count > 0)
        {
            // The first failure, as it was thrown (a TiffException, the caller's IOException, a cancellation).
            ExceptionDispatchInfo.Throw(ex.InnerExceptions[0]);
        }
    }

    /// <summary>
    /// The buffers of one worker, allocated when first needed, of the sizes the plan reserved, and reused for every unit the worker
    /// decodes. Their contents are never read before they are written.
    /// </summary>
    private sealed class Scratch<TD> where TD : unmanaged
    {
        private PixelPlan _plan;
        private byte[]? _read;
        private byte[]? _decoded;
        private TD[]? _row;
        private byte[]? _predictor;

        private Scratch(PixelPlan plan) => _plan = plan;

        public byte[] Read => _read = Fit(_read, _plan.ReadBytes);

        public byte[] Decoded => _decoded = Fit(_decoded, _plan.DecodedBytes);

        public TD[] Row => _row is { } row && row.Length >= _plan.RowSamples ? row : _row = GC.AllocateUninitializedArray<TD>((int)_plan.RowSamples);

        public byte[] Predictor => _predictor = Fit(_predictor, _plan.PredictorBytes);

        /// <summary>Buffers from the cache (large enough ones are reused for another plan of the same read or writer), or new ones.</summary>
        public static Scratch<TD> Take(ScratchCache cache, PixelPlan plan)
        {
            // Buffers of another sample type are left to the collector: a cache serves one image, of one type.
            if (cache.TryTake(out object? item) && item is Scratch<TD> scratch)
            {
                scratch._plan = plan;
                return scratch;
            }

            return new Scratch<TD>(plan);
        }

        private static byte[] Fit(byte[]? buffer, long size) =>
            buffer is not null && buffer.Length >= size ? buffer : GC.AllocateUninitializedArray<byte>((int)size);
    }

    /// <summary>The state of one read, shared by its workers (read only).</summary>
    private sealed class Context<TD, TOut>
        where TD : unmanaged, INumber<TD>
        where TOut : unmanaged, INumber<TOut>
    {
        private readonly ImageDescriptor _d;
        private readonly TiffSource _source;
        private readonly ResourceBudget _budget;
        private readonly bool _compressed;
        private readonly JpegTables? _jpegTables;
        private readonly TiffPredictor _predictor;
        private readonly PixelRequest _request;
        private readonly StoredRect _stored;
        private readonly int _channels;
        private readonly long _rowBytes;
        private readonly bool _little;
        private readonly int _orientation;
        private readonly double[]? _maxima;
        private readonly bool _identity;
        private readonly bool _direct;
        private readonly bool _directSwap;
        private readonly bool _uniformMaxima;

        public Context(PixelPlan plan, TiffSource source, ResourceBudget budget, int channels, bool littleEndian)
        {
            _d = plan.Descriptor;
            _source = source;
            _budget = budget;
            _compressed = plan.Compressed;
            _jpegTables = plan.JpegTables;
            _predictor = plan.Predictor;
            _request = plan.Request;
            _stored = plan.Stored;
            _channels = channels;
            _little = littleEndian;
            _orientation = _d.Orientation;
            _rowBytes = plan.RowBytes;
            _identity = typeof(TD) == typeof(TOut) && _request.Scale == TiffPixelScale.Stored;
            // Unpacking is a plain copy: uncompressed, whole bytes of the delivered type, in the byte order of the machine, nothing
            // to invert, expand or reverse.
            // Big-endian samples too: read in place, then their bytes swapped.
            _direct = !_compressed && _identity && _d.BitsPerSample == Unsafe.SizeOf<TD>() * 8
                && !_d.ReversedBits && _d.Photometric is not (TiffPhotometric.MinIsWhite or TiffPhotometric.Palette);
            _directSwap = _direct && _d.BitsPerSample > 8 && littleEndian != BitConverter.IsLittleEndian;
            _maxima = _request.Scale == TiffPixelScale.Normalized ? Maxima(_d, channels) : null;
            _uniformMaxima = _maxima is null || Array.TrueForAll(_maxima, m => m == _maxima[0]);
        }

        public void Process(PixelUnit unit, Span<TOut> destination, Scratch<TD> scratch)
        {
            int deliveredPerPixel = SampleUnpacker.DeliveredChannels(_d, unit.Plane);
            int rowSamples = (int)(_d.BlockWidth * deliveredPerPixel);
            bool straight = Straight(unit);
            if (straight && _direct && unit.X0 == _request.X && _d.BlockWidth == _request.Width)
            {
                // The stored rows are the delivered ones, whole and contiguous: read straight into the destination, one copy.
                long at = (unit.Y0 - _request.Y) * _request.Width * _channels;
                var target = MemoryMarshal.AsBytes(destination.Slice((int)at, unit.Rows * rowSamples));
                _source.ReadExactly(_d.Offsets[unit.Block] + unit.FirstRow * _rowBytes, target);
                if (_directSwap)
                {
                    SampleUnpacker.Swap(target, _d.BitsPerSample / 8);
                }

                return;
            }

            if (_compressed && PixelPlan.Streamed(_d, (unit.FirstRow + unit.Rows) * _rowBytes))
            {
                Inflate(unit, destination, scratch, deliveredPerPixel, rowSamples, straight);
                return;
            }

            Span<byte> rows;
            if (_compressed)
            {
                rows = Decode(unit, scratch);
            }
            else
            {
                // The plan checked that the block holds these rows.
                rows = scratch.Read.AsSpan(0, (int)(unit.Rows * _rowBytes));
                _source.ReadExactly(_d.Offsets[unit.Block] + unit.FirstRow * _rowBytes, rows);
            }

            for (int r = 0; r < unit.Rows; r++)
            {
                Deliver(unit, r, rows.Slice((int)(r * _rowBytes), (int)_rowBytes), destination, scratch, deliveredPerPixel, rowSamples, straight);
            }
        }

        /// <summary>Row <paramref name="r"/> of a unit (decoded, predictor undone) into its place in the destination.</summary>
        private void Deliver(PixelUnit unit, long r, Span<byte> stored, Span<TOut> destination, Scratch<TD> scratch, int deliveredPerPixel,
            int rowSamples, bool straight)
        {
            if (straight)
            {
                // Nothing to reorder or convert: the row is unpacked straight into its place in the destination.
                long at = ((unit.Y0 + r - _request.Y) * _request.Width + (unit.X0 - _request.X)) * _channels;
                Span<TD> target = MemoryMarshal.Cast<TOut, TD>(destination.Slice((int)at, rowSamples));
                SampleUnpacker.Unpack<TD>(_d, unit.Plane, stored, (int)_d.BlockWidth, target, _little);
                return;
            }

            Span<TD> row = scratch.Row.AsSpan(0, rowSamples);
            SampleUnpacker.Unpack<TD>(_d, unit.Plane, stored, (int)_d.BlockWidth, row, _little);
            Place(unit, unit.Y0 + r, row, deliveredPerPixel, destination);
        }

        /// <summary>
        /// A large Deflate block, inflated as a stream read from the file, a few rows at a time: the rows before the unit are
        /// inflated and dropped, those of the unit delivered as they come. Bounded as <see cref="Decode"/> is: the compressed bytes a
        /// read can need, the rows the unit needs.
        /// </summary>
        private void Inflate(PixelUnit unit, Span<TOut> destination, Scratch<TD> scratch, int deliveredPerPixel, int rowSamples, bool straight)
        {
            int block = unit.Block;
            long last = unit.FirstRow + unit.Rows;
            long count = PixelPlan.CompressedToRead(_d.Compression, _d.ByteCounts[block], last * _rowBytes);
            string kind = _d.Layout == TiffLayout.Tiles ? "tile" : "strip";
            int chunkRows = PixelPlan.StreamRows(_rowBytes);
            Span<byte> chunk = scratch.Decoded.AsSpan(0, (int)(chunkRows * _rowBytes));
            var range = _source.OpenRange(_d.Offsets[block], count);
            try
            {
                using var zlib = new ZLibStream(new BufferedStream(range, 1 << 16), CompressionMode.Decompress);
                for (long row = 0; row < last; row += chunkRows)
                {
                    _budget.ThrowIfCancelled();
                    int n = (int)Math.Min(chunkRows, last - row);
                    Span<byte> part = chunk[..(int)(n * _rowBytes)];
                    int got = zlib.ReadAtLeast(part, part.Length, throwOnEndOfStream: false);
                    if (got < part.Length)
                    {
                        throw _budget.Invalid($"{kind} {block} decodes to {row * _rowBytes + got} bytes, {last * _rowBytes} are needed: the file is damaged or truncated.");
                    }

                    for (int r = 0; r < n; r++)
                    {
                        long stored = row + r;
                        if (stored < unit.FirstRow)
                        {
                            continue;
                        }

                        Span<byte> rowBytes = part.Slice((int)(r * _rowBytes), (int)_rowBytes);
                        UndoPredictor(rowBytes, scratch);
                        Deliver(unit, stored - unit.FirstRow, rowBytes, destination, scratch, deliveredPerPixel, rowSamples, straight);
                    }
                }

                long blockRows = PixelPlan.BlockRows(_d, unit.Y0 - unit.FirstRow);
                Span<byte> one = stackalloc byte[1];
                if (_budget.Options.Strict && last == blockRows && zlib.Read(one) > 0)
                {
                    throw _budget.Invalid($"{kind} {block} holds more data than its {blockRows} rows of {_rowBytes} bytes.");
                }
            }
            catch (IOException ex) when (ex is not EndOfStreamException)
            {
                // A failure of the file or the stream of the application is its own, given as it is; zlib reports damaged data as
                // an InvalidDataException or a ZLibException (an IOException).
                if (range.Failure is { } failure)
                {
                    System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(failure);
                }

                throw _budget.Invalid($"{kind} {block} cannot be decoded ({_d.Compression}): {ex.Message}");
            }
            catch (InvalidDataException ex)
            {
                throw _budget.Invalid($"{kind} {block} cannot be decoded ({_d.Compression}): {ex.Message}");
            }
        }

        /// <summary>Undoes the predictor of one decoded row, in place.</summary>
        private void UndoPredictor(Span<byte> row, Scratch<TD> scratch)
        {
            if (_predictor == TiffPredictor.Horizontal)
            {
                Predictors.UndoHorizontal(row, _d.BitsPerSample / 8, SampleUnpacker.StoredSamples(_d), _little);
            }
            else if (_predictor == TiffPredictor.FloatingPoint)
            {
                Predictors.UndoFloatingPoint(row, scratch.Predictor, _d.BitsPerSample / 8, SampleUnpacker.StoredSamples(_d), _little);
            }
        }

        /// <summary>
        /// Decodes a compressed block up to the last row of the unit; returns the rows of the unit. The output is bounded by what
        /// the unit needs: a block that would decode to more stops there, one that decodes to less is damaged.
        /// </summary>
        private Span<byte> Decode(PixelUnit unit, Scratch<TD> scratch)
        {
            int block = unit.Block;
            long blockRows = PixelPlan.BlockRows(_d, unit.Y0 - unit.FirstRow);
            bool jpeg = _d.Compression == TiffCompression.Jpeg;
            int needed = (int)((jpeg ? blockRows : unit.FirstRow + unit.Rows) * _rowBytes);
            int count = (int)PixelPlan.CompressedToRead(_d.Compression, _d.ByteCounts[block], needed);
            string kind = _d.Layout == TiffLayout.Tiles ? "tile" : "strip";
            byte[] compressed = scratch.Read;
            _source.ReadExactly(_d.Offsets[block], compressed.AsSpan(0, count));
            if (jpeg)
            {
                bool lastStrip = _d.Layout == TiffLayout.Strips && unit.Y0 - unit.FirstRow + blockRows == _d.Height;
                DecodeJpeg(compressed.AsSpan(0, count), scratch, needed, (int)blockRows, lastStrip, kind, block);
                return scratch.Decoded.AsSpan((int)(unit.FirstRow * _rowBytes), (int)(unit.Rows * _rowBytes));
            }

            // Data beyond the block is ignored, as libtiff does, unless the read is strict and the whole block is decoded.
            bool whole = unit.FirstRow + unit.Rows == blockRows;
            Span<byte> output = scratch.Decoded.AsSpan(0, needed);
            int written;
            bool overflow;
            try
            {
                written = _d.Compression switch
                {
                    TiffCompression.Lzw => Lzw.Decode(compressed.AsSpan(0, count), output, out overflow),
                    TiffCompression.PackBits => PackBits.Decode(compressed.AsSpan(0, count), output, out overflow),
                    _ => Deflate.Decode(compressed, count, output, _budget.Options.Strict && whole, out overflow),
                };
            }
            catch (InvalidDataException ex)
            {
                throw _budget.Invalid($"{kind} {block} cannot be decoded ({_d.Compression}): {ex.Message}");
            }

            if (written < needed)
            {
                throw _budget.Invalid($"{kind} {block} decodes to {written} bytes, {needed} are needed: the file is damaged or truncated.");
            }

            if (overflow && whole && _budget.Options.Strict)
            {
                throw _budget.Invalid($"{kind} {block} holds more data than its {blockRows} rows of {_rowBytes} bytes.");
            }

            Span<byte> rows = output.Slice((int)(unit.FirstRow * _rowBytes), (int)(unit.Rows * _rowBytes));
            if (_predictor != TiffPredictor.None)
            {
                // Rows are predicted independently: only the rows of the unit are undone.
                int bytesPerSample = _d.BitsPerSample / 8;
                int stride = SampleUnpacker.StoredSamples(_d);
                for (int r = 0; r < unit.Rows; r++)
                {
                    Span<byte> row = rows.Slice((int)(r * _rowBytes), (int)_rowBytes);
                    if (_predictor == TiffPredictor.Horizontal)
                    {
                        Predictors.UndoHorizontal(row, bytesPerSample, stride, _little);
                    }
                    else
                    {
                        Predictors.UndoFloatingPoint(row, scratch.Predictor, bytesPerSample, stride, _little);
                    }
                }
            }

            return rows;
        }

        /// <summary>
        /// Decodes the JPEG stream of a block (Technical Note 2): after the tables of JPEGTables, a frame as wide as the block and
        /// as high as its rows (the last strip may be higher, as some writers make it, and libtiff reads it), into the stored rows.
        /// YCbCr is converted to RGB, as libtiff and tifffile deliver it; other components are given as they are.
        /// </summary>
        private void DecodeJpeg(ReadOnlySpan<byte> stream, Scratch<TD> scratch, int needed, int blockRows, bool lastStrip, string kind, int block)
        {
            // The last strip may be higher than its rows (as libtiff accepts): its planes are still counted in the budget.
            int maxHeight = lastStrip ? ushort.MaxValue : blockRows;
            var decoder = _jpegTables is { } tables
                ? new JpegDecoder(tables, _budget, (int)_d.BlockWidth, maxHeight)
                : new JpegDecoder(_budget, (int)_d.BlockWidth, maxHeight);
            try
            {
                var frame = decoder.Decode(stream);
                if (frame.Width != _d.BlockWidth || frame.Height < blockRows)
                {
                    throw new InvalidDataException($"its frame of {frame.Width} x {frame.Height} does not cover the {_d.BlockWidth} x {blockRows} pixels of the {kind}.");
                }

                if (frame.Components.Length != _d.SamplesPerPixel)
                {
                    throw new InvalidDataException($"its frame has {frame.Components.Length} components for {_d.SamplesPerPixel} samples per pixel.");
                }

                // The buffer of the rows is taken once the stream is decoded: a stream refused in its scans never costs it.
                JpegColour.ToSamples(frame, blockRows, _d.Photometric == TiffPhotometric.YCbCr, scratch.Decoded.AsSpan(0, needed), (int)_rowBytes, _budget);
            }
            catch (InvalidDataException ex)
            {
                throw _budget.Invalid($"{kind} {block} cannot be decoded (JPEG): {ex.Message}");
            }
            finally
            {
                decoder.Release();
            }
        }

        /// <summary>
        /// Whether the rows of a unit go to the destination as they are: same type and scale, top-left orientation, interleaved
        /// samples of every channel, and the whole width of the block inside the region (so that no column is cut).
        /// </summary>
        private bool Straight(PixelUnit unit) => _identity && _orientation == 1 && !_d.Planar && _request.Channel is null
            && _request.Order == TiffPixelOrder.Interleaved && unit.X0 >= _request.X
            && unit.X0 + _d.BlockWidth <= _request.X + (long)_request.Width;

        /// <summary>Puts the samples of stored row <paramref name="sy"/> in their places in the destination.</summary>
        private void Place(PixelUnit unit, long sy, ReadOnlySpan<TD> row, int perPixel, Span<TOut> destination)
        {
            var q = _request;
            long x0 = Math.Max(unit.X0, _stored.X0);
            long x1 = Math.Min(Math.Min(unit.X0 + _d.BlockWidth, _stored.X1), _d.Width);
            if (x1 <= x0)
            {
                return;
            }

            if (_orientation == 1)
            {
                PlaceRun(unit, sy, row, perPixel, destination, x0, (int)(x1 - x0));
                return;
            }

            // Where the first sample of the row goes, and how far the next pixel of the row is in the destination.
            var (vx, vy) = Visual(x0, sy);
            var (dx, dy) = Step();
            int outChannels = q.Channel is null ? _channels : 1;
            bool planar = q.Order == TiffPixelOrder.Planar && q.Channel is null;
            long pixelStep = planar ? dx + dy * (long)q.Width : (dx + dy * (long)q.Width) * outChannels;
            long index = planar
                ? (vy - q.Y) * q.Width + (vx - q.X)
                : ((vy - q.Y) * q.Width + (vx - q.X)) * outChannels;
            long planeSize = (long)q.Width * q.Height;
            int firstChannel = _d.Planar ? ChannelOfPlane(unit.Plane) : 0;

            int from = (int)((x0 - unit.X0) * perPixel);
            for (long x = x0; x < x1; x++, index += pixelStep, from += perPixel)
            {
                for (int c = 0; c < perPixel; c++)
                {
                    int channel = firstChannel + c;
                    if (q.Channel is { } wanted)
                    {
                        if (channel != wanted)
                        {
                            continue;
                        }

                        destination[(int)index] = Convert(row[from + c], channel);
                        continue;
                    }

                    long at = planar ? index + channel * planeSize : index + channel;
                    destination[(int)at] = Convert(row[from + c], channel);
                }
            }
        }

        /// <summary>
        /// <see cref="Place"/> for a stored row that is a delivered row (Orientation 1): each channel is a run, contiguous or
        /// every few samples, converted as a whole instead of sample by sample.
        /// </summary>
        private void PlaceRun(PixelUnit unit, long sy, ReadOnlySpan<TD> row, int perPixel, Span<TOut> destination, long x0, int count)
        {
            var q = _request;
            int from = (int)((x0 - unit.X0) * perPixel);
            long pixel = (sy - q.Y) * q.Width + (x0 - q.X);
            int firstChannel = _d.Planar ? ChannelOfPlane(unit.Plane) : 0;
            if (q.Channel is { } wanted)
            {
                int c = wanted - firstChannel;
                if (c >= 0 && c < perPixel)
                {
                    RowConversion<TD, TOut>.Strided(row, from + c, perPixel, destination, (int)pixel, 1, count, _identity, Maximum(wanted));
                }

                return;
            }

            if (q.Order == TiffPixelOrder.Planar)
            {
                // A FITS cube: each channel to its plane, contiguous.
                long planeSize = (long)q.Width * q.Height;
                for (int c = 0; c < perPixel; c++)
                {
                    int channel = firstChannel + c;
                    RowConversion<TD, TOut>.Strided(row, from + c, perPixel, destination, (int)(pixel + channel * planeSize), 1, count,
                        _identity, Maximum(channel));
                }

                return;
            }

            long at = pixel * _channels + firstChannel;
            if (perPixel == _channels && _uniformMaxima)
            {
                // Interleaved samples of every channel: one run.
                RowConversion<TD, TOut>.Run(row.Slice(from, count * perPixel), destination.Slice((int)at, count * perPixel), _identity, Maximum(0));
                return;
            }

            // A plane of the file into interleaved pixels, or channels of different maxima (a palette): channel by channel.
            for (int c = 0; c < perPixel; c++)
            {
                RowConversion<TD, TOut>.Strided(row, from + c, perPixel, destination, (int)(at + c), _channels, count, _identity,
                    Maximum(firstChannel + c));
            }
        }

        /// <summary>The divisor of a channel for a normalised read, <see cref="double.NaN"/> for a stored one.</summary>
        private double Maximum(int channel) => _maxima is null ? double.NaN : _maxima[channel];

        private int ChannelOfPlane(int plane) =>
            _d.Photometric == TiffPhotometric.Palette && plane > 0 ? plane + 2 : plane;

        /// <summary>A sample as delivered: unchanged, converted, or divided by the largest value of its channel (as numpy does).</summary>
        private TOut Convert(TD value, int channel)
        {
            if (_identity)
            {
                return Unsafe.As<TD, TOut>(ref value);
            }

            return _maxima is null
                ? TOut.CreateTruncating(value)
                : TOut.CreateTruncating(double.CreateTruncating(value) / _maxima[channel]);
        }

        /// <summary>The delivered (visual) position of stored pixel (sx, sy), TIFF 6.0 Orientation.</summary>
        private (long X, long Y) Visual(long sx, long sy)
        {
            long w = _d.Width, h = _d.Height;
            return _orientation switch
            {
                2 => (w - 1 - sx, sy),
                3 => (w - 1 - sx, h - 1 - sy),
                4 => (sx, h - 1 - sy),
                5 => (sy, sx),
                6 => (h - 1 - sy, sx),
                7 => (h - 1 - sy, w - 1 - sx),
                8 => (sy, w - 1 - sx),
                _ => (sx, sy),
            };
        }

        /// <summary>How the visual position moves when the stored column grows by one.</summary>
        private (int Dx, int Dy) Step() => _orientation switch
        {
            2 or 3 => (-1, 0),
            5 or 6 => (0, 1),
            7 or 8 => (0, -1),
            _ => (1, 0),
        };

        /// <summary>
        /// The largest value of each delivered channel: 1 for floating point, the largest positive value for signed samples, the
        /// largest value of the stored depth otherwise; the 16-bit colour map for the colours of a palette image.
        /// </summary>
        private static double[] Maxima(ImageDescriptor d, int channels)
        {
            double depth = d.SampleFormat switch
            {
                3 => 1.0,
                2 => Math.Pow(2, d.BitsPerSample - 1) - 1,
                _ => Math.Pow(2, d.BitsPerSample) - 1,
            };
            var maxima = new double[channels];
            Array.Fill(maxima, depth);
            if (d.Photometric == TiffPhotometric.Palette)
            {
                maxima.AsSpan(0, 3).Fill(ushort.MaxValue);
            }

            return maxima;
        }
    }
}

/// <summary>Buffers of the workers of reads, kept between the reads of one caller (the bands of a write).</summary>
internal sealed class ScratchCache
{
    private readonly System.Collections.Concurrent.ConcurrentBag<object> _items = [];

    public bool TryTake(out object? item) => _items.TryTake(out item);

    public void Put(object item) => _items.Add(item);
}
