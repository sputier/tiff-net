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

    private readonly TiffImage _image;

    public ImageEncoder(TiffImage image, TiffWriteOptions options)
    {
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
            BlockHeight = (int)Math.Clamp(StripBytes / Math.Max(1, rowBytes), 1, Height);
        }

        Across = (Width + BlockWidth - 1) / BlockWidth;
        Down = (Height + BlockHeight - 1) / BlockHeight;
        Planes = Planar ? Channels : 1;
        RowBytes = (long)BlockWidth * samples * BytesPerSample;
        if (RowBytes * BlockHeight > Array.MaxLength)
        {
            throw new ArgumentException($"Blocks of {BlockWidth} x {BlockHeight} pixels are larger than an array: use smaller tiles.");
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
    /// The rows of the image read at once, a multiple of the block height. Each block of the source is decoded once: a band covers
    /// whole source strips or tiles (an image stored on its side, Orientation 5 to 8, is read whole, since each of its rows crosses
    /// every source block); a source in memory or uncompressed needs only enough blocks to keep the threads busy.
    /// </summary>
    private int BandRows(int degree)
    {
        long sourceRows = 1;
        if (_image.File is not null && _image.Storage is { } storage && storage.Compression != TiffCompression.None)
        {
            var d = _image.Descriptor;
            sourceRows = d.Orientation >= 5 ? Height : d.BlockHeight;
        }
        else if (_image.File is not null && _image.Descriptor.Orientation >= 5)
        {
            sourceRows = Height;
        }

        long wanted = Math.Max(sourceRows, (long)BlockHeight * (Tiled ? 1 : 8 * Math.Max(1, degree)));
        long rows = (wanted + BlockHeight - 1) / BlockHeight * BlockHeight;
        return (int)Math.Min(rows, (Height + BlockHeight - 1L) / BlockHeight * BlockHeight);
    }

    /// <summary>
    /// Encodes the blocks of the image and appends them: band after band of rows, each read once from the image (in parallel),
    /// its blocks encoded in parallel and appended in order (every plane of a band before the next band).
    /// </summary>
    public void Write(FileWriter writer, long[] offsets, long[] counts, int degree, CancellationToken cancellationToken)
    {
        int bandRows = BandRows(degree);
        int pixelBytes = Channels * BytesPerSample;
        long bandBytes = (long)bandRows * Width * pixelBytes;
        if (bandBytes > Array.MaxLength)
        {
            throw new NotSupportedException(
                $"Writing this image reads {bandRows} rows at once ({bandBytes} bytes), more than an array holds: write it with TiffImageWriter.");
        }

        int blockRowsPerBand = bandRows / BlockHeight;
        for (int bandY = 0; bandY < Height; bandY += bandRows)
        {
            int rows = Math.Min(bandRows, Height - bandY);
            _image.ValidateBand(bandY, rows);
            byte[] band = System.Buffers.ArrayPool<byte>.Shared.Rent(rows * Width * pixelBytes);
            try
            {
                _image.ReadBand(bandY, rows, band.AsMemory(0, rows * Width * pixelBytes), degree, cancellationToken);
                int firstRow = bandY / BlockHeight;
                int blockRows = Math.Min(blockRowsPerBand, Down - firstRow);
                int perPlane = blockRows * Across;
                BlockPipeline.Run(perPlane * Planes, degree,
                    j => EncodeFromBand(band, bandY, j / perPlane, firstRow + j % perPlane / Across, j % perPlane % Across),
                    (j, encoded) =>
                    {
                        int block = j / perPlane * Across * Down + (firstRow + j % perPlane / Across) * Across + j % perPlane % Across;
                        offsets[block] = writer.Append(encoded.Span);
                        counts[block] = encoded.Length;
                        encoded.Return();
                    },
                    encoded => encoded.Return(),
                    cancellationToken);
            }
            finally
            {
                System.Buffers.ArrayPool<byte>.Shared.Return(band);
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
            for (int x = 0; x < width; x++)
            {
                source.Slice(x * pixelBytes + plane * BytesPerSample, BytesPerSample).CopyTo(target[(x * BytesPerSample)..]);
            }
        }

        return BlockCodec.Encode(raw, rows, rowBytes, Planar ? 1 : Channels, BytesPerSample, Predictor, Compression, Level);
    }
}
