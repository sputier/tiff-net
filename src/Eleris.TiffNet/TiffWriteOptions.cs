namespace Eleris.TiffNet;

/// <summary>How a TIFF file is written: compression, prediction, layout, BigTIFF, threads.</summary>
/// <remarks>
/// <para>
/// The defaults (<see cref="Default"/>) write Deflate with the predictor that suits the samples, in strips, as classic TIFF unless
/// the file needs BigTIFF: lossless, compact, and readable by every TIFF reader. JPEG is never written: it is lossy, and a TIFF
/// meant for processing should not be.
/// </para>
/// <para>Change an option with a <c>with</c> expression: <c>TiffWriteOptions.Default with { Layout = TiffLayout.Tiles }</c>.</para>
/// </remarks>
public sealed record TiffWriteOptions
{
    private readonly TiffCompression _compression = TiffCompression.Deflate;
    private readonly TiffCompressionLevel _level = TiffCompressionLevel.Optimal;
    private readonly TiffPredictor _predictor = TiffPredictor.Auto;
    private readonly TiffLayout _layout = TiffLayout.Strips;
    private readonly int _tileSize = 256;
    private readonly TiffPixelOrder _planarConfiguration = TiffPixelOrder.Interleaved;
    private readonly TiffBigTiff _bigTiff = TiffBigTiff.Auto;
    private readonly int _maxDegreeOfParallelism = -1;

    /// <summary>Creates the default options; set the ones to change with an object initializer.</summary>
    public TiffWriteOptions()
    {
    }

    /// <summary>The default options: Deflate, the predictor that suits the samples, strips, classic TIFF unless BigTIFF is needed.</summary>
    public static TiffWriteOptions Default { get; } = new();

    /// <summary>The compression: <see cref="TiffCompression.Deflate"/> by default; None, LZW and PackBits too.</summary>
    /// <exception cref="NotSupportedException">The value is <see cref="TiffCompression.Jpeg"/>: JPEG is read, not written.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a compression of the enumeration.</exception>
    public TiffCompression Compression
    {
        get => _compression;
        init => _compression = value switch
        {
            TiffCompression.None or TiffCompression.Lzw or TiffCompression.Deflate or TiffCompression.PackBits => value,
            TiffCompression.Jpeg => throw new NotSupportedException(
                "JPEG is not written: it is lossy. Write Deflate (the default), LZW, PackBits or no compression."),
            _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Not a compression the library writes."),
        };
    }

    /// <summary>How hard Deflate works: <see cref="TiffCompressionLevel.Optimal"/> by default.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a level of the enumeration.</exception>
    public TiffCompressionLevel Level
    {
        get => _level;
        init => _level = Enum.IsDefined(value) ? value : throw new ArgumentOutOfRangeException(nameof(value), value, "Not a compression level.");
    }

    /// <summary>
    /// The predictor, with LZW and Deflate: <see cref="TiffPredictor.Auto"/> (the default) chooses horizontal differencing for
    /// integers of 8 to 32 bits, the floating point predictor for floating point samples, and none for 64-bit integers (where it
    /// gains nothing). With no compression or PackBits, no predictor is written.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a predictor of the enumeration.</exception>
    public TiffPredictor Predictor
    {
        get => _predictor;
        init => _predictor = Enum.IsDefined(value) ? value : throw new ArgumentOutOfRangeException(nameof(value), value, "Not a predictor.");
    }

    /// <summary>Strips of whole rows (about 256 KB each, the default) or square tiles of <see cref="TileSize"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a layout of the enumeration.</exception>
    public TiffLayout Layout
    {
        get => _layout;
        init => _layout = Enum.IsDefined(value) ? value : throw new ArgumentOutOfRangeException(nameof(value), value, "Not a layout.");
    }

    /// <summary>The width and height of the tiles, a multiple of 16 (TIFF 6.0): 256 by default.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a positive multiple of 16.</exception>
    public int TileSize
    {
        get => _tileSize;
        init => _tileSize = value > 0 && value % 16 == 0 && value <= 65536
            ? value
            : throw new ArgumentOutOfRangeException(nameof(value), value, "Tiles are a positive multiple of 16 pixels wide, at most 65536.");
    }

    /// <summary>The samples of a pixel together (<see cref="TiffPixelOrder.Interleaved"/>, the default) or each channel in its own plane.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not an order of the enumeration.</exception>
    public TiffPixelOrder PlanarConfiguration
    {
        get => _planarConfiguration;
        init => _planarConfiguration = Enum.IsDefined(value) ? value : throw new ArgumentOutOfRangeException(nameof(value), value, "Not an order.");
    }

    /// <summary>
    /// Whether a save leaves out the pages the library cannot read (<see cref="TiffImage.UnsupportedReason"/>: a CMYK thumbnail
    /// beside the main image, for example) instead of failing with a <see cref="NotSupportedException"/>; false by default, so that
    /// no page is lost without the application asking. It concerns the pages: sub-images are not written in this version.
    /// </summary>
    public bool SkipUnsupportedPages { get; init; }

    /// <summary>Classic TIFF or BigTIFF: <see cref="TiffBigTiff.Auto"/> (the default) writes BigTIFF only when the file could exceed 4 GB.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a choice of the enumeration.</exception>
    public TiffBigTiff BigTiff
    {
        get => _bigTiff;
        init => _bigTiff = Enum.IsDefined(value) ? value : throw new ArgumentOutOfRangeException(nameof(value), value, "Not a BigTIFF choice.");
    }

    /// <summary>The threads that encode strips or tiles at once: -1 (the default) for every core, 1 to write on the calling thread only.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is neither -1 nor positive.</exception>
    public int MaxDegreeOfParallelism
    {
        get => _maxDegreeOfParallelism;
        init => _maxDegreeOfParallelism = value is -1 or > 0
            ? value
            : throw new ArgumentOutOfRangeException(nameof(value), value, "-1 for every core, or a positive number of threads.");
    }

    /// <summary>
    /// Options that write an image as the file it was read from was stored: the same compression (JPEG, which is not written,
    /// becomes Deflate, as do the compressions the library does not write), predictor, layout and tile size (square tiles whose
    /// side is a multiple of 16; 256 otherwise), planar configuration, and BigTIFF when the file was BigTIFF.
    /// </summary>
    /// <param name="storage">How the image was stored (<see cref="TiffImage.Storage"/>).</param>
    /// <returns>The options; the level and the threads are the defaults.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="storage"/> is null.</exception>
    public static TiffWriteOptions From(TiffStorage storage)
    {
        ArgumentNullException.ThrowIfNull(storage);
        bool written = storage.Compression is TiffCompression.None or TiffCompression.Lzw or TiffCompression.Deflate or TiffCompression.PackBits;
        bool squareTiles = storage.Layout == TiffLayout.Tiles && storage.TileWidth == storage.TileHeight && storage.TileWidth % 16 == 0
            && storage.TileWidth is > 0 and <= 65536;
        return new TiffWriteOptions
        {
            Compression = written ? storage.Compression : TiffCompression.Deflate,
            Predictor = written && Enum.IsDefined(storage.Predictor) ? storage.Predictor : TiffPredictor.Auto,
            Layout = storage.Layout,
            TileSize = squareTiles ? storage.TileWidth : 256,
            PlanarConfiguration = storage.PlanarConfiguration,
            BigTiff = storage.BigTiff ? TiffBigTiff.Always : TiffBigTiff.Auto,
        };
    }

    /// <summary>The threads to use.</summary>
    internal int Degree => MaxDegreeOfParallelism == -1 ? Environment.ProcessorCount : MaxDegreeOfParallelism;
}
