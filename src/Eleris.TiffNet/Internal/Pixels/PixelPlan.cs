using System.IO.Compression;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using Eleris.TiffNet.Internal.Codecs;
using Eleris.TiffNet.Internal.Jpeg;
using Eleris.TiffNet.Internal.Structure;

namespace Eleris.TiffNet.Internal.Pixels;

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
