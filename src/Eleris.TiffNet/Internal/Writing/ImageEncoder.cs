using Eleris.TiffNet.Internal.Pixels;

namespace Eleris.TiffNet.Internal.Writing;

/// <summary>
/// How one image is written: its blocks (strips of about 256 KB, or square tiles; plane after plane when planar), the predictor
/// and the compression; and the encoding of each block, which reads its pixels from the image, independently of the others (so
/// that blocks can be encoded in parallel and the file does not depend on the number of threads).
/// </summary>
internal sealed class ImageEncoder
{
    /// <summary>The bytes of an uncompressed strip the writer aims at: large enough to compress well, small enough to read a region.</summary>
    internal const int StripBytes = 256 * 1024;

    /// <summary>The bytes of a band beyond which it no longer covers several source blocks that do not align with the output.</summary>
    private const long BandBudget = 64L << 20;

    private readonly TiffImage _image;

    /// <exception cref="NotSupportedException">The image cannot be read, or has more blocks than an array holds.</exception>
    /// <exception cref="TiffException">The image comes from a file that does not hold its pixels, or whose pixels exceed
    /// <see cref="TiffReadOptions.MaxPixelBytes"/>.</exception>
    public ImageEncoder(TiffImage image, TiffWriteOptions options)
    {
        // Before anything is allocated for it: an image of a file is checked whole, as reading it would be.
        image.ValidateForWrite();
        _image = image;
        Width = image.Width;
        Height = image.Height;
        Channels = image.Channels;
        SampleType = image.SampleType;
        BytesPerSample = SampleConversion.SizeOf(SampleType);
        Planar = options.PlanarConfiguration == TiffPixelOrder.Planar && Channels > 1;
        Compression = options.Compression;
        Level = options.Level;
        Predictor = ChoosePredictor(options.Predictor, options.Compression, SampleType);
        Tiled = options.Layout == TiffLayout.Tiles;
        int samples = Planar ? 1 : Channels;
        long rowBytes = (long)Width * samples * BytesPerSample;
        if (Tiled)
        {
            BlockWidth = BlockHeight = options.TileSize;
        }
        else
        {
            BlockWidth = Width;
            BlockHeight = StripRows((int)Math.Clamp(StripBytes / Math.Max(1, rowBytes), 1, Height), SourceRows(image), Height);
        }

        long across = (Width + (long)BlockWidth - 1) / BlockWidth, down = (Height + (long)BlockHeight - 1) / BlockHeight;
        Planes = Planar ? Channels : 1;
        if (across * down * Planes > Array.MaxLength)
        {
            throw new NotSupportedException(Tiled
                ? $"The image would have {across * down * Planes} tiles, more than a table holds: use larger tiles."
                : $"The image would have {across * down * Planes} strips, more than a table holds: write it in tiles.");
        }

        Across = (int)across;
        Down = (int)down;
        RowBytes = (long)BlockWidth * samples * BytesPerSample;
        if (RowBytes * BlockHeight > Array.MaxLength)
        {
            // Tiles too large are a choice of the options; a row larger than an array is the image itself.
            throw Tiled
                ? new ArgumentException($"Tiles of {BlockWidth} x {BlockHeight} pixels are larger than an array: use smaller tiles.")
                : new NotSupportedException($"Rows of {Width} pixels are larger than an array: write the image in tiles.");
        }
    }

    public int Width { get; }

    public int Height { get; }

    public int Channels { get; }

    public TiffSampleType SampleType { get; }

    public int BytesPerSample { get; }

    public bool Planar { get; }

    public bool Tiled { get; }

    public TiffCompression Compression { get; }

    public TiffCompressionLevel Level { get; }

    /// <summary>The predictor written: None, Horizontal or FloatingPoint (never Auto).</summary>
    public TiffPredictor Predictor { get; }

    /// <summary>The tile width, or the width of the image for strips.</summary>
    public int BlockWidth { get; }

    /// <summary>The tile height, or the rows per strip.</summary>
    public int BlockHeight { get; }

    public int Across { get; }

    public int Down { get; }

    public int Planes { get; }

    public long RowBytes { get; }

    public int Blocks => Across * Down * Planes;

    /// <summary>The structure the directory of the image describes.</summary>
    public ImageShape Shape(TiffImage image) => new(Width, Height, Channels, SampleType, image.ColorModel,
        [.. image.ExtraChannels.Select(c => c.Kind)], image.Descriptor.SubfileType, Tiled, Planar, BlockWidth, BlockHeight, Compression, Predictor);

