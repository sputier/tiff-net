using System.Buffers.Binary;

namespace Eleris.TiffNet.Internal.Jpeg;

/// <summary>One component of a decoded frame: its samples at its own resolution, in a plane padded to whole MCUs.</summary>
internal sealed class JpegComponent
{
    public required int Id { get; init; }

    /// <summary>The horizontal sampling factor (1 to 4).</summary>
    public required int H { get; init; }

    /// <summary>The vertical sampling factor (1 to 4).</summary>
    public required int V { get; init; }

    public required int QuantizationTable { get; init; }

    /// <summary>The width of the component, in samples: the frame width scaled by H / Hmax, rounded up (A.1.1).</summary>
    public required int Width { get; init; }

    public required int Height { get; init; }

    /// <summary>The samples, row after row; the plane covers whole MCUs, beyond <see cref="Width"/> x <see cref="Height"/>.</summary>
    /// <remarks>Allocated with the frame for a sequential image, after the last scan for a progressive one.</remarks>
    public byte[] Plane { get; internal set; } = [];

    /// <summary>The rows of <see cref="Plane"/> (whole MCUs).</summary>
    internal int PlaneRows { get; init; }

    /// <summary>The bytes of a row of <see cref="Plane"/>.</summary>
    public required int Stride { get; init; }

    internal int DcTable { get; set; }

    internal int AcTable { get; set; }

    internal int Prediction { get; set; }

    internal bool Scanned { get; set; }

    /// <summary>The blocks of a row of <see cref="Plane"/> (whole MCUs).</summary>
    internal int BlocksAcross => Stride / 8;

    /// <summary>A progressive image: the coefficients of every block, 64 per block, refined scan after scan.</summary>
    internal short[]? Coefficients { get; init; }

    /// <summary>A progressive image: the bit of each coefficient known so far (-1: none), to tell scans that refine nothing.</summary>
    internal int[]? KnownBits { get; init; }

    /// <summary>The quantization table, latched at the first scan of the component (as libjpeg does).</summary>
    internal ushort[]? Quantization { get; set; }
}
