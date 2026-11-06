using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.Arm;
using System.Runtime.Intrinsics.X86;

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
        int done = bytesPerSample == 4 ? Split4(row, scratch, count) : 0;
        for (int b = 0; b < bytesPerSample; b++)
        {
            // Plane b holds byte (bytesPerSample - 1 - b) of each sample: the most significant first.
            var plane = scratch.Slice(b * count, count);
            int at = bytesPerSample - 1 - b;
            for (int i = done; i < count; i++)
            {
                plane[i] = row[i * bytesPerSample + at];
            }
        }

        scratch[..row.Length].CopyTo(row);
        Difference(row, stride);
    }

    /// <summary>
    /// The four byte planes of 32-bit little-endian samples, 16 samples at once (SSSE3 or AdvSimd): the bytes of each group of 4
    /// samples sorted by significance, then the 4 groups transposed. Returns the number of samples done.
    /// </summary>
    private static int Split4(ReadOnlySpan<byte> row, Span<byte> planes, int count)
    {
        if (!BitConverter.IsLittleEndian || !(Ssse3.IsSupported || AdvSimd.Arm64.IsSupported))
        {
            return 0;
        }

        // In each 16 bytes: the most significant bytes of the 4 samples, then the next ones…
        var bySignificance = Vector128.Create((byte)3, 7, 11, 15, 2, 6, 10, 14, 1, 5, 9, 13, 0, 4, 8, 12);
        int i = 0;
        for (; i + 16 <= count; i += 16)
        {
            var source = row[(i * 4)..];
            var t0 = Lookup(Vector128.Create<byte>(source), bySignificance).AsUInt32();
            var t1 = Lookup(Vector128.Create<byte>(source[16..]), bySignificance).AsUInt32();
            var t2 = Lookup(Vector128.Create<byte>(source[32..]), bySignificance).AsUInt32();
            var t3 = Lookup(Vector128.Create<byte>(source[48..]), bySignificance).AsUInt32();
            var a = ZipLow(t0, t1).AsUInt64();
            var b = ZipLow(t2, t3).AsUInt64();
            var c = ZipHigh(t0, t1).AsUInt64();
            var d = ZipHigh(t2, t3).AsUInt64();
            ZipLow(a, b).AsByte().CopyTo(planes[i..]);
            ZipHigh(a, b).AsByte().CopyTo(planes[(count + i)..]);
            ZipLow(c, d).AsByte().CopyTo(planes[(2 * count + i)..]);
            ZipHigh(c, d).AsByte().CopyTo(planes[(3 * count + i)..]);
        }

        return i;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector128<byte> Lookup(Vector128<byte> x, Vector128<byte> indices) =>
        Ssse3.IsSupported ? Ssse3.Shuffle(x, indices) : AdvSimd.Arm64.VectorTableLookup(x, indices);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector128<uint> ZipLow(Vector128<uint> a, Vector128<uint> b) => Sse2.IsSupported ? Sse2.UnpackLow(a, b) : AdvSimd.Arm64.ZipLow(a, b);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector128<uint> ZipHigh(Vector128<uint> a, Vector128<uint> b) => Sse2.IsSupported ? Sse2.UnpackHigh(a, b) : AdvSimd.Arm64.ZipHigh(a, b);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector128<ulong> ZipLow(Vector128<ulong> a, Vector128<ulong> b) => Sse2.IsSupported ? Sse2.UnpackLow(a, b) : AdvSimd.Arm64.ZipLow(a, b);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector128<ulong> ZipHigh(Vector128<ulong> a, Vector128<ulong> b) => Sse2.IsSupported ? Sse2.UnpackHigh(a, b) : AdvSimd.Arm64.ZipHigh(a, b);

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
