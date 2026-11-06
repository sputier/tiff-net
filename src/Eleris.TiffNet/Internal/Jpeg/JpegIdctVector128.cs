using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.Arm;
using System.Runtime.Intrinsics.X86;

namespace Eleris.TiffNet.Internal.Jpeg;

/// <summary>
/// The islow inverse DCT of <see cref="JpegIdct"/> on four lanes at once (128-bit vectors: SSE2 without AVX2, AdvSimd on ARM —
/// Android, Apple Silicon): the columns of pass 1 four at a time, the block transposed by 4 x 4 quarters, the rows of pass 2 four at
/// a time. The arithmetic is that of libjpeg in 32 bits, as the scalar and the AVX2 transforms compute it: the same samples for any
/// block.
/// </summary>
internal static class JpegIdctVector128
{
    private const int ConstBits = 13, Pass1Bits = 2;

    public static bool IsSupported => Vector128.IsHardwareAccelerated && (Sse2.IsSupported || AdvSimd.Arm64.IsSupported);

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

        // Pass 1, columns 0 to 3 then 4 to 7: lane c is column c; l[r] and h[r] are row r of the dequantized coefficients.
        Load(ref co, ref q, 0, out var l0, out var h0);
        Load(ref co, ref q, 1, out var l1, out var h1);
        Load(ref co, ref q, 2, out var l2, out var h2);
        Load(ref co, ref q, 3, out var l3, out var h3);
        Load(ref co, ref q, 4, out var l4, out var h4);
        Load(ref co, ref q, 5, out var l5, out var h5);
        Load(ref co, ref q, 6, out var l6, out var h6);
        Load(ref co, ref q, 7, out var l7, out var h7);
        const int Shift1 = ConstBits - Pass1Bits;
        Butterfly(l0, l1, l2, l3, l4, l5, l6, l7, Shift1, out l0, out l1, out l2, out l3, out l4, out l5, out l6, out l7);
        Butterfly(h0, h1, h2, h3, h4, h5, h6, h7, Shift1, out h0, out h1, out h2, out h3, out h4, out h5, out h6, out h7);

