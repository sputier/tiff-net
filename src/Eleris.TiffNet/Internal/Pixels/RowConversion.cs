using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Eleris.TiffNet.Internal.Pixels;

/// <summary>
/// Converts runs of samples of a row from the unpacked type <typeparamref name="TD"/> to the delivered type
/// <typeparamref name="TOut"/>: unchanged (a copy), converted (<c>TOut.CreateTruncating</c>), or divided by the largest value of
/// their channel then converted (normalised). The common conversions of astronomical data (8, 16 and 32-bit integers to
/// <see cref="float"/> or <see cref="double"/>, floats to <see cref="double"/>) go through vectors; every result is the one of
/// the scalar conversion, bit for bit.
/// </summary>
/// <remarks>
/// A normalised 8 or 16-bit sample is divided in <see cref="float"/> when the result is a <see cref="float"/>: the quotient of
/// two numbers of 24 bits rounded once to <see cref="float"/> equals the quotient rounded to <see cref="double"/> then to
/// <see cref="float"/> (double rounding is harmless when 53 ≥ 2 × 24 + 2), which is what the scalar conversion computes. A
/// 32-bit integer is exact in <see cref="double"/>: it is divided there, then rounded to <see cref="float"/>, as the scalar
/// conversion does. A floating-point image read normalised to its own type does not come here: every maximum is 1, so its
/// samples are given as they are, every bit (a signalling NaN keeps its payload, where a conversion would make it quiet).
/// </remarks>
internal static class RowConversion<TD, TOut>
    where TD : unmanaged, INumber<TD>
    where TOut : unmanaged, INumber<TOut>
{
    /// <summary>Converts <paramref name="source"/> into <paramref name="destination"/> (same length).</summary>
    /// <param name="source">The unpacked samples.</param>
    /// <param name="destination">Where they are delivered.</param>
    /// <param name="identity">Whether the samples are delivered as they are (same type, stored scale).</param>
    /// <param name="maximum">The divisor of a normalised read, or <see cref="double.NaN"/> for a stored one.</param>
    public static void Run(ReadOnlySpan<TD> source, Span<TOut> destination, bool identity, double maximum)
    {
        if (identity)
        {
            MemoryMarshal.Cast<TD, TOut>(source).CopyTo(destination);
            return;
        }

        int done = Vector.IsHardwareAccelerated ? Vectorised(source, destination, maximum) : 0;
        Scalar(source[done..], destination[done..], maximum);
    }

    /// <summary>
    /// Converts <paramref name="count"/> samples taken every <paramref name="sourceStride"/> samples from
    /// <paramref name="sourceStart"/>, into <paramref name="destination"/> every <paramref name="destinationStride"/> samples
    /// from <paramref name="destinationStart"/>: one channel of interleaved pixels into a plane, or back.
    /// </summary>
    [SkipLocalsInit]
    public static void Strided(ReadOnlySpan<TD> source, int sourceStart, int sourceStride, Span<TOut> destination, int destinationStart,
        int destinationStride, int count, bool identity, double maximum)
    {
        if (count <= 0)
        {
            return;
        }

        // Both ends of each run are checked (the slices, then their last samples); the loops then go through references.
        var from = source[sourceStart..];
        var to = destination[destinationStart..];
        _ = from[(count - 1) * sourceStride];
        _ = to[(count - 1) * destinationStride];
        if (sourceStride == 1 && destinationStride == 1)
        {
            Run(from[..count], to[..count], identity, maximum);
            return;
        }

        ref TD s = ref MemoryMarshal.GetReference(from);
        ref TOut d = ref MemoryMarshal.GetReference(to);
        if (identity)
        {
            for (int k = 0; k < count; k++)
            {
                TD value = Unsafe.Add(ref s, k * sourceStride);
                Unsafe.Add(ref d, k * destinationStride) = Unsafe.As<TD, TOut>(ref value);
            }

            return;
        }

        // A conversion: the samples are gathered into a contiguous chunk (unless they are), converted with vectors straight into
        // the destination when it is contiguous, otherwise into a chunk spread afterwards.
        Span<TD> gathered = stackalloc TD[Chunk];
        Span<TOut> converted = stackalloc TOut[Chunk];
        for (int k0 = 0; k0 < count; k0 += Chunk)
        {
            int n = Math.Min(Chunk, count - k0);
            scoped ReadOnlySpan<TD> run;
            if (sourceStride == 1)
            {
                run = from.Slice(k0, n);
            }
            else
            {
                for (int k = 0; k < n; k++)
                {
                    gathered[k] = Unsafe.Add(ref s, (k0 + k) * sourceStride);
                }

                run = gathered[..n];
            }

            if (destinationStride == 1)
            {
                Run(run, to.Slice(k0, n), identity: false, maximum);
                continue;
            }

            Run(run, converted[..n], identity: false, maximum);
            for (int k = 0; k < n; k++)
            {
                Unsafe.Add(ref d, (k0 + k) * destinationStride) = converted[k];
            }
        }
    }

    /// <summary>The samples converted at once by <see cref="Strided"/> (at most 2 × 256 × 8 bytes on the stack).</summary>
    private const int Chunk = 256;

    private static void Scalar(ReadOnlySpan<TD> source, Span<TOut> destination, double maximum)
    {
        if (double.IsNaN(maximum))
        {
            for (int i = 0; i < source.Length; i++)
            {
                destination[i] = TOut.CreateTruncating(source[i]);
            }

            return;
        }

        for (int i = 0; i < source.Length; i++)
        {
            destination[i] = TOut.CreateTruncating(double.CreateTruncating(source[i]) / maximum);
        }
    }

    /// <summary>The samples converted with vectors (whole vectors only); the number done.</summary>
    private static int Vectorised(ReadOnlySpan<TD> source, Span<TOut> destination, double maximum)
    {
        if (typeof(TOut) == typeof(float) && typeof(TD) == typeof(int))
        {
            return Wide.ToSingle(MemoryMarshal.Cast<TD, int>(source), MemoryMarshal.Cast<TOut, float>(destination), maximum);
        }

        if (typeof(TOut) == typeof(float) && typeof(TD) == typeof(uint))
        {
            return Wide.ToSingle(MemoryMarshal.Cast<TD, uint>(source), MemoryMarshal.Cast<TOut, float>(destination), maximum);
        }

        if (typeof(TOut) == typeof(float) && (double.IsNaN(maximum) || (float)maximum == maximum))
        {
            var target = MemoryMarshal.Cast<TOut, float>(destination);
            if (typeof(TD) == typeof(ushort))
            {
                return Wide.ToSingle(MemoryMarshal.Cast<TD, ushort>(source), target, (float)maximum);
            }

            if (typeof(TD) == typeof(short))
            {
                return Wide.ToSingle(MemoryMarshal.Cast<TD, short>(source), target, (float)maximum);
            }

            if (typeof(TD) == typeof(byte))
            {
                return Wide.ToSingle(MemoryMarshal.Cast<TD, byte>(source), target, (float)maximum);
            }
        }
        else if (typeof(TOut) == typeof(double))
        {
            var target = MemoryMarshal.Cast<TOut, double>(destination);
            if (typeof(TD) == typeof(ushort))
            {
                return Wide.ToDouble(MemoryMarshal.Cast<TD, ushort>(source), target, maximum);
            }

            if (typeof(TD) == typeof(short))
            {
                return Wide.ToDouble(MemoryMarshal.Cast<TD, short>(source), target, maximum);
            }

            if (typeof(TD) == typeof(byte))
            {
                return Wide.ToDouble(MemoryMarshal.Cast<TD, byte>(source), target, maximum);
            }

            if (typeof(TD) == typeof(float))
            {
                return Wide.ToDouble(MemoryMarshal.Cast<TD, float>(source), target, maximum);
            }

            if (typeof(TD) == typeof(int))
            {
                return Wide.ToDouble(MemoryMarshal.Cast<TD, int>(source), target, maximum);
            }

            if (typeof(TD) == typeof(uint))
            {
                return Wide.ToDouble(MemoryMarshal.Cast<TD, uint>(source), target, maximum);
            }
        }

        return 0;
    }
}

