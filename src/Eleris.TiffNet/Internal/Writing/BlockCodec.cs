using System.Buffers;
using Eleris.TiffNet.Internal.Codecs;

namespace Eleris.TiffNet.Internal.Writing;

/// <summary>
/// An encoded block in a buffer rented from the shared pool (<see cref="Return"/> it once written), or bytes of the source
/// written as they are (<paramref name="Borrowed"/>, alive as long as the source, nothing to return).
/// </summary>
internal readonly record struct EncodedBlock(byte[]? Buffer, int Length, ReadOnlyMemory<byte> Borrowed = default)
{
    public ReadOnlySpan<byte> Span => Buffer is null ? Borrowed.Span[..Length] : Buffer.AsSpan(0, Length);

    public void Return()
    {
        if (Buffer is not null)
        {
            ArrayPool<byte>.Shared.Return(Buffer);
        }
    }
}

/// <summary>Predicts and compresses one block (a strip or a tile) of raw samples, for both writers, in pooled buffers.</summary>
internal static class BlockCodec
{
    /// <summary>
    /// Encodes <paramref name="rows"/> rows of <paramref name="rowBytes"/> bytes. Takes <paramref name="raw"/> (rented from the
    /// shared pool, changed in place by the predictor): it becomes the encoded block without compression, and is returned to the
    /// pool otherwise.
    /// </summary>
    public static EncodedBlock Encode(byte[] raw, int rows, int rowBytes, int samples, int bytesPerSample, TiffPredictor predictor,
        TiffCompression compression, TiffCompressionLevel level)
    {
        int length = rows * rowBytes;
        if (predictor != TiffPredictor.None)
        {
            byte[]? scratch = predictor == TiffPredictor.FloatingPoint ? ArrayPool<byte>.Shared.Rent(rowBytes) : null;
            try
            {
                for (int r = 0; r < rows; r++)
                {
                    var row = raw.AsSpan(r * rowBytes, rowBytes);
                    if (predictor == TiffPredictor.Horizontal)
                    {
                        PredictorEncoder.Horizontal(row, bytesPerSample, samples);
                    }
                    else
                    {
                        PredictorEncoder.FloatingPoint(row, scratch, bytesPerSample, samples);
                    }
                }
            }
            finally
            {
                if (scratch is not null)
                {
                    ArrayPool<byte>.Shared.Return(scratch);
                }
            }
        }

        if (compression == TiffCompression.None)
        {
            return new EncodedBlock(raw, length);
        }

        try
        {
            var source = raw.AsSpan(0, length);
            switch (compression)
            {
                case TiffCompression.PackBits:
                {
                    byte[] output = ArrayPool<byte>.Shared.Rent((int)(PackBitsEncoder.MaxEncoded(rowBytes) * rows));
                    int written = 0;
                    for (int r = 0; r < rows; r++)
                    {
                        written += PackBitsEncoder.EncodeRow(source.Slice(r * rowBytes, rowBytes), output.AsSpan(written));
                    }

                    return new EncodedBlock(output, written);
                }

                case TiffCompression.Lzw:
                {
                    byte[] output = ArrayPool<byte>.Shared.Rent((int)LzwEncoder.MaxEncoded(length));
                    return new EncodedBlock(output, LzwEncoder.Encode(source, output));
                }

                default:
                {
                    byte[] output = ArrayPool<byte>.Shared.Rent((int)DeflateEncoder.MaxEncoded(length));
                    return new EncodedBlock(output, DeflateEncoder.Encode(source, output, level, predictor));
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(raw);
        }
    }

    /// <summary>The most bytes a block of <paramref name="raw"/> bytes in <paramref name="rows"/> rows encodes to.</summary>
    public static long MaxEncoded(TiffCompression compression, long raw, int rows) => compression switch
    {
        TiffCompression.None => raw,
        TiffCompression.PackBits => PackBitsEncoder.MaxEncoded(raw / Math.Max(1, rows)) * rows,
        TiffCompression.Lzw => LzwEncoder.MaxEncoded(raw),
        _ => DeflateEncoder.MaxEncoded(raw),
    };
}
