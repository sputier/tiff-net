using System.Diagnostics.CodeAnalysis;

namespace Eleris.TiffNet;

/// <summary>The type of the samples an image delivers.</summary>
/// <remarks>Samples of 1 to 7 bits are delivered as <see cref="UInt8"/>, of 9 to 15 bits as <see cref="UInt16"/>, values unchanged.</remarks>
[SuppressMessage("Naming", "CA1720:Identifier contains type name", Justification = "The names say the size and kind of a sample.")]
public enum TiffSampleType
{
    /// <summary>Unsigned 8-bit integers (<see cref="byte"/>).</summary>
    UInt8 = 1,

    /// <summary>Signed 8-bit integers (<see cref="sbyte"/>).</summary>
    Int8 = 2,

    /// <summary>Unsigned 16-bit integers (<see cref="ushort"/>), the usual type of camera frames.</summary>
    UInt16 = 3,

    /// <summary>Signed 16-bit integers (<see cref="short"/>).</summary>
    Int16 = 4,

    /// <summary>Unsigned 32-bit integers (<see cref="uint"/>).</summary>
    UInt32 = 5,

    /// <summary>Signed 32-bit integers (<see cref="int"/>).</summary>
    Int32 = 6,

    /// <summary>Unsigned 64-bit integers (<see cref="ulong"/>).</summary>
    UInt64 = 7,

    /// <summary>Signed 64-bit integers (<see cref="long"/>).</summary>
    Int64 = 8,

    /// <summary>IEEE half precision floating point numbers (<see cref="Half"/>).</summary>
    Float16 = 9,

    /// <summary>IEEE single precision floating point numbers (<see cref="float"/>), the usual type of stacked images.</summary>
    Float32 = 10,

    /// <summary>IEEE double precision floating point numbers (<see cref="double"/>).</summary>
    Float64 = 11,
}

/// <summary>What the colour channels of an image are.</summary>
public enum TiffColorModel
{
    /// <summary>One channel of grey (or of any single quantity), followed by the extra channels.</summary>
    Grey = 1,

    /// <summary>Three channels, red, green and blue, followed by the extra channels.</summary>
    Rgb = 2,
}

/// <summary>What an extra channel (after the colour channels) holds.</summary>
public enum TiffExtraChannelKind
{
    /// <summary>Data with no meaning for the TIFF format: a band, a mask, a weight.</summary>
    Data = 0,

    /// <summary>Opacity, by which the colour channels are already multiplied (premultiplied alpha).</summary>
    AssociatedAlpha = 1,

    /// <summary>Opacity, independent of the colour channels.</summary>
    UnassociatedAlpha = 2,
}

/// <summary>The order of the samples in an array of pixels.</summary>
public enum TiffPixelOrder
{
    /// <summary>The channels of a pixel together: <c>[y][x][c]</c>.</summary>
    Interleaved = 0,

    /// <summary>One plane per channel: <c>[c][y][x]</c>, the order of a FITS cube.</summary>
    Planar = 1,
}

/// <summary>How samples are compressed in a file. The values are the codes of the TIFF <c>Compression</c> tag.</summary>
/// <remarks>A file can hold a code this enumeration does not name (a compression the library does not read); it is kept as it is.</remarks>
public enum TiffCompression
{
    /// <summary>No compression.</summary>
    None = 1,

    /// <summary>LZW (Lempel-Ziv-Welch).</summary>
    Lzw = 5,

    /// <summary>JPEG, as TIFF Technical Note 2 defines it (read only).</summary>
    Jpeg = 7,

    /// <summary>Deflate (zlib).</summary>
    Deflate = 8,

    /// <summary>PackBits (run lengths).</summary>
    PackBits = 32773,
}

/// <summary>The prediction applied to samples before their compression.</summary>
public enum TiffPredictor
{
    /// <summary>When writing: horizontal differencing for integers, the floating point predictor for floating point samples.</summary>
    Auto = 0,

    /// <summary>No prediction.</summary>
    None = 1,

    /// <summary>Horizontal differencing (TIFF 6.0).</summary>
    Horizontal = 2,

    /// <summary>Floating point prediction (TIFF Technical Note 3).</summary>
    FloatingPoint = 3,
}

/// <summary>How the samples of an image are divided in a file.</summary>
public enum TiffLayout
{
    /// <summary>Strips of whole rows.</summary>
    Strips = 0,

    /// <summary>Rectangular tiles.</summary>
    Tiles = 1,
}

/// <summary>The photometric interpretation of a file: what its samples mean. The values are the codes of the TIFF tag.</summary>
/// <remarks>A file can hold a code this enumeration does not name (CMYK, Lab…); it is kept as it is.</remarks>
public enum TiffPhotometric
{
    /// <summary>Grey, 0 being white.</summary>
    MinIsWhite = 0,

    /// <summary>Grey, 0 being black.</summary>
    MinIsBlack = 1,

    /// <summary>Red, green, blue.</summary>
    Rgb = 2,

    /// <summary>Indices into a colour map.</summary>
    Palette = 3,

    /// <summary>Luminance and chrominance (with JPEG compression).</summary>
    YCbCr = 6,
}