/// <summary>The vector loops of <see cref="RowConversion{TD, TOut}"/>, by source type.</summary>
internal static class Wide
{
    // 8 and 16-bit integers are exact in float (24 bits) and their quotients by a maximum of 24 bits are rounded as the scalar
    // conversion rounds them (see the remarks of RowConversion).
    public static int ToSingle(ReadOnlySpan<ushort> source, Span<float> destination, float maximum)
    {
        int lanes = Vector<ushort>.Count, i = 0;
        bool divide = !float.IsNaN(maximum);
        var max = new Vector<float>(maximum);
        for (; i <= source.Length - lanes; i += lanes)
        {
            Vector.Widen(new Vector<ushort>(source[i..]), out Vector<uint> low, out Vector<uint> high);
            Store(Vector.ConvertToSingle(Vector.AsVectorInt32(low)), destination[i..], divide, max);
            Store(Vector.ConvertToSingle(Vector.AsVectorInt32(high)), destination[(i + lanes / 2)..], divide, max);
        }

        return i;
    }

    public static int ToSingle(ReadOnlySpan<short> source, Span<float> destination, float maximum)
    {
        int lanes = Vector<short>.Count, i = 0;
        bool divide = !float.IsNaN(maximum);
        var max = new Vector<float>(maximum);
        for (; i <= source.Length - lanes; i += lanes)
        {
            Vector.Widen(new Vector<short>(source[i..]), out Vector<int> low, out Vector<int> high);
            Store(Vector.ConvertToSingle(low), destination[i..], divide, max);
            Store(Vector.ConvertToSingle(high), destination[(i + lanes / 2)..], divide, max);
        }

        return i;
    }

