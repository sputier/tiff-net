using System.Buffers.Binary;

namespace Eleris.TiffNet.Internal.Jpeg;

/// <summary>A decoded JPEG frame: its size, its components (before any colour conversion), and what its markers say about colour.</summary>
internal sealed class JpegFrame
{
    public required int Width { get; init; }

    public required int Height { get; init; }

    public required JpegComponent[] Components { get; init; }

    /// <summary>Whether the frame is progressive (SOF2): its coefficients are transformed once its last scan is read.</summary>
    public bool Progressive { get; init; }

    /// <summary>The colour transform of an Adobe APP14 marker (0: none, 1: YCbCr), or null without that marker.</summary>
    public int? AdobeTransform { get; set; }

    /// <summary>Whether the stream has a JFIF APP0 marker (its components are then YCbCr).</summary>
    public bool Jfif { get; set; }
}
