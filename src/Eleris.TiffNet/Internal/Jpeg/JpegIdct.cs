using System.Runtime.CompilerServices;

namespace Eleris.TiffNet.Internal.Jpeg;

/// <summary>
/// The inverse DCT of libjpeg's "islow" method (jidctint.c, the default of libjpeg and libjpeg-turbo), reproduced bit for bit: a
/// separable integer algorithm (Loeffler, Ligtenberg and Moschytz) with 13-bit constants, columns first with 2 extra bits of
/// precision, then rows, and the same range limiting of the result (a 10-bit wrap, then a clamp to 0..255).
/// </summary>
internal static class JpegIdct
{
    private const int ConstBits = 13, Pass1Bits = 2;

    private const int Fix0298631336 = 2446, Fix0390180644 = 3196, Fix0541196100 = 4433, Fix0765366865 = 6270, Fix0899976223 = 7373,
        Fix1175875602 = 9633, Fix1501321110 = 12299, Fix1847759065 = 15137, Fix1961570560 = 16069, Fix2053119869 = 16819,
        Fix2562915447 = 20995, Fix3072711026 = 25172;

    /// <summary>Transforms one block of coefficients (natural order) with its quantization table into 8 x 8 samples.</summary>
    /// <param name="coefficients">64 quantized coefficients, in natural (row by row) order.</param>
    /// <param name="quantization">64 quantization steps, in natural order.</param>
    /// <param name="output">The first sample of the block in its plane.</param>
    /// <param name="stride">The bytes from one row of the plane to the next.</param>
    [SkipLocalsInit]
    public static void Transform(ReadOnlySpan<short> coefficients, ReadOnlySpan<ushort> quantization, Span<byte> output, int stride)
    {
        Span<int> work = stackalloc int[64];

        // Pass 1: the columns, from the coefficients; results scaled up by 2^Pass1Bits.
        for (int c = 0; c < 8; c++)
        {
            if (coefficients[8 + c] == 0 && coefficients[16 + c] == 0 && coefficients[24 + c] == 0 && coefficients[32 + c] == 0
                && coefficients[40 + c] == 0 && coefficients[48 + c] == 0 && coefficients[56 + c] == 0)
            {
                // Only the DC term: the column is constant.
                int dc = coefficients[c] * quantization[c] << Pass1Bits;
                for (int r = 0; r < 8; r++)
                {
                    work[r * 8 + c] = dc;
                }

                continue;
            }

            long z2 = coefficients[16 + c] * quantization[16 + c];
            long z3 = coefficients[48 + c] * quantization[48 + c];
            long z1 = (z2 + z3) * Fix0541196100;
            long tmp2 = z1 + z3 * -Fix1847759065;
            long tmp3 = z1 + z2 * Fix0765366865;
            z2 = coefficients[c] * quantization[c];
            z3 = coefficients[32 + c] * quantization[32 + c];
            long tmp0 = (z2 + z3) << ConstBits;
            long tmp1 = (z2 - z3) << ConstBits;
            long tmp10 = tmp0 + tmp3, tmp13 = tmp0 - tmp3, tmp11 = tmp1 + tmp2, tmp12 = tmp1 - tmp2;

            Odd(coefficients[56 + c] * quantization[56 + c], coefficients[40 + c] * quantization[40 + c],
                coefficients[24 + c] * quantization[24 + c], coefficients[8 + c] * quantization[8 + c],
                out tmp0, out tmp1, out tmp2, out tmp3);

            const int Shift = ConstBits - Pass1Bits;
            work[c] = Descale(tmp10 + tmp3, Shift);
            work[56 + c] = Descale(tmp10 - tmp3, Shift);
            work[8 + c] = Descale(tmp11 + tmp2, Shift);
            work[48 + c] = Descale(tmp11 - tmp2, Shift);
            work[16 + c] = Descale(tmp12 + tmp1, Shift);
            work[40 + c] = Descale(tmp12 - tmp1, Shift);
            work[24 + c] = Descale(tmp13 + tmp0, Shift);
            work[32 + c] = Descale(tmp13 - tmp0, Shift);
        }

        // Pass 2: the rows, into samples; the scale of pass 1 and the 8 of the transform are taken out.
        for (int r = 0; r < 8; r++)
        {
            Span<int> w = work.Slice(r * 8, 8);
            Span<byte> row = output.Slice(r * stride, 8);
            const int Shift = ConstBits + Pass1Bits + 3;
            if (w[1] == 0 && w[2] == 0 && w[3] == 0 && w[4] == 0 && w[5] == 0 && w[6] == 0 && w[7] == 0)
            {
                row.Fill(Limit(Descale(w[0], Pass1Bits + 3)));
                continue;
            }

            long z2 = w[2], z3 = w[6];
            long z1 = (z2 + z3) * Fix0541196100;
            long tmp2 = z1 + z3 * -Fix1847759065;
            long tmp3 = z1 + z2 * Fix0765366865;
            long tmp0 = ((long)w[0] + w[4]) << ConstBits;
            long tmp1 = ((long)w[0] - w[4]) << ConstBits;
            long tmp10 = tmp0 + tmp3, tmp13 = tmp0 - tmp3, tmp11 = tmp1 + tmp2, tmp12 = tmp1 - tmp2;

            Odd(w[7], w[5], w[3], w[1], out tmp0, out tmp1, out tmp2, out tmp3);

            row[0] = Limit(Descale(tmp10 + tmp3, Shift));
            row[7] = Limit(Descale(tmp10 - tmp3, Shift));
            row[1] = Limit(Descale(tmp11 + tmp2, Shift));
            row[6] = Limit(Descale(tmp11 - tmp2, Shift));
            row[2] = Limit(Descale(tmp12 + tmp1, Shift));
            row[5] = Limit(Descale(tmp12 - tmp1, Shift));
            row[3] = Limit(Descale(tmp13 + tmp0, Shift));
            row[4] = Limit(Descale(tmp13 - tmp0, Shift));
        }
    }

    /// <summary>The odd part of the 8-point transform, from the inputs 7, 5, 3 and 1.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Odd(long in7, long in5, long in3, long in1, out long tmp0, out long tmp1, out long tmp2, out long tmp3)
    {
        long z1 = in7 + in1, z2 = in5 + in3, z3 = in7 + in3, z4 = in5 + in1;
        long z5 = (z3 + z4) * Fix1175875602;
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
    private static int Descale(long value, int n) => (int)((value + (1L << (n - 1))) >> n);

    /// <summary>libjpeg's range limit after the IDCT: the value plus 128, wrapped to 10 bits as a signed value, clamped to a byte.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static byte Limit(int value)
    {
        int wrapped = ((value & 1023) ^ 512) - 512;
        return (byte)Math.Clamp(wrapped + 128, 0, 255);
    }
}
