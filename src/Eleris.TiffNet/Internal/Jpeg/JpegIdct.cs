using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Eleris.TiffNet.Internal.Jpeg;

/// <summary>
/// The inverse DCT of libjpeg's "islow" method (jidctint.c, the default of libjpeg and libjpeg-turbo), reproduced bit for bit: a
/// separable integer algorithm (Loeffler, Ligtenberg and Moschytz) with 13-bit constants, columns first with 2 extra bits of
/// precision, then rows, and the same range limiting of the result (a 10-bit wrap, then a clamp to 0..255). The arithmetic is
/// in 32 bits, wrapping as libjpeg-turbo's does, so that the scalar and the vectorised transforms give the same samples for any
/// block, a hostile one included (16-bit quantization steps can make the sums overflow): the pixels do not depend on the machine.
/// </summary>
internal static class JpegIdct
{
    private const int ConstBits = 13, Pass1Bits = 2;

    private const int Fix0298631336 = 2446, Fix0390180644 = 3196, Fix0541196100 = 4433, Fix0765366865 = 6270, Fix0899976223 = 7373,
        Fix1175875602 = 9633, Fix1501321110 = 12299, Fix1847759065 = 15137, Fix1961570560 = 16069, Fix2053119869 = 16819,
        Fix2562915447 = 20995, Fix3072711026 = 25172;

    /// <summary>The scalar transform, whatever the machine (the reference of the vectorised one).</summary>
    internal static void TransformScalar(ReadOnlySpan<short> coefficients, ReadOnlySpan<ushort> quantization, Span<byte> output, int stride)
    {
        CheckBounds(coefficients, quantization, output, stride);
        Scalar(coefficients, quantization, output, stride);
    }

    /// <summary>Transforms one block of coefficients (natural order) with its quantization table into 8 x 8 samples.</summary>
    /// <param name="coefficients">64 quantized coefficients, in natural (row by row) order.</param>
    /// <param name="quantization">64 quantization steps, in natural order.</param>
    /// <param name="output">The first sample of the block in its plane.</param>
    /// <param name="stride">The bytes from one row of the plane to the next.</param>
    [SkipLocalsInit]
    public static void Transform(ReadOnlySpan<short> coefficients, ReadOnlySpan<ushort> quantization, Span<byte> output, int stride)
    {
        // The bounds are checked once here; the loops then go through references (the IDCT runs for every block).
        CheckBounds(coefficients, quantization, output, stride);
        if (JpegIdctAvx2.IsSupported)
        {
            JpegIdctAvx2.TransformChecked(coefficients, quantization, output, stride);
            return;
        }

        Scalar(coefficients, quantization, output, stride);
    }

    internal static void CheckBounds(ReadOnlySpan<short> coefficients, ReadOnlySpan<ushort> quantization, Span<byte> output, int stride)
    {
        if (coefficients.Length < 64 || quantization.Length < 64 || stride < 8 || output.Length < 7L * stride + 8)
        {
            throw new ArgumentException("A block needs 64 coefficients, 64 quantization steps and 8 rows of output.");
        }
    }

