using System.Buffers;
using System.Numerics;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using Eleris.TiffNet.Internal.Structure;

namespace Eleris.TiffNet.Internal.Pixels;

/// <summary>What a read asks for: a region of the delivered (oriented) image, its channels, their order and scale.</summary>
internal readonly record struct PixelRequest(int X, int Y, int Width, int Height, int? Channel, TiffPixelOrder Order, TiffPixelScale Scale)
{
    public long Count(int channels) => (long)Width * Height * (Channel is null ? channels : 1);
}

/// <summary>
/// Reads the pixels of an image: plans the strips or tiles a region needs, decodes them in parallel into pooled buffers, and puts
/// each sample in its place in the destination (orientation, region, order, channel, conversion), straight from the stored rows.
/// </summary>
/// <remarks>
/// Uncompressed strips are read in chunks of rows, never whole, so that an image stored in one strip costs its destination and a
/// few buffers, not twice its size. The writes of the workers are disjoint: the result does not depend on the number of threads.
/// </remarks>
internal static class PixelReader
{
    /// <summary>The bytes of stored rows read at once from an uncompressed strip or tile.</summary>
    private const int ChunkBytes = 1 << 20;

    /// <summary>One piece of work: rows of one strip or tile (of one plane).</summary>
    private readonly record struct Unit(int Block, int Plane, long X0, long Y0, int FirstRow, int Rows);

    public static void Read<TD, TOut>(TiffImage image, TiffSource source, ResourceBudget budget, PixelRequest request, Span<TOut> destination,
        TOut[]? array, bool littleEndian)
        where TD : unmanaged, INumber<TD>
        where TOut : unmanaged, INumber<TOut>
    {
        var d = image.Descriptor;
        if (d.Compression != TiffCompression.None)
        {
            throw new NotSupportedException($"{source.Name}: reading {d.Compression} compressed images is not implemented yet.");
        }

        var context = new Context<TD, TOut>(d, source, budget, request, image.Channels, littleEndian);
        var units = Plan(d, context.Stored, request.Channel);

        // The scratch buffers of the workers are counted, so that a huge row or tile cannot make them allocate more than allowed.
        int degree = budget.Options.MaxDegreeOfParallelism is -1 ? Environment.ProcessorCount : budget.Options.MaxDegreeOfParallelism;
        int workers = array is null ? 1 : Math.Max(1, Math.Min(degree, units.Count));
        budget.ReservePixels(workers * context.ScratchBytes, "the decoding buffers");

        if (array is null || workers == 1)
        {
            foreach (var unit in units)
            {
                budget.ThrowIfCancelled();
                context.Process(unit, destination);
            }

            return;
        }

        try
        {
            Parallel.ForEach(units, new ParallelOptions { MaxDegreeOfParallelism = workers }, unit =>
            {
                budget.ThrowIfCancelled();
                context.Process(unit, array);
            });
        }
        catch (AggregateException ex) when (ex.InnerExceptions.Count > 0)
        {
            // The first failure, as it was thrown (a TiffException, the caller's IOException, a cancellation).
            ExceptionDispatchInfo.Throw(ex.InnerExceptions[0]);
        }
    }

