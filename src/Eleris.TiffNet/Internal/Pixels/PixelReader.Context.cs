using System.IO.Compression;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using Eleris.TiffNet.Internal.Codecs;
using Eleris.TiffNet.Internal.Jpeg;
using Eleris.TiffNet.Internal.Structure;

namespace Eleris.TiffNet.Internal.Pixels;

internal static partial class PixelReader
{
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
