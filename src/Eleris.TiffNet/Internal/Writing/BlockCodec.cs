using Eleris.TiffNet.Internal.Codecs;

namespace Eleris.TiffNet.Internal.Writing;

/// <summary>Predicts and compresses one block (a strip or a tile) of raw samples, for both writers.</summary>
internal static class BlockCodec
{
    /// <summary>Encodes <paramref name="rows"/> rows of <paramref name="rowBytes"/> bytes; the predictor works in place on <paramref name="raw"/>.</summary>
    public static byte[] Encode(byte[] raw, int rows, int rowBytes, int samples, int bytesPerSample, TiffPredictor predictor,
        TiffCompression compression, TiffCompressionLevel level)
    {
        if (predictor != TiffPredictor.None)
        {
            byte[]? scratch = predictor == TiffPredictor.FloatingPoint ? new byte[rowBytes] : null;
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

        return compression switch
        {
            TiffCompression.None => raw,
            TiffCompression.PackBits => PackBits(raw, rowBytes, rows),
            TiffCompression.Lzw => Lzw(raw),
            _ => DeflateEncoder.Encode(raw, level),
        };
    }

    private static byte[] PackBits(byte[] raw, int rowBytes, int rows)
    {
        byte[] output = new byte[PackBitsEncoder.MaxEncoded(rowBytes) * rows];
        int length = 0;
        for (int r = 0; r < rows; r++)
        {
            length += PackBitsEncoder.EncodeRow(raw.AsSpan(r * rowBytes, rowBytes), output.AsSpan(length));
        }

        return output[..length];
    }

    private static byte[] Lzw(byte[] raw)
    {
        byte[] output = new byte[LzwEncoder.MaxEncoded(raw.Length)];
        return output[..LzwEncoder.Encode(raw, output)];
    }
}
