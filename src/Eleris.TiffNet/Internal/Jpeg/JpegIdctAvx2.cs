using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace Eleris.TiffNet.Internal.Jpeg;

/// <summary>
/// The islow inverse DCT of <see cref="JpegIdct"/> on eight lanes at once (AVX2): the eight columns of pass 1 together, a
/// transposition, the eight rows of pass 2 together, a transposition back. The arithmetic is that of libjpeg in 32 bits (as
/// libjpeg-turbo computes it), as the scalar transform's: both give the same samples for any block.
/// </summary>
internal static class JpegIdctAvx2
{
    private const int ConstBits = 13, Pass1Bits = 2;

    public static bool IsSupported => Avx2.IsSupported;

    /// <summary>Transforms one block, as <see cref="JpegIdct.Transform"/>.</summary>
    public static void Transform(ReadOnlySpan<short> coefficients, ReadOnlySpan<ushort> quantization, Span<byte> output, int stride)
    {
        JpegIdct.CheckBounds(coefficients, quantization, output, stride);
        TransformChecked(coefficients, quantization, output, stride);
    }

    /// <summary>Transforms one block whose bounds the caller checked.</summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    internal static void TransformChecked(ReadOnlySpan<short> coefficients, ReadOnlySpan<ushort> quantization, Span<byte> output, int stride)
    {
        ref short co = ref MemoryMarshal.GetReference(coefficients);
        ref ushort q = ref MemoryMarshal.GetReference(quantization);

        // Pass 1: lane c is column c; v[r] is row r of the dequantized coefficients.
        var v0 = Load(ref co, ref q, 0);
        var v1 = Load(ref co, ref q, 1);
        var v2 = Load(ref co, ref q, 2);
        var v3 = Load(ref co, ref q, 3);
        var v4 = Load(ref co, ref q, 4);
        var v5 = Load(ref co, ref q, 5);
        var v6 = Load(ref co, ref q, 6);
        var v7 = Load(ref co, ref q, 7);
        Butterfly(v0, v1, v2, v3, v4, v5, v6, v7, ConstBits - Pass1Bits,
            out var w0, out var w1, out var w2, out var w3, out var w4, out var w5, out var w6, out var w7);

        // Pass 2: lane r is row r; the inputs are the columns of the work block.
        Transpose(ref w0, ref w1, ref w2, ref w3, ref w4, ref w5, ref w6, ref w7);
        Butterfly(w0, w1, w2, w3, w4, w5, w6, w7, ConstBits + Pass1Bits + 3,
            out var o0, out var o1, out var o2, out var o3, out var o4, out var o5, out var o6, out var o7);
        Transpose(ref o0, ref o1, ref o2, ref o3, ref o4, ref o5, ref o6, ref o7);

        ref byte out0 = ref MemoryMarshal.GetReference(output);
        Store(ref out0, stride, 0, Limit(o0), Limit(o1), Limit(o2), Limit(o3));
        Store(ref out0, stride, 4, Limit(o4), Limit(o5), Limit(o6), Limit(o7));
    }

    /// <summary>Row <paramref name="row"/> of the coefficients times its quantization steps, as 8 integers.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector256<int> Load(ref short co, ref ushort q, int row)
    {
        var c = Vector128.LoadUnsafe(ref Unsafe.Add(ref co, row * 8));
        var s = Vector128.LoadUnsafe(ref Unsafe.Add(ref q, row * 8));
        return Avx2.ConvertToVector256Int32(c) * Avx2.ConvertToVector256Int32(s);
    }