    /// <summary>The most bytes the encoded image can take, to decide between classic TIFF and BigTIFF before writing.</summary>
    public long MaxEncodedBytes() => BlockCodec.MaxEncoded(Compression, RowBytes * BlockHeight, BlockHeight) * Blocks + 2L * Blocks;

    /// <summary>
    /// The predictor written. Auto: horizontal differencing for integers of 8 to 32 bits, the floating point predictor for floating
    /// point samples, none for 64-bit integers; and no predictor at all without LZW or Deflate (it would not be undone).
    /// </summary>
    /// <exception cref="ArgumentException">The floating point predictor is asked for integer samples.</exception>
    public static TiffPredictor ChoosePredictor(TiffPredictor asked, TiffCompression compression, TiffSampleType type)
    {
        bool floating = type is TiffSampleType.Float16 or TiffSampleType.Float32 or TiffSampleType.Float64;
        if (compression is not (TiffCompression.Lzw or TiffCompression.Deflate))
        {
            return TiffPredictor.None;
        }

        return asked switch
        {
            TiffPredictor.Auto when floating => TiffPredictor.FloatingPoint,
            TiffPredictor.Auto => type is TiffSampleType.UInt64 or TiffSampleType.Int64 ? TiffPredictor.None : TiffPredictor.Horizontal,
            TiffPredictor.FloatingPoint when !floating => throw new ArgumentException(
                $"The floating point predictor applies to floating point samples, not {type}."),
            _ => asked,
        };
    }

    /// <summary>
    /// The rows of the source decoded together: a compressed strip or a tile row (decoding a part costs the whole); the whole
    /// image when it is stored on its side (Orientation 5 to 8: each of its rows crosses every source block); one row otherwise
    /// (pixels in memory, or stored uncompressed).
    /// </summary>
    private static long SourceRows(TiffImage image)
    {
        var d = image.Descriptor;
        if (image.InMemory)
        {
            return 1;
        }

        if (d.Orientation >= 5)
        {
            return image.Height;
        }

        return d.Compression == TiffCompression.None ? 1 : d.BlockHeight;
    }

    /// <summary>
    /// The rows of an output strip: about <paramref name="target"/>, aligned on the source when it can be (a multiple of a short
    /// source strip, or a divisor of a long one), so that bands end where source blocks end.
    /// </summary>
    internal static int StripRows(int target, long source, int height)
    {
        if (source <= 1 || source >= height)
        {
            return target;
        }

        if (source <= target)
        {
            return (int)(source * (target / source));
        }

        for (int rows = target; rows >= Math.Max(1, target / 2); rows--)
        {
            if (source % rows == 0)
            {
                return rows;
            }
        }

        return target;
    }

    /// <summary>
    /// The rows of the image read at once: a multiple of the output block height that ends on source blocks (their least common
    /// multiple, when it is not too large; otherwise up to 8 source blocks), with enough output blocks for the threads. The
    /// blocks are appended in order whatever the band, so the file does not depend on the number of threads.
    /// </summary>
    internal int BandRows(int degree = 1)
    {
        long source = Math.Min(SourceRows(_image), Height);
        long common = source * BlockHeight / (long)System.Numerics.BigInteger.GreatestCommonDivisor(source, BlockHeight);
        long aligned = common;
        if (common > Math.Max(source, BlockHeight) * 16L)
        {
            // Without a common multiple of a fair size (tiles of 128 rows over strips of 262), a band covers up to 8 source blocks,
            // within 64 MB: the source block it ends in is decoded twice, at most 1/8 more work when memory allows.
            long rowBytes = (long)Width * Channels * BytesPerSample;
            long blocks = Math.Clamp(BandBudget / Math.Max(1, source * rowBytes), 1, 8);
            aligned = (source * blocks + BlockHeight - 1) / BlockHeight * BlockHeight;
        }

        // Enough blocks for the threads: 8 per thread, so that the pause between bands (the read of the next one) costs little.
        // Planar files take a fixed number, since the blocks of a band are written plane after plane: the file would otherwise
        // depend on the number of threads.
        // The threads ask for more blocks only within the budget of a band (a very wide image has large strips).
        long blockBytes = Math.Max(1, RowBytes * BlockHeight * Planes);
        int blocksWanted = Tiled ? 1 : Planar ? 16 : (int)Math.Max(16, Math.Min(8L * degree, BandBudget / blockBytes));
        long wanted = Math.Max(aligned, (long)BlockHeight * blocksWanted);
        long rows = (wanted + aligned - 1) / aligned * aligned;
        return (int)Math.Min(rows, (Height + BlockHeight - 1L) / BlockHeight * BlockHeight);
    }

