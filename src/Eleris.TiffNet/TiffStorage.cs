namespace Eleris.TiffNet;

/// <summary>
/// How an image is stored in the file it was read from: compression, layout, byte order… The pixels an image delivers do not
/// depend on it (they are always top-down, in the byte order of the machine); it tells what the file holds, and lets an
/// application write a file the same way.
/// </summary>
public sealed record TiffStorage
{
    internal TiffStorage()
    {
    }

    /// <summary>
    /// The compression of the samples; <see cref="TiffCompression.Unknown"/> for a code the enumeration does not name (the code is in
    /// <see cref="RawCompressionCode"/>; see also <see cref="TiffImage.UnsupportedReason"/>).
    /// </summary>
    public required TiffCompression Compression { get; init; }

    /// <summary>
    /// The Compression tag exactly as stored: 34713 for a Nikon NEF, for example. For a named compression it is the numeric value
    /// of <see cref="Compression"/>, except 32946, the first code of Deflate, which reads as <see cref="TiffCompression.Deflate"/>.
    /// </summary>
    public required int RawCompressionCode { get; init; }

    /// <summary>The prediction applied before compression.</summary>
    public required TiffPredictor Predictor { get; init; }

    /// <summary>Strips or tiles.</summary>
    public required TiffLayout Layout { get; init; }

    /// <summary>The width of a tile, or 0 for strips.</summary>
    public required int TileWidth { get; init; }

    /// <summary>The height of a tile, or 0 for strips.</summary>
    public required int TileHeight { get; init; }

    /// <summary>The number of rows of a strip (the last one may have fewer), or 0 for tiles.</summary>
    public required int RowsPerStrip { get; init; }

    /// <summary>Whether the channels of a pixel are stored together or each in its own plane.</summary>
    public required TiffPixelOrder PlanarConfiguration { get; init; }

    /// <summary>Whether the file is big endian ("MM").</summary>
    public required bool BigEndian { get; init; }

    /// <summary>Whether the file is BigTIFF (64-bit offsets).</summary>
    public required bool BigTiff { get; init; }

    /// <summary>The Orientation tag, 1 to 8, as stored. The delivered pixels always have the orientation applied.</summary>
    public required int Orientation { get; init; }

    /// <summary>
    /// What the stored samples mean (a palette or MinIsWhite image is delivered as RGB or grey, this says what it was);
    /// <see cref="TiffPhotometric.Unknown"/> for a code the enumeration does not name (the code is in <see cref="RawPhotometricCode"/>).
    /// </summary>
    public required TiffPhotometric Photometric { get; init; }

    /// <summary>The PhotometricInterpretation tag exactly as stored: 32803 for a CFA, for example. For a named value it is the numeric value of <see cref="Photometric"/>.</summary>
    public required int RawPhotometricCode { get; init; }

    /// <summary>The number of bits of a stored sample, 1 to 64 (a 12-bit sample is delivered in 16 bits; this says it had 12).</summary>
    public required int BitsPerSample { get; init; }
}
