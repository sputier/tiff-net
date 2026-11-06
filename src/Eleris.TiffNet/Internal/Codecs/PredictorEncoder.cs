using System.Runtime.InteropServices;

namespace Eleris.TiffNet.Internal.Codecs;

/// <summary>Applies the predictors of TIFF to a row before it is compressed (the inverse of <see cref="Predictors"/>).</summary>
internal static class PredictorEncoder
{
    /// <summary>Horizontal differencing, in place: each sample minus the one <paramref name="stride"/> before it.</summary>
    /// <param name="row">A row of whole samples, in the order of the machine (little endian).</param>
    /// <param name="bytesPerSample">1, 2, 4 or 8.</param>
    /// <param name="stride">The samples per pixel of the row (1 for a plane).</param>
    public static void Horizontal(Span<byte> row, int bytesPerSample, int stride)
    {
        switch (bytesPerSample)
        {
            case 1:
                Difference(row, stride);
                break;
            case 2:
                Difference(MemoryMarshal.Cast<byte, ushort>(row), stride);
                break;
            case 4:
                Difference(MemoryMarshal.Cast<byte, uint>(row), stride);
                break;
            default:
                Difference(MemoryMarshal.Cast<byte, ulong>(row), stride);
                break;
        }
    }

    /// <summary>
    /// The floating point predictor (Technical Note 3), in place: the bytes of the samples split into planes, most significant
    /// first, then differenced with a stride of <paramref name="stride"/> bytes.
    /// </summary>
    /// <param name="row">A row of whole samples, little endian.</param>
    /// <param name="scratch">At least the length of <paramref name="row"/>.</param>
    /// <param name="bytesPerSample">2, 4 or 8.</param>
    /// <param name="stride">The samples per pixel of the row (1 for a plane).</param>
    public static void FloatingPoint(Span<byte> row, Span<byte> scratch, int bytesPerSample, int stride)
    {
        int count = row.Length / bytesPerSample;
        for (int b = 0; b < bytesPerSample; b++)
        {
            // Plane b holds byte (bytesPerSample - 1 - b) of each sample: the most significant first.
            var plane = scratch.Slice(b * count, count);
            int at = bytesPerSample - 1 - b;
            for (int i = 0; i < count; i++)
            {
                plane[i] = row[i * bytesPerSample + at];
            }
        }

        scratch[..row.Length].CopyTo(row);
        Difference(row, stride);
    }

    /// <summary>v[i] -= v[i - stride], from the end, so that every value is taken before it changes; vectorised.</summary>
    private static void Difference<T>(Span<T> values, int stride) where T : unmanaged, System.Numerics.IBinaryInteger<T>
    {
        int n = values.Length;
        int i = n;
        int lanes = System.Numerics.Vector<T>.Count;
        if (System.Numerics.Vector.IsHardwareAccelerated)
        {
            // Each vector is loaded before it is stored: the values it reads below it are not changed yet.
            while (i - lanes >= stride)
            {
                i -= lanes;
                var current = new System.Numerics.Vector<T>(values[i..]);
                var previous = new System.Numerics.Vector<T>(values[(i - stride)..]);
                (current - previous).CopyTo(values[i..]);
            }
        }

        for (int k = i - 1; k >= stride; k--)
        {
            values[k] -= values[k - stride];
        }
    }
}
