namespace Eleris.TiffNet.Internal.Jpeg;

/// <summary>A buffer of the caller a decoder may write a plane into, allocated only when it is asked for.</summary>
internal interface IOutputBuffer
{
    /// <summary>The bytes <see cref="Buffer"/> holds, known without allocating it.</summary>
    long Bytes { get; }

    /// <summary>The buffer (allocated on the first call).</summary>
    byte[] Buffer { get; }
}