        // Pass 2, rows 0 to 3 then 4 to 7: lane r is row r; the inputs are the columns of the work block.
        ref byte out0 = ref MemoryMarshal.GetReference(output);
        Rows(l0, l1, l2, l3, h0, h1, h2, h3, ref out0, stride, 0);
        Rows(l4, l5, l6, l7, h4, h5, h6, h7, ref out0, stride, 4);
    }

    /// <summary>Four rows of the work block (their columns 0 to 3 and 4 to 7) through pass 2, into rows <paramref name="first"/> + 0 to 3.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Rows(Vector128<int> l0, Vector128<int> l1, Vector128<int> l2, Vector128<int> l3,
        Vector128<int> h0, Vector128<int> h1, Vector128<int> h2, Vector128<int> h3, ref byte output, int stride, int first)
    {
        Transpose(ref l0, ref l1, ref l2, ref l3);
        Transpose(ref h0, ref h1, ref h2, ref h3);
        Butterfly(l0, l1, l2, l3, h0, h1, h2, h3, ConstBits + Pass1Bits + 3,
            out var o0, out var o1, out var o2, out var o3, out var o4, out var o5, out var o6, out var o7);
        Transpose(ref o0, ref o1, ref o2, ref o3);
        Transpose(ref o4, ref o5, ref o6, ref o7);
        Store(ref output, stride, first, Limit(o0), Limit(o4));
        Store(ref output, stride, first + 1, Limit(o1), Limit(o5));
        Store(ref output, stride, first + 2, Limit(o2), Limit(o6));
        Store(ref output, stride, first + 3, Limit(o3), Limit(o7));
    }

    /// <summary>Row <paramref name="row"/> of the coefficients times its quantization steps, columns 0 to 3 and 4 to 7.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Load(ref short co, ref ushort q, int row, out Vector128<int> low, out Vector128<int> high)
    {
        var c = Vector128.LoadUnsafe(ref Unsafe.Add(ref co, row * 8));
        var s = Vector128.LoadUnsafe(ref Unsafe.Add(ref q, row * 8));
        low = Vector128.WidenLower(c) * Vector128.WidenLower(s).AsInt32();
        high = Vector128.WidenUpper(c) * Vector128.WidenUpper(s).AsInt32();
    }

    /// <summary>The 8-point transform of jidctint.c on each lane, with the final DESCALE by 2^<paramref name="shift"/>.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Butterfly(Vector128<int> in0, Vector128<int> in1, Vector128<int> in2, Vector128<int> in3, Vector128<int> in4,
        Vector128<int> in5, Vector128<int> in6, Vector128<int> in7, int shift,
        out Vector128<int> out0, out Vector128<int> out1, out Vector128<int> out2, out Vector128<int> out3,
        out Vector128<int> out4, out Vector128<int> out5, out Vector128<int> out6, out Vector128<int> out7)
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

        var half = Vector128.Create(1 << (shift - 1));
        out0 = (tmp10 + o3 + half) >> shift;
        out7 = (tmp10 - o3 + half) >> shift;
        out1 = (tmp11 + o2 + half) >> shift;
        out6 = (tmp11 - o2 + half) >> shift;
        out2 = (tmp12 + o1 + half) >> shift;
        out5 = (tmp12 - o1 + half) >> shift;
        out3 = (tmp13 + o0 + half) >> shift;
        out4 = (tmp13 - o0 + half) >> shift;
    }

    /// <summary>Transposes a 4 x 4 block of integers held as 4 rows of 4 lanes.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Transpose(ref Vector128<int> r0, ref Vector128<int> r1, ref Vector128<int> r2, ref Vector128<int> r3)
    {
        var t0 = ZipLow(r0, r1).AsInt64();
        var t1 = ZipLow(r2, r3).AsInt64();
        var t2 = ZipHigh(r0, r1).AsInt64();
        var t3 = ZipHigh(r2, r3).AsInt64();
        r0 = ZipLow(t0, t1).AsInt32();
        r1 = ZipHigh(t0, t1).AsInt32();
        r2 = ZipLow(t2, t3).AsInt32();
        r3 = ZipHigh(t2, t3).AsInt32();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector128<int> ZipLow(Vector128<int> a, Vector128<int> b) => Sse2.IsSupported ? Sse2.UnpackLow(a, b) : AdvSimd.Arm64.ZipLow(a, b);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector128<int> ZipHigh(Vector128<int> a, Vector128<int> b) => Sse2.IsSupported ? Sse2.UnpackHigh(a, b) : AdvSimd.Arm64.ZipHigh(a, b);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector128<long> ZipLow(Vector128<long> a, Vector128<long> b) => Sse2.IsSupported ? Sse2.UnpackLow(a, b) : AdvSimd.Arm64.ZipLow(a, b);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector128<long> ZipHigh(Vector128<long> a, Vector128<long> b) => Sse2.IsSupported ? Sse2.UnpackHigh(a, b) : AdvSimd.Arm64.ZipHigh(a, b);

    /// <summary>libjpeg's range limit: the value plus 128, wrapped to 10 bits as a signed value, clamped to a byte.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector128<int> Limit(Vector128<int> value)
    {
        var wrapped = ((value & Vector128.Create(1023)) ^ Vector128.Create(512)) - Vector128.Create(512);
        return Vector128.Min(Vector128.Max(wrapped + Vector128.Create(128), Vector128<int>.Zero), Vector128.Create(255));
    }

    /// <summary>Writes row <paramref name="row"/>: its columns 0 to 3, then 4 to 7 (each 0 to 255).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Store(ref byte output, int stride, int row, Vector128<int> low, Vector128<int> high)
    {
        var shorts = Vector128.Narrow(low, high);
        var bytes = Vector128.Narrow(shorts, shorts).AsUInt64();
        Unsafe.WriteUnaligned(ref Unsafe.Add(ref output, row * stride), bytes.GetElement(0));
    }
}