    public static int ToSingle(ReadOnlySpan<byte> source, Span<float> destination, float maximum)
    {
        int lanes = Vector<byte>.Count, quarter = lanes / 4, i = 0;
        bool divide = !float.IsNaN(maximum);
        var max = new Vector<float>(maximum);
        for (; i <= source.Length - lanes; i += lanes)
        {
            Vector.Widen(new Vector<byte>(source[i..]), out Vector<ushort> low, out Vector<ushort> high);
            Vector.Widen(low, out Vector<uint> a, out Vector<uint> b);
            Vector.Widen(high, out Vector<uint> c, out Vector<uint> d);
            Store(Vector.ConvertToSingle(Vector.AsVectorInt32(a)), destination[i..], divide, max);
            Store(Vector.ConvertToSingle(Vector.AsVectorInt32(b)), destination[(i + quarter)..], divide, max);
            Store(Vector.ConvertToSingle(Vector.AsVectorInt32(c)), destination[(i + 2 * quarter)..], divide, max);
            Store(Vector.ConvertToSingle(Vector.AsVectorInt32(d)), destination[(i + 3 * quarter)..], divide, max);
        }

        return i;
    }

    // 16-bit integers are exact in float, and float to double is exact: through float, without 64-bit integer lanes (their
    // conversion to double has no instruction before AVX-512).
    public static int ToDouble(ReadOnlySpan<ushort> source, Span<double> destination, double maximum)
    {
        int lanes = Vector<ushort>.Count, quarter = lanes / 4, i = 0;
        bool divide = !double.IsNaN(maximum);
        var max = new Vector<double>(maximum);
        for (; i <= source.Length - lanes; i += lanes)
        {
            Vector.Widen(new Vector<ushort>(source[i..]), out Vector<uint> low, out Vector<uint> high);
            Vector.Widen(Vector.ConvertToSingle(Vector.AsVectorInt32(low)), out Vector<double> a, out Vector<double> b);
            Vector.Widen(Vector.ConvertToSingle(Vector.AsVectorInt32(high)), out Vector<double> c, out Vector<double> d);
            Store(a, destination[i..], divide, max);
            Store(b, destination[(i + quarter)..], divide, max);
            Store(c, destination[(i + 2 * quarter)..], divide, max);
            Store(d, destination[(i + 3 * quarter)..], divide, max);
        }

        return i;
    }

    public static int ToDouble(ReadOnlySpan<short> source, Span<double> destination, double maximum)
    {
        int lanes = Vector<short>.Count, quarter = lanes / 4, i = 0;
        bool divide = !double.IsNaN(maximum);
        var max = new Vector<double>(maximum);
        for (; i <= source.Length - lanes; i += lanes)
        {
            Vector.Widen(new Vector<short>(source[i..]), out Vector<int> low, out Vector<int> high);
            Vector.Widen(Vector.ConvertToSingle(low), out Vector<double> a, out Vector<double> b);
            Vector.Widen(Vector.ConvertToSingle(high), out Vector<double> c, out Vector<double> d);
            Store(a, destination[i..], divide, max);
            Store(b, destination[(i + quarter)..], divide, max);
            Store(c, destination[(i + 2 * quarter)..], divide, max);
            Store(d, destination[(i + 3 * quarter)..], divide, max);
        }

        return i;
    }

    public static int ToDouble(ReadOnlySpan<byte> source, Span<double> destination, double maximum)
    {
        int lanes = Vector<byte>.Count, eighth = lanes / 8, i = 0;
        bool divide = !double.IsNaN(maximum);
        var max = new Vector<double>(maximum);
        for (; i <= source.Length - lanes; i += lanes)
        {
            Vector.Widen(new Vector<byte>(source[i..]), out Vector<ushort> low, out Vector<ushort> high);
            Vector.Widen(low, out Vector<uint> a, out Vector<uint> b);
            Vector.Widen(high, out Vector<uint> c, out Vector<uint> d);
            Quarter(a, destination[i..], divide, max, eighth);
            Quarter(b, destination[(i + 2 * eighth)..], divide, max, eighth);
            Quarter(c, destination[(i + 4 * eighth)..], divide, max, eighth);
            Quarter(d, destination[(i + 6 * eighth)..], divide, max, eighth);
        }

        return i;
    }

