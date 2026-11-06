using Eleris.TiffNet.Internal.Codecs;
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
    public long MaxEncodedBytes()
    {
        long raw = RowBytes * BlockHeight;
        long perBlock = Compression switch
        {
            TiffCompression.None => raw,
            TiffCompression.PackBits => raw + (raw + 127) / 128 + BlockHeight,
            TiffCompression.Lzw => LzwEncoder.MaxEncoded((int)Math.Min(raw, int.MaxValue / 2)),
            _ => raw + raw / 1000 * 5 + 1024,
        };
        return perBlock * Blocks + 2L * Blocks;
    }

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

    /// <summary>Encodes block <paramref name="block"/>: reads its pixels, applies the predictor, compresses.</summary>
    public byte[] Encode(int block)
    {
        int plane = block / (Across * Down);
        int index = block % (Across * Down);
        int bx = index % Across, by = index / Across;
        int x0 = bx * BlockWidth, y0 = by * BlockHeight;
        int width = Math.Min(BlockWidth, Width - x0), height = Math.Min(BlockHeight, Height - y0);

        // A tile is whole (its part beyond the image is zeros); a strip has the rows it holds.
        int rows = Tiled ? BlockHeight : height;
        int rowBytes = (int)RowBytes;
        byte[] raw = new byte[rowBytes * rows];
        int samples = Planar ? 1 : Channels;
        if (width == BlockWidth)
        {
            _image.ReadBlock(x0, y0, width, height, Planar ? plane : null, raw.AsSpan(0, rowBytes * height));
        }
        else
        {
            int pieceRow = width * samples * BytesPerSample;
            byte[] piece = new byte[pieceRow * height];
            _image.ReadBlock(x0, y0, width, height, Planar ? plane : null, piece);
            for (int r = 0; r < height; r++)
            {
                piece.AsSpan(r * pieceRow, pieceRow).CopyTo(raw.AsSpan(r * rowBytes));
            }
        }

        return BlockCodec.Encode(raw, rows, rowBytes, samples, BytesPerSample, Predictor, Compression, Level);
    }
}