    /// <summary>The units of work covering the stored rectangle, block after block (plane by plane for planar images).</summary>
    private static List<Unit> Plan(ImageDescriptor d, StoredRect r, int? channel)
    {
        var units = new List<Unit>();
        long rowBytes = ImageDescriptor.BytesPerRow(d.BlockWidth, SampleUnpacker.StoredSamples(d), d.BitsPerSample);
        int rowsPerUnit = (int)Math.Clamp(ChunkBytes / Math.Max(1, rowBytes), 1, d.BlockHeight);
        long across = (d.Width + d.BlockWidth - 1) / d.BlockWidth;
        long down = (d.Height + d.BlockHeight - 1) / d.BlockHeight;
        foreach (int plane in Planes(d, channel))
        {
            for (long by = r.Y0 / d.BlockHeight; by * d.BlockHeight < r.Y1; by++)
            {
                for (long bx = r.X0 / d.BlockWidth; bx * d.BlockWidth < r.X1; bx++)
                {
                    int block = (int)(plane * across * down + by * across + bx);
                    long blockY = by * d.BlockHeight;
                    long first = Math.Max(r.Y0, blockY) - blockY;
                    long end = Math.Min(Math.Min(r.Y1, blockY + d.BlockHeight), d.Height) - blockY;
                    for (long row = first; row < end; row += rowsPerUnit)
                    {
                        units.Add(new Unit(block, plane, bx * d.BlockWidth, blockY + row, (int)row, (int)Math.Min(rowsPerUnit, end - row)));
                    }
                }
            }
        }

        return units;
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

    /// <summary>The rectangle of the stored image that holds a region of the delivered one: [X0, X1) x [Y0, Y1).</summary>
    internal readonly record struct StoredRect(long X0, long Y0, long X1, long Y1);

    /// <summary>The state of one read, shared by its workers (read only).</summary>
    private sealed class Context<TD, TOut>
        where TD : unmanaged, INumber<TD>
        where TOut : unmanaged, INumber<TOut>
    {
        private readonly ImageDescriptor _d;
        private readonly TiffSource _source;
        private readonly ResourceBudget _budget;
        private readonly PixelRequest _request;
        private readonly int _channels;
        private readonly long _rowBytes;
        private readonly bool _little;
        private readonly int _orientation;
        private readonly double _scale;
        private readonly bool _identity;

        public Context(ImageDescriptor d, TiffSource source, ResourceBudget budget, PixelRequest request, int channels, bool littleEndian)
        {
            _d = d;
            _source = source;
            _budget = budget;
            _request = request;
            _channels = channels;
            _little = littleEndian;
            _orientation = d.Orientation;
            _rowBytes = ImageDescriptor.BytesPerRow(d.BlockWidth, SampleUnpacker.StoredSamples(d), d.BitsPerSample);
            Stored = StoredRectOf(d, request);
            _identity = typeof(TD) == typeof(TOut) && request.Scale == TiffPixelScale.Stored;
            _scale = request.Scale == TiffPixelScale.Normalized ? 1.0 / Maximum(d) : 1.0;
            long rows = Math.Clamp(ChunkBytes / Math.Max(1, _rowBytes), 1, d.BlockHeight);
            ScratchBytes = rows * _rowBytes + d.BlockWidth * Math.Max(d.Channels, d.SamplesPerPixel) * Marshal.SizeOf<TD>();
        }

        public StoredRect Stored { get; }

        public long ScratchBytes { get; }

        public void Process(Unit unit, Span<TOut> destination)
        {
            long length = unit.Rows * _rowBytes;
            long start = (long)unit.FirstRow * _rowBytes;
            if (start + length > _d.ByteCounts[unit.Block])
            {
                string kind = _d.Layout == TiffLayout.Tiles ? "tile" : "strip";
                throw _budget.Invalid($"{kind} {unit.Block} holds {_d.ByteCounts[unit.Block]} bytes, {start + length} are needed: the file is truncated.");
            }

            int deliveredPerPixel = SampleUnpacker.DeliveredChannels(_d, unit.Plane);
            int rowSamples = (int)(_d.BlockWidth * deliveredPerPixel);
            bool straight = Straight(unit);
            byte[] raw = ArrayPool<byte>.Shared.Rent((int)length);
            TD[] row = ArrayPool<TD>.Shared.Rent(rowSamples);
            try
            {
                _source.ReadExactly(_d.Offsets[unit.Block] + start, raw.AsSpan(0, (int)length));
                for (int r = 0; r < unit.Rows; r++)
                {
                    Span<byte> stored = raw.AsSpan((int)(r * _rowBytes), (int)_rowBytes);
                    if (straight)
                    {
                        // Nothing to reorder or convert: the row is unpacked straight into its place in the destination.
                        long at = ((unit.Y0 + r - _request.Y) * (long)_request.Width + (unit.X0 - _request.X)) * _channels;
                        Span<TD> target = MemoryMarshal.Cast<TOut, TD>(destination.Slice((int)at, rowSamples));
                        SampleUnpacker.Unpack<TD>(_d, unit.Plane, stored, (int)_d.BlockWidth, target, _little);
                        continue;
                    }

                    SampleUnpacker.Unpack<TD>(_d, unit.Plane, stored, (int)_d.BlockWidth, row.AsSpan(0, rowSamples), _little);
                    Place(unit, unit.Y0 + r, row.AsSpan(0, rowSamples), deliveredPerPixel, destination);
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(raw);
                ArrayPool<TD>.Shared.Return(row);
            }
        }

        /// <summary>
        /// Whether the rows of a unit go to the destination as they are: same type and scale, top-left orientation, interleaved
        /// samples of every channel, and the whole width of the block inside the region (so that no column is cut).
        /// </summary>
        private bool Straight(Unit unit) => _identity && _orientation == 1 && !_d.Planar && _request.Channel is null
            && _request.Order == TiffPixelOrder.Interleaved && unit.X0 >= _request.X
            && unit.X0 + _d.BlockWidth <= _request.X + (long)_request.Width;

        /// <summary>Puts the samples of stored row <paramref name="sy"/> in their places in the destination.</summary>
        private void Place(Unit unit, long sy, ReadOnlySpan<TD> row, int perPixel, Span<TOut> destination)
        {
            var q = _request;
            long x0 = Math.Max(unit.X0, Stored.X0);
            long x1 = Math.Min(Math.Min(unit.X0 + _d.BlockWidth, Stored.X1), _d.Width);
            if (x1 <= x0)
            {
                return;
            }

            // Where the first sample of the row goes, and how far the next pixel of the row is in the destination.
            var (vx, vy) = Visual(x0, sy);
            var (dx, dy) = Step();
            int outChannels = q.Channel is null ? _channels : 1;
            bool planar = q.Order == TiffPixelOrder.Planar && q.Channel is null;
            long pixelStep = planar ? dx + dy * (long)q.Width : (dx + dy * (long)q.Width) * outChannels;
            long index = planar
                ? (vy - q.Y) * (long)q.Width + (vx - q.X)
                : ((vy - q.Y) * (long)q.Width + (vx - q.X)) * outChannels;
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

                        destination[(int)index] = Convert(row[from + c]);
                        continue;
                    }

                    long at = planar ? index + channel * planeSize : index + channel;
                    destination[(int)at] = Convert(row[from + c]);
                }
            }
        }

        private int ChannelOfPlane(int plane) =>
            _d.Photometric == TiffPhotometric.Palette && plane > 0 ? plane + 2 : plane;

        private TOut Convert(TD value) => _identity
            ? System.Runtime.CompilerServices.Unsafe.As<TD, TOut>(ref value)
            : _scale == 1.0 ? TOut.CreateTruncating(value) : TOut.CreateTruncating(double.CreateTruncating(value) * _scale);

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

        private static double Maximum(ImageDescriptor d) => d.SampleFormat switch
        {
            3 => 1.0,
            2 => d.BitsPerSample switch { 8 => sbyte.MaxValue, 16 => short.MaxValue, 32 => int.MaxValue, _ => long.MaxValue },
            _ => d.Photometric == TiffPhotometric.Palette ? ushort.MaxValue : Math.Pow(2, d.BitsPerSample) - 1,
        };

        /// <summary>The stored rectangle under a region of the delivered image (its corners mapped back).</summary>
        private static StoredRect StoredRectOf(ImageDescriptor d, PixelRequest q)
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
}