    /// <summary>The scalar transform, the bounds checked by the caller.</summary>
    [SkipLocalsInit]
    private static void Scalar(ReadOnlySpan<short> coefficients, ReadOnlySpan<ushort> quantization, Span<byte> output, int stride)
    {
        Span<int> work = stackalloc int[64];
        ref short co = ref MemoryMarshal.GetReference(coefficients);
        ref ushort q = ref MemoryMarshal.GetReference(quantization);
        ref int ws = ref MemoryMarshal.GetReference(work);

        // Pass 1: the columns, from the coefficients; results scaled up by 2^Pass1Bits.
        for (int c = 0; c < 8; c++)
        {
            ref short ci = ref Unsafe.Add(ref co, c);
            ref ushort qi = ref Unsafe.Add(ref q, c);
            ref int wi = ref Unsafe.Add(ref ws, c);
            if (Unsafe.Add(ref ci, 8) == 0 && Unsafe.Add(ref ci, 16) == 0 && Unsafe.Add(ref ci, 24) == 0 && Unsafe.Add(ref ci, 32) == 0
                && Unsafe.Add(ref ci, 40) == 0 && Unsafe.Add(ref ci, 48) == 0 && Unsafe.Add(ref ci, 56) == 0)
            {
                // Only the DC term: the column is constant, the value the full pass gives (its wrap included).
                int dc = Descale(ci * qi << ConstBits, ConstBits - Pass1Bits);
                for (int r = 0; r < 8; r++)
                {
                    Unsafe.Add(ref wi, r * 8) = dc;
                }

                continue;
            }

            int z2 = Unsafe.Add(ref ci, 16) * Unsafe.Add(ref qi, 16);
            int z3 = Unsafe.Add(ref ci, 48) * Unsafe.Add(ref qi, 48);
            int z1 = (z2 + z3) * Fix0541196100;
            int tmp2 = z1 + z3 * -Fix1847759065;
            int tmp3 = z1 + z2 * Fix0765366865;
            z2 = ci * qi;
            z3 = Unsafe.Add(ref ci, 32) * Unsafe.Add(ref qi, 32);
            int tmp0 = (z2 + z3) << ConstBits;
            int tmp1 = (z2 - z3) << ConstBits;
            int tmp10 = tmp0 + tmp3, tmp13 = tmp0 - tmp3, tmp11 = tmp1 + tmp2, tmp12 = tmp1 - tmp2;

            Odd(Unsafe.Add(ref ci, 56) * Unsafe.Add(ref qi, 56), Unsafe.Add(ref ci, 40) * Unsafe.Add(ref qi, 40),
                Unsafe.Add(ref ci, 24) * Unsafe.Add(ref qi, 24), Unsafe.Add(ref ci, 8) * Unsafe.Add(ref qi, 8),
                out tmp0, out tmp1, out tmp2, out tmp3);

            const int Shift = ConstBits - Pass1Bits;
            wi = Descale(tmp10 + tmp3, Shift);
            Unsafe.Add(ref wi, 56) = Descale(tmp10 - tmp3, Shift);
            Unsafe.Add(ref wi, 8) = Descale(tmp11 + tmp2, Shift);
            Unsafe.Add(ref wi, 48) = Descale(tmp11 - tmp2, Shift);
            Unsafe.Add(ref wi, 16) = Descale(tmp12 + tmp1, Shift);
            Unsafe.Add(ref wi, 40) = Descale(tmp12 - tmp1, Shift);
            Unsafe.Add(ref wi, 24) = Descale(tmp13 + tmp0, Shift);
            Unsafe.Add(ref wi, 32) = Descale(tmp13 - tmp0, Shift);
        }

        // Pass 2: the rows, into samples; the scale of pass 1 and the 8 of the transform are taken out.
        ref byte out0 = ref MemoryMarshal.GetReference(output);
        for (int r = 0; r < 8; r++)
        {
            ref int w = ref Unsafe.Add(ref ws, r * 8);
            ref byte row = ref Unsafe.Add(ref out0, r * stride);
            const int Shift = ConstBits + Pass1Bits + 3;
            if (Unsafe.Add(ref w, 1) == 0 && Unsafe.Add(ref w, 2) == 0 && Unsafe.Add(ref w, 3) == 0 && Unsafe.Add(ref w, 4) == 0
                && Unsafe.Add(ref w, 5) == 0 && Unsafe.Add(ref w, 6) == 0 && Unsafe.Add(ref w, 7) == 0)
            {
                Unsafe.InitBlockUnaligned(ref row, Limit(Descale(w << ConstBits, Shift)), 8);
                continue;
            }

            int z2 = Unsafe.Add(ref w, 2), z3 = Unsafe.Add(ref w, 6);
            int z1 = (z2 + z3) * Fix0541196100;
            int tmp2 = z1 + z3 * -Fix1847759065;
            int tmp3 = z1 + z2 * Fix0765366865;
            int tmp0 = (w + Unsafe.Add(ref w, 4)) << ConstBits;
            int tmp1 = (w - Unsafe.Add(ref w, 4)) << ConstBits;
            int tmp10 = tmp0 + tmp3, tmp13 = tmp0 - tmp3, tmp11 = tmp1 + tmp2, tmp12 = tmp1 - tmp2;

            Odd(Unsafe.Add(ref w, 7), Unsafe.Add(ref w, 5), Unsafe.Add(ref w, 3), Unsafe.Add(ref w, 1), out tmp0, out tmp1, out tmp2, out tmp3);

            row = Limit(Descale(tmp10 + tmp3, Shift));
            Unsafe.Add(ref row, 7) = Limit(Descale(tmp10 - tmp3, Shift));
            Unsafe.Add(ref row, 1) = Limit(Descale(tmp11 + tmp2, Shift));
            Unsafe.Add(ref row, 6) = Limit(Descale(tmp11 - tmp2, Shift));
            Unsafe.Add(ref row, 2) = Limit(Descale(tmp12 + tmp1, Shift));
            Unsafe.Add(ref row, 5) = Limit(Descale(tmp12 - tmp1, Shift));
            Unsafe.Add(ref row, 3) = Limit(Descale(tmp13 + tmp0, Shift));
            Unsafe.Add(ref row, 4) = Limit(Descale(tmp13 - tmp0, Shift));
        }
    }

    /// <summary>
    /// A block whose only coefficient is DC: every sample is the same, what <see cref="Transform"/> gives for it (pass 1 scales
    /// the DC by 2^Pass1Bits, pass 2 takes out that scale and the 8 of the transform).
    /// </summary>
    public static void TransformDcOnly(int dcTimesQuantization, Span<byte> output, int stride)
    {
        int column = Descale(dcTimesQuantization << ConstBits, ConstBits - Pass1Bits);
        byte value = Limit(Descale(column << ConstBits, ConstBits + Pass1Bits + 3));
        for (int r = 0; r < 8; r++)
        {
            output.Slice(r * stride, 8).Fill(value);
        }
    }

    /// <summary>The odd part of the 8-point transform, from the inputs 7, 5, 3 and 1.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Odd(int in7, int in5, int in3, int in1, out int tmp0, out int tmp1, out int tmp2, out int tmp3)
    {
        int z1 = in7 + in1, z2 = in5 + in3, z3 = in7 + in3, z4 = in5 + in1;
        int z5 = (z3 + z4) * Fix1175875602;
        tmp0 = in7 * Fix0298631336;
        tmp1 = in5 * Fix2053119869;
        tmp2 = in3 * Fix3072711026;
        tmp3 = in1 * Fix1501321110;
        z1 *= -Fix0899976223;
        z2 *= -Fix2562915447;
        z3 = z3 * -Fix1961570560 + z5;
        z4 = z4 * -Fix0390180644 + z5;
        tmp0 += z1 + z3;
        tmp1 += z2 + z4;
        tmp2 += z2 + z3;
        tmp3 += z1 + z4;
    }

    /// <summary>Divides by 2^<paramref name="n"/>, rounding (DESCALE): an arithmetic shift of the value plus a half.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int Descale(int value, int n) => (value + (1 << (n - 1))) >> n;

    /// <summary>libjpeg's range limit after the IDCT: the value plus 128, wrapped to 10 bits as a signed value, clamped to a byte.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static byte Limit(int value)
    {
        int wrapped = ((value & 1023) ^ 512) - 512;
        return (byte)Math.Clamp(wrapped + 128, 0, 255);
    }
}