    /// <summary>
    /// Encodes the blocks of the image and appends them: band after band of rows, each read once from the image (in parallel),
    /// its blocks encoded in parallel and appended in order (every plane of a band before the next band).
    /// </summary>
    public void Write(FileWriter writer, long[] offsets, long[] counts, int degree, CancellationToken cancellationToken,
        ProgressCounter? progress = null)
    {
        int bandRows = BandRows(degree);
        int pixelBytes = Channels * BytesPerSample;
        long bandBytes = (long)Math.Min(bandRows, Height) * Width * pixelBytes;
        if (bandBytes > Array.MaxLength)
        {
            throw new NotSupportedException(
                $"Writing this image reads {bandRows} rows at once ({bandBytes} bytes), more than an array holds: write it with TiffImageWriter.");
        }

        int blockRowsPerBand = bandRows / BlockHeight;

        // A large band (a whole single-strip source) is allocated for the write, not rented: the pool would round it up and keep it.
        // It is allocated once for every band, after the first band is checked against the limits.
        bool pooled = bandBytes <= 16 << 20;
        byte[]? large = null;
        var scratch = new Pixels.ScratchCache();
        for (int bandY = 0; bandY < Height; bandY += bandRows)
        {
            int rows = Math.Min(bandRows, Height - bandY);
            _image.ValidateBand(bandY, rows, degree);
            int bytes = rows * Width * pixelBytes;
            if (!pooled)
            {
                large ??= GC.AllocateUninitializedArray<byte>((int)bandBytes);
            }

            byte[] band = large ?? System.Buffers.ArrayPool<byte>.Shared.Rent(bytes);
            try
            {
                _image.ReadBand(bandY, rows, band.AsMemory(0, bytes), degree, scratch, cancellationToken);
                int firstRow = bandY / BlockHeight;
                int blockRows = Math.Min(blockRowsPerBand, Down - firstRow);
                int perPlane = blockRows * Across;
                BlockPipeline.Run(perPlane * Planes, degree,
                    j => EncodeFromBand(band, bandY, j / perPlane, firstRow + j % perPlane / Across, j % perPlane % Across),
                    (j, encoded) =>
                    {
                        int block = j / perPlane * Across * Down + (firstRow + j % perPlane / Across) * Across + j % perPlane % Across;
                        try
                        {
                            offsets[block] = writer.Append(encoded.Span);
                            counts[block] = encoded.Length;
                        }
                        finally
                        {
                            encoded.Return();
                        }

                        progress?.Add();
                    },
                    encoded => encoded.Return(),
                    cancellationToken);
            }
            finally
            {
                if (pooled)
                {
                    System.Buffers.ArrayPool<byte>.Shared.Return(band);
                }
            }
        }
    }

    /// <summary>Cuts block (<paramref name="bx"/>, <paramref name="by"/>) of a plane out of a band, then predicts and compresses it.</summary>
    private EncodedBlock EncodeFromBand(byte[] band, int bandY, int plane, int by, int bx)
    {
        int x0 = bx * BlockWidth, y0 = by * BlockHeight;
        int width = Math.Min(BlockWidth, Width - x0), height = Math.Min(BlockHeight, Height - y0);

        // A tile is whole (its part beyond the image is zeros); a strip has the rows it holds.
        int rows = Tiled ? BlockHeight : height;
        int rowBytes = (int)RowBytes;
        int pixelBytes = Channels * BytesPerSample;
        int bandRow = Width * pixelBytes;
        byte[] raw = System.Buffers.ArrayPool<byte>.Shared.Rent(rowBytes * rows);
        if (width < BlockWidth || height < rows)
        {
            raw.AsSpan(0, rowBytes * rows).Clear();
        }

        for (int r = 0; r < height; r++)
        {
            var source = band.AsSpan((y0 - bandY + r) * bandRow + x0 * pixelBytes, width * pixelBytes);
            var target = raw.AsSpan(r * rowBytes, rowBytes);
            if (!Planar)
            {
                source.CopyTo(target);
                continue;
            }

            // One channel of interleaved pixels.
            Writing.Channels.Extract(source, Channels, plane, BytesPerSample, width, target);
        }

        return BlockCodec.Encode(raw, rows, rowBytes, Planar ? 1 : Channels, BytesPerSample, Predictor, Compression, Level);
    }
}