    public static int ToDouble(ReadOnlySpan<float> source, Span<double> destination, double maximum)
    {
        int lanes = Vector<float>.Count, i = 0;
        bool divide = !double.IsNaN(maximum) && maximum != 1;   // a quotient by 1 is the value (a float normalised to double)
        var max = new Vector<double>(maximum);
        for (; i <= source.Length - lanes; i += lanes)
        {
            Vector.Widen(new Vector<float>(source[i..]), out Vector<double> low, out Vector<double> high);
            Store(low, destination[i..], divide, max);
            Store(high, destination[(i + lanes / 2)..], divide, max);
        }

        return i;
    }

    // 32-bit integers are exact in double: each is put in the mantissa of 2^52 (the sign bit flipped for signed ones, so that
    // every value is positive), and 2^52 (plus 2^31) taken off; the vectors need no conversion of 64-bit lanes (AVX-512).
    public static int ToDouble(ReadOnlySpan<int> source, Span<double> destination, double maximum)
    {
        int lanes = Vector<int>.Count, i = 0;
        bool divide = !double.IsNaN(maximum);
        var max = new Vector<double>(maximum);
        for (; i <= source.Length - lanes; i += lanes)
        {
            Exact(new Vector<int>(source[i..]), out var low, out var high);
            Store(low, destination[i..], divide, max);
            Store(high, destination[(i + lanes / 2)..], divide, max);
        }

        return i;
    }

    public static int ToDouble(ReadOnlySpan<uint> source, Span<double> destination, double maximum)
    {
        int lanes = Vector<uint>.Count, i = 0;
        bool divide = !double.IsNaN(maximum);
        var max = new Vector<double>(maximum);
        for (; i <= source.Length - lanes; i += lanes)
        {
            Exact(new Vector<uint>(source[i..]), out var low, out var high);
            Store(low, destination[i..], divide, max);
            Store(high, destination[(i + lanes / 2)..], divide, max);
        }

        return i;
    }

    // To float: in double (exact, and the quotient by the maximum as the scalar conversion computes it), then rounded once.
    public static int ToSingle(ReadOnlySpan<int> source, Span<float> destination, double maximum)
    {
        int lanes = Vector<int>.Count, i = 0;
        bool divide = !double.IsNaN(maximum);
        var max = new Vector<double>(maximum);
        for (; i <= source.Length - lanes; i += lanes)
        {
            Exact(new Vector<int>(source[i..]), out var low, out var high);
            Vector.Narrow(divide ? low / max : low, divide ? high / max : high).CopyTo(destination[i..]);
        }

        return i;
    }

    public static int ToSingle(ReadOnlySpan<uint> source, Span<float> destination, double maximum)
    {
        int lanes = Vector<uint>.Count, i = 0;
        bool divide = !double.IsNaN(maximum);
        var max = new Vector<double>(maximum);
        for (; i <= source.Length - lanes; i += lanes)
        {
            Exact(new Vector<uint>(source[i..]), out var low, out var high);
            Vector.Narrow(divide ? low / max : low, divide ? high / max : high).CopyTo(destination[i..]);
        }

        return i;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Exact(Vector<int> values, out Vector<double> low, out Vector<double> high)
    {
        Vector.Widen(Vector.AsVectorUInt32(values) ^ new Vector<uint>(0x8000_0000), out Vector<ulong> a, out Vector<ulong> b);
        var bias = new Vector<double>(4503601774854144.0);   // 2^52 + 2^31
        low = Vector.AsVectorDouble(a | new Vector<ulong>(0x4330_0000_0000_0000)) - bias;
        high = Vector.AsVectorDouble(b | new Vector<ulong>(0x4330_0000_0000_0000)) - bias;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Exact(Vector<uint> values, out Vector<double> low, out Vector<double> high)
    {
        Vector.Widen(values, out Vector<ulong> a, out Vector<ulong> b);
        var bias = new Vector<double>(4503599627370496.0);   // 2^52
        low = Vector.AsVectorDouble(a | new Vector<ulong>(0x4330_0000_0000_0000)) - bias;
        high = Vector.AsVectorDouble(b | new Vector<ulong>(0x4330_0000_0000_0000)) - bias;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Quarter(Vector<uint> values, Span<double> destination, bool divide, Vector<double> maximum, int eighth)
    {
        Vector.Widen(Vector.ConvertToSingle(Vector.AsVectorInt32(values)), out Vector<double> low, out Vector<double> high);
        Store(low, destination, divide, maximum);
        Store(high, destination[eighth..], divide, maximum);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Store(Vector<float> values, Span<float> destination, bool divide, Vector<float> maximum) =>
        (divide ? values / maximum : values).CopyTo(destination);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Store(Vector<double> values, Span<double> destination, bool divide, Vector<double> maximum) =>
        (divide ? values / maximum : values).CopyTo(destination);
}
