using System.IO.Compression;

namespace Eleris.TiffNet.Internal.Codecs;

/// <summary>The Deflate decoder (compression 8, and 32946 of the first writers): a zlib stream, inflated by System.IO.Compression.</summary>
internal static class Deflate
{
    /// <summary>Inflates the compressed bytes straight into <paramref name="destination"/>; returns the number of bytes written.</summary>
    /// <param name="source">An array holding the compressed bytes of a strip or tile.</param>
    /// <param name="length">The number of compressed bytes at the start of <paramref name="source"/>.</param>
    /// <param name="destination">The decoded bytes wanted: the inflation stops when it is full.</param>
    /// <param name="checkOverflow">Whether to look for data beyond the destination (one more byte inflated).</param>
    /// <param name="overflow">Whether the stream goes on beyond the destination (always false unless checked).</param>
    /// <exception cref="InvalidDataException">The stream is damaged (whether zlib reports it as invalid data or as an error).</exception>
    public static int Decode(byte[] source, int length, Span<byte> destination, bool checkOverflow, out bool overflow)
    {
        using var compressed = new MemoryStream(source, 0, length, writable: false);
        using var zlib = new ZLibStream(compressed, CompressionMode.Decompress);
        try
        {
            int written = zlib.ReadAtLeast(destination, destination.Length, throwOnEndOfStream: false);
            Span<byte> one = stackalloc byte[1];
            overflow = checkOverflow && written == destination.Length && zlib.Read(one) > 0;
            return written;
        }
        catch (IOException ex)
        {
            // zlib reports some damaged streams as a ZLibException (an IOException): the data is in memory, it is damaged data.
            throw new InvalidDataException(ex.Message, ex);
        }
    }
}