    /// <summary>The 8-point transform of jidctint.c on each lane, with the final DESCALE by 2^<paramref name="shift"/>.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Butterfly(Vector256<int> in0, Vector256<int> in1, Vector256<int> in2, Vector256<int> in3, Vector256<int> in4,
        Vector256<int> in5, Vector256<int> in6, Vector256<int> in7, int shift,
        out Vector256<int> out0, out Vector256<int> out1, out Vector256<int> out2, out Vector256<int> out3,
        out Vector256<int> out4, out Vector256<int> out5, out Vector256<int> out6, out Vector256<int> out7)
    {
        // Even part.
        var z1 = (in2 + in6) * 4433;
        var tmp2 = z1 + in6 * -15137;
        var tmp3 = z1 + in2 * 6270;
        var tmp0 = (in0 + in4) << ConstBits;
        var tmp1 = (in0 - in4) << ConstBits;
        var tmp10 = tmp0 + tmp3;
        var tmp13 = tmp0 - tmp3;
        var tmp11 = tmp1 + tmp2;
        var tmp12 = tmp1 - tmp2;

        // Odd part, from the inputs 7, 5, 3, 1.
        var a1 = in7 + in1;
        var a2 = in5 + in3;
        var a3 = in7 + in3;
        var a4 = in5 + in1;
        var z5 = (a3 + a4) * 9633;
        var o0 = in7 * 2446;
        var o1 = in5 * 16819;
        var o2 = in3 * 25172;
        var o3 = in1 * 12299;
        a1 *= -7373;
        a2 *= -20995;
        a3 = a3 * -16069 + z5;
        a4 = a4 * -3196 + z5;
        o0 += a1 + a3;
        o1 += a2 + a4;
        o2 += a2 + a3;
        o3 += a1 + a4;

        var half = Vector256.Create(1 << (shift - 1));
        out0 = (tmp10 + o3 + half) >> shift;
        out7 = (tmp10 - o3 + half) >> shift;
        out1 = (tmp11 + o2 + half) >> shift;
        out6 = (tmp11 - o2 + half) >> shift;
        out2 = (tmp12 + o1 + half) >> shift;
        out5 = (tmp12 - o1 + half) >> shift;
        out3 = (tmp13 + o0 + half) >> shift;
        out4 = (tmp13 - o0 + half) >> shift;
    }

    /// <summary>Transposes an 8 x 8 block of integers held as 8 rows of 8 lanes.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Transpose(ref Vector256<int> r0, ref Vector256<int> r1, ref Vector256<int> r2, ref Vector256<int> r3,
        ref Vector256<int> r4, ref Vector256<int> r5, ref Vector256<int> r6, ref Vector256<int> r7)
    {
        var t0 = Avx2.UnpackLow(r0, r1);
        var t1 = Avx2.UnpackHigh(r0, r1);
        var t2 = Avx2.UnpackLow(r2, r3);
        var t3 = Avx2.UnpackHigh(r2, r3);
        var t4 = Avx2.UnpackLow(r4, r5);
        var t5 = Avx2.UnpackHigh(r4, r5);
        var t6 = Avx2.UnpackLow(r6, r7);
        var t7 = Avx2.UnpackHigh(r6, r7);

        var u0 = Avx2.UnpackLow(t0.AsInt64(), t2.AsInt64()).AsInt32();
        var u1 = Avx2.UnpackHigh(t0.AsInt64(), t2.AsInt64()).AsInt32();
        var u2 = Avx2.UnpackLow(t1.AsInt64(), t3.AsInt64()).AsInt32();
        var u3 = Avx2.UnpackHigh(t1.AsInt64(), t3.AsInt64()).AsInt32();
        var u4 = Avx2.UnpackLow(t4.AsInt64(), t6.AsInt64()).AsInt32();
        var u5 = Avx2.UnpackHigh(t4.AsInt64(), t6.AsInt64()).AsInt32();
        var u6 = Avx2.UnpackLow(t5.AsInt64(), t7.AsInt64()).AsInt32();
        var u7 = Avx2.UnpackHigh(t5.AsInt64(), t7.AsInt64()).AsInt32();

        r0 = Avx2.Permute2x128(u0, u4, 0x20);
        r1 = Avx2.Permute2x128(u1, u5, 0x20);
        r2 = Avx2.Permute2x128(u2, u6, 0x20);
        r3 = Avx2.Permute2x128(u3, u7, 0x20);
        r4 = Avx2.Permute2x128(u0, u4, 0x31);
        r5 = Avx2.Permute2x128(u1, u5, 0x31);
        r6 = Avx2.Permute2x128(u2, u6, 0x31);
        r7 = Avx2.Permute2x128(u3, u7, 0x31);
    }

    /// <summary>libjpeg's range limit: the value plus 128, wrapped to 10 bits as a signed value, clamped to a byte.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector256<int> Limit(Vector256<int> value)
    {
        var wrapped = ((value & Vector256.Create(1023)) ^ Vector256.Create(512)) - Vector256.Create(512);
        return Vector256.Min(Vector256.Max(wrapped + Vector256.Create(128), Vector256<int>.Zero), Vector256.Create(255));
    }

    /// <summary>Writes rows <paramref name="first"/> to <paramref name="first"/> + 3 (8 samples of 0 to 255 each).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Store(ref byte output, int stride, int first, Vector256<int> a, Vector256<int> b, Vector256<int> c, Vector256<int> d)
    {
        var bytes = Vector256.Narrow(Vector256.Narrow(a, b), Vector256.Narrow(c, d)).AsUInt64();
        Unsafe.WriteUnaligned(ref Unsafe.Add(ref output, first * stride), bytes.GetElement(0));
        Unsafe.WriteUnaligned(ref Unsafe.Add(ref output, (first + 1) * stride), bytes.GetElement(1));
        Unsafe.WriteUnaligned(ref Unsafe.Add(ref output, (first + 2) * stride), bytes.GetElement(2));
        Unsafe.WriteUnaligned(ref Unsafe.Add(ref output, (first + 3) * stride), bytes.GetElement(3));
    }
}
