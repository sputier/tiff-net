using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.Arm;
using System.Runtime.Intrinsics.X86;

namespace Eleris.TiffNet.Internal.Jpeg;

/// <summary>
/// The samples of a decoded frame as an image: the components upsampled to the size of the frame with libjpeg's default
/// ("fancy") filters, then, for YCbCr, converted to RGB with libjpeg's integer tables, so that the result is what libjpeg-turbo
/// gives (jdsample.c and jdcolor.c).
/// </summary>
/// <remarks>
/// The colour conversion and the fancy filters run 16 samples at a time with 128-bit vectors (SSSE3 or AdvSimd), the rest of a
/// row as the scalar code: the vectors compute libjpeg's integer arithmetic itself (the products its tables hold, rounded the
/// same, then clamped as its range limit does), so every sample is the one of the scalar code, bit for bit (tested against it).
/// </remarks>
internal static class JpegColour
{
    private const int ScaleBits = 16, OneHalf = 1 << (ScaleBits - 1);

    private static readonly int[] CrRed = new int[256], CbBlue = new int[256];
    private static readonly int[] CrGreen = new int[256], CbGreen = new int[256];

#pragma warning disable CA1810 // The tables are computed once, as libjpeg's build_ycc_rgb_table does.
    static JpegColour()
#pragma warning restore CA1810
    {
        static long Fix(double x) => (long)(x * (1L << ScaleBits) + 0.5);
        for (int i = 0, x = -128; i < 256; i++, x++)
        {
            CrRed[i] = (int)((Fix(1.40200) * x + OneHalf) >> ScaleBits);
            CbBlue[i] = (int)((Fix(1.77200) * x + OneHalf) >> ScaleBits);
            CrGreen[i] = (int)(-Fix(0.71414) * x);              // within +-6 million: their sum fits an int
            CbGreen[i] = (int)(-Fix(0.34414) * x + OneHalf);
        }
    }

    /// <summary>The number of samples per pixel the frame gives: 1 for grey, 3 for colour.</summary>
    /// <exception cref="InvalidDataException">The frame has 2 components, which make no image.</exception>
    public static int Channels(JpegFrame frame) => frame.Components.Length switch
    {
        1 => 1,
        3 => 3,
        var n => throw new InvalidDataException($"a JPEG frame of {n} components is neither grey nor colour."),
    };

    /// <summary>
    /// Writes the first <paramref name="rows"/> rows of the frame as interleaved samples, <paramref name="stride"/> bytes per row.
    /// </summary>
    /// <param name="frame">A decoded frame of 1 or 3 components.</param>
    /// <param name="rows">The rows to write, at most the height of the frame.</param>
    /// <param name="ycbcr">Whether 3 components are YCbCr, converted to RGB; otherwise they are given as they are.</param>
    /// <param name="output">At least <c>Height × stride</c> bytes.</param>
    /// <param name="stride">The bytes of a row of <paramref name="output"/>, at least <c>Width × Channels</c>.</param>
    /// <param name="budget">Counts the upsampled components and checks cancellation.</param>
    /// <exception cref="NotSupportedException">The sampling factors are not whole multiples of each other.</exception>
    public static void ToSamples(JpegFrame frame, int rows, bool ycbcr, Span<byte> output, int stride, ResourceBudget budget)
    {
        int width = frame.Width, height = Math.Min(rows, frame.Height);
        _ = Channels(frame);
        if (frame.Components.Length == 1)
        {
            var grey = frame.Components[0];
            if (grey.Stride == stride && output.Overlaps(grey.Plane, out int offset) && offset == 0)
            {
                // The blocks were transformed into the output rows themselves (see JpegDecoder.GreyOutput).
                return;
            }

            for (int y = 0; y < height; y++)
            {
                grey.Plane.AsSpan(y * grey.Stride, width).CopyTo(output.Slice(y * stride, width));
            }

            return;
        }

        // Row by row: each component's row at the size of the frame (upsampled into a small buffer when it must be), then the
        // three interleaved (and converted). Nothing of the size of the image is allocated.
        int hMax = frame.Components.Max(c => c.H), vMax = frame.Components.Max(c => c.V);
        foreach (var c in frame.Components)
        {
            if (hMax % c.H != 0 || vMax % c.V != 0)
            {
                throw new NotSupportedException($"JPEG sampling factors of {c.H} x {c.V} within {hMax} x {vMax} (fractional) are not supported.");
            }
        }

        var buffers = new byte[3][];
        for (int i = 0; i < 3; i++)
        {
            buffers[i] = new byte[2 * frame.Components[i].Width + width + 2];
        }

        ReadOnlySpan<byte> limit = RangeLimit;
        for (int y = 0; y < height; y++)
        {
            if ((y & 63) == 0)
            {
                budget.ThrowIfCancelled();
            }

            var luma = Row(frame.Components[0], hMax, vMax, y, width, buffers[0]);
            var blue = Row(frame.Components[1], hMax, vMax, y, width, buffers[1]);
            var red = Row(frame.Components[2], hMax, vMax, y, width, buffers[2]);
            var row = output.Slice(y * stride, width * 3);
            if (!ycbcr)
            {
                for (int x = 0; x < width; x++)
                {
                    (row[3 * x], row[3 * x + 1], row[3 * x + 2]) = (luma[x], blue[x], red[x]);
                }

                continue;
            }

            Convert(luma, blue, red, row, width, limit);
        }
    }

    /// <summary>YCbCr to RGB for a row (jdcolor.c's ycc_rgb_convert), through references: the hot loop of colour images.</summary>
    internal static void Convert(ReadOnlySpan<byte> luma, ReadOnlySpan<byte> blue, ReadOnlySpan<byte> red, Span<byte> row, int width,
        ReadOnlySpan<byte> limit)
    {
        if (luma.Length < width || blue.Length < width || red.Length < width || row.Length < 3 * width || limit.Length < 768)
        {
            throw new ArgumentException("The rows are shorter than the width.");
        }

        int done = ConvertVectors(luma, blue, red, row, width);
        ConvertScalar(luma, blue, red, row, width, limit, done);
    }

    /// <summary>The scalar conversion from pixel <paramref name="start"/> (the reference of the vectors), the lengths checked.</summary>
    internal static void ConvertScalar(ReadOnlySpan<byte> luma, ReadOnlySpan<byte> blue, ReadOnlySpan<byte> red, Span<byte> row, int width,
        ReadOnlySpan<byte> limit, int start)
    {
        if (luma.Length < width || blue.Length < width || red.Length < width || row.Length < 3 * width || limit.Length < 768)
        {
            throw new ArgumentException("The rows are shorter than the width.");
        }

        ref byte y0 = ref MemoryMarshal.GetReference(luma);
        ref byte cb0 = ref MemoryMarshal.GetReference(blue);
        ref byte cr0 = ref MemoryMarshal.GetReference(red);
        ref byte out0 = ref MemoryMarshal.GetReference(row);
        ref byte limit0 = ref MemoryMarshal.GetReference(limit);
        ref int crRed = ref MemoryMarshal.GetArrayDataReference(CrRed);
        ref int cbBlue = ref MemoryMarshal.GetArrayDataReference(CbBlue);
        ref int crGreen = ref MemoryMarshal.GetArrayDataReference(CrGreen);
        ref int cbGreen = ref MemoryMarshal.GetArrayDataReference(CbGreen);
        for (int x = start; x < width; x++)
        {
            // The spans were checked by their slicing: luma, blue and red hold width samples, row 3 x width, limit 768 values
            // (an index of 256 + 0..255 + at most +-227).
            int l = Unsafe.Add(ref y0, x) + 256, cb = Unsafe.Add(ref cb0, x), cr = Unsafe.Add(ref cr0, x);
            ref byte o = ref Unsafe.Add(ref out0, 3 * x);
            o = Unsafe.Add(ref limit0, l + Unsafe.Add(ref crRed, cr));
            Unsafe.Add(ref o, 1) = Unsafe.Add(ref limit0, l + ((Unsafe.Add(ref cbGreen, cb) + Unsafe.Add(ref crGreen, cr)) >> ScaleBits));
            Unsafe.Add(ref o, 2) = Unsafe.Add(ref limit0, l + Unsafe.Add(ref cbBlue, cb));
        }
    }

    // The products of the tables of build_ycc_rgb_table, computed in the lanes: x × FIX(c) (+ ONE_HALF for those that round).
    private const int FixCrRed = (int)(1.40200 * (1 << ScaleBits) + 0.5), FixCbBlue = (int)(1.77200 * (1 << ScaleBits) + 0.5);
    private const int FixCrGreen = (int)(0.71414 * (1 << ScaleBits) + 0.5), FixCbGreen = (int)(0.34414 * (1 << ScaleBits) + 0.5);

    /// <summary>
    /// Where each byte of three 16-byte chunks of interleaved RGB comes from: for chunk j, the lanes of R, then G, then B (0x80:
    /// none); byte i of the chunk is pixel (16j + i) / 3, component (16j + i) % 3.
    /// </summary>
    private static readonly Vector128<byte>[] Interleave = BuildInterleave();

    private static Vector128<byte>[] BuildInterleave()
    {
        var tables = new Vector128<byte>[9];
        Span<byte> lanes = stackalloc byte[16];
        for (int j = 0; j < 3; j++)
        {
            for (int component = 0; component < 3; component++)
            {
                for (int i = 0; i < 16; i++)
                {
                    int q = 16 * j + i;
                    lanes[i] = q % 3 == component ? (byte)(q / 3) : (byte)0x80;
                }

                tables[3 * j + component] = Vector128.Create<byte>(lanes);
            }
        }

        return tables;
    }

    /// <summary>Whether the rows go through vectors here: byte shuffles of 128 bits.</summary>
    private static bool Shuffles => Vector128.IsHardwareAccelerated && (Ssse3.IsSupported || AdvSimd.Arm64.IsSupported);

    /// <summary>The conversion of whole groups of 16 pixels with vectors; the number of pixels done.</summary>
    private static int ConvertVectors(ReadOnlySpan<byte> luma, ReadOnlySpan<byte> blue, ReadOnlySpan<byte> red, Span<byte> row, int width)
    {
        if (!Shuffles)
        {
            return 0;
        }

        var tables = Interleave;
        int x = 0;
        for (; x + 16 <= width; x += 16)
        {
            var y = Vector128.Create<byte>(luma[x..]);
            var cb = Vector128.Create<byte>(blue[x..]);
            var cr = Vector128.Create<byte>(red[x..]);
            var r = Channel(y, cb, cr, 0);
            var g = Channel(y, cb, cr, 1);
            var b = Channel(y, cb, cr, 2);
            var target = row[(3 * x)..];
            for (int j = 0; j < 3; j++)
            {
                (Lookup(r, tables[3 * j]) | Lookup(g, tables[3 * j + 1]) | Lookup(b, tables[3 * j + 2])).CopyTo(target[(16 * j)..]);
            }
        }

        return x;
    }

    /// <summary>One component of 16 pixels (0: red, 1: green, 2: blue), as ycc_rgb_convert computes it, in 32-bit lanes.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector128<byte> Channel(Vector128<byte> y, Vector128<byte> cb, Vector128<byte> cr, int component)
    {
        var (y0, y1) = Vector128.Widen(y);
        var (cb0, cb1) = Vector128.Widen(cb);
        var (cr0, cr1) = Vector128.Widen(cr);
        var a = Quarter(Vector128.WidenLower(y0), Vector128.WidenLower(cb0), Vector128.WidenLower(cr0), component);
        var b = Quarter(Vector128.WidenUpper(y0), Vector128.WidenUpper(cb0), Vector128.WidenUpper(cr0), component);
        var c = Quarter(Vector128.WidenLower(y1), Vector128.WidenLower(cb1), Vector128.WidenLower(cr1), component);
        var d = Quarter(Vector128.WidenUpper(y1), Vector128.WidenUpper(cb1), Vector128.WidenUpper(cr1), component);
        return Vector128.Narrow(Vector128.Narrow(a, b), Vector128.Narrow(c, d));
    }

    /// <summary>One component of 4 pixels, clamped to 0..255 (the range limit of libjpeg: the sums stay within -256..511).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector128<uint> Quarter(Vector128<uint> y, Vector128<uint> cb, Vector128<uint> cr, int component)
    {
        var luma = y.AsInt32();
        var blueX = cb.AsInt32() - Vector128.Create(128);
        var redX = cr.AsInt32() - Vector128.Create(128);
        var half = Vector128.Create(OneHalf);
        var shift = component switch
        {
            0 => (redX * FixCrRed + half) >> ScaleBits,
            1 => (blueX * -FixCbGreen + half + redX * -FixCrGreen) >> ScaleBits,
            _ => (blueX * FixCbBlue + half) >> ScaleBits,
        };
        return Vector128.Clamp(luma + shift, Vector128<int>.Zero, Vector128.Create(255)).AsUInt32();
    }

    /// <summary>Bytes of <paramref name="x"/> at <paramref name="indices"/>; an index of 0x80 gives 0.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector128<byte> Lookup(Vector128<byte> x, Vector128<byte> indices) =>
        Ssse3.IsSupported ? Ssse3.Shuffle(x, indices) : AdvSimd.Arm64.VectorTableLookup(x, indices);

    /// <summary>Pairs the lanes of <paramref name="even"/> and <paramref name="odd"/> (e0, o0, e1, o1…) into 32 bytes.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Zip(Vector128<byte> even, Vector128<byte> odd, Span<byte> output)
    {
        if (Sse2.IsSupported)
        {
            Sse2.UnpackLow(even, odd).CopyTo(output);
            Sse2.UnpackHigh(even, odd).CopyTo(output[16..]);
        }
        else
        {
            AdvSimd.Arm64.ZipLow(even, odd).CopyTo(output);
            AdvSimd.Arm64.ZipHigh(even, odd).CopyTo(output[16..]);
        }
    }

    /// <summary>A value clamped to 0..255, indexed by the value plus 256 (libjpeg's range_limit, for -256 to 511).</summary>
    private static readonly byte[] RangeLimit = [.. Enumerable.Range(-256, 768).Select(v => (byte)Math.Clamp(v, 0, 255))];

    /// <summary>
    /// Row <paramref name="y"/> of a component at the size of the frame. As libjpeg-turbo chooses: the row itself at full
    /// resolution; the triangle ("fancy") filters for 2:1 horizontally (h2v1, when the component is wider than 2), 2:1
    /// vertically (h1v2) and both (h2v2, wider than 2); replication for any other whole ratio.
    /// </summary>
    private static ReadOnlySpan<byte> Row(JpegComponent c, int hMax, int vMax, int y, int width, byte[] buffer)
    {
        int hr = hMax / c.H, vr = vMax / c.V;
        int w = c.Width, h = c.Height;
        var target = buffer.AsSpan(0, width);

        // A row of the component; rows beyond it repeat its edge (as libjpeg's context rows do at the bottom and the top).
        ReadOnlySpan<byte> Source(int sy) => c.Plane.AsSpan(Math.Clamp(sy, 0, h - 1) * c.Stride, w);

        if (hr == 1 && vr == 1)
        {
            return c.Plane.AsSpan(y * c.Stride, width);
        }

        if (hr == 2 && vr == 1 && w > 2)
        {
            FancyH2V1(Source(y), buffer);
            return target;
        }

        if (hr == 1 && vr == 2)
        {
            int sy = y / 2;
            var near = Source(sy);
            var far = Source(y % 2 == 0 ? sy - 1 : sy + 1);
            FancyH1V2(near, far, y % 2 == 0 ? 1 : 2, target);
            return target;
        }

        if (hr == 2 && vr == 2 && w > 2)
        {
            int sy = y / 2;
            FancyH2V2(Source(sy), Source(y % 2 == 0 ? sy - 1 : sy + 1), buffer);
            return target;
        }

        var source = Source(y / vr);
        for (int x = 0; x < width; x++)
        {
            target[x] = source[x / hr];
        }

        return target;
    }

    /// <summary>libjpeg's h2v1_fancy_upsample: each output sample is 3/4 of the nearer input and 1/4 of the further one.</summary>
    internal static void FancyH2V1(ReadOnlySpan<byte> input, Span<byte> output, bool vectors = true)
    {
        int w = input.Length;
        int value = input[0];
        output[0] = (byte)value;
        output[1] = (byte)((value * 3 + input[1] + 2) >> 2);
        int start = 1;
        if (vectors && Shuffles)
        {
            // Inputs x - 1 to x + 16 within the row: 16 inputs, 32 outputs at a time.
            var one = Vector128.Create((ushort)1);
            var two = Vector128.Create((ushort)2);
            for (; start + 17 <= w; start += 16)
            {
                var (c0, c1) = Vector128.Widen(Vector128.Create<byte>(input[start..]));
                var (p0, p1) = Vector128.Widen(Vector128.Create<byte>(input[(start - 1)..]));
                var (n0, n1) = Vector128.Widen(Vector128.Create<byte>(input[(start + 1)..]));
                c0 *= 3;
                c1 *= 3;
                var even = Vector128.Narrow((c0 + p0 + one) >> 2, (c1 + p1 + one) >> 2);
                var odd = Vector128.Narrow((c0 + n0 + two) >> 2, (c1 + n1 + two) >> 2);
                Zip(even, odd, output[(2 * start)..]);
            }
        }

        for (int x = start; x < w - 1; x++)
        {
            value = input[x] * 3;
            output[2 * x] = (byte)((value + input[x - 1] + 1) >> 2);
            output[2 * x + 1] = (byte)((value + input[x + 1] + 2) >> 2);
        }

        value = input[w - 1];
        output[2 * w - 2] = (byte)((value * 3 + input[w - 2] + 1) >> 2);
        output[2 * w - 1] = (byte)value;
    }

    /// <summary>libjpeg's h2v2_fancy_upsample for one output row: the nearer input row (3/4) and the further one (1/4), then the same horizontally.</summary>
    internal static void FancyH2V2(ReadOnlySpan<byte> near, ReadOnlySpan<byte> far, Span<byte> output, bool vectors = true)
    {
        int w = near.Length;
        int thisSum = near[0] * 3 + far[0];
        int nextSum = near[1] * 3 + far[1];
        output[0] = (byte)((thisSum * 4 + 8) >> 4);
        output[1] = (byte)((thisSum * 3 + nextSum + 7) >> 4);
        int start = 1;
        if (vectors && Shuffles)
        {
            // The column sums of inputs x - 1 to x + 16 (3 near + far, at most 1020): 16 inputs, 32 outputs at a time.
            var seven = Vector128.Create((ushort)7);
            var eight = Vector128.Create((ushort)8);
            for (; start + 17 <= w; start += 16)
            {
                var (t0, t1) = Sums(near, far, start);
                var (l0, l1) = Sums(near, far, start - 1);
                var (n0, n1) = Sums(near, far, start + 1);
                t0 *= 3;
                t1 *= 3;
                var even = Vector128.Narrow((t0 + l0 + eight) >> 4, (t1 + l1 + eight) >> 4);
                var odd = Vector128.Narrow((t0 + n0 + seven) >> 4, (t1 + n1 + seven) >> 4);
                Zip(even, odd, output[(2 * start)..]);
            }
        }

        // The loop goes on from where the vectors stopped: the sums before it, as it keeps them.
        int lastSum = near[start - 1] * 3 + far[start - 1];
        thisSum = near[start] * 3 + far[start];
        for (int x = start; x < w - 1; x++)
        {
            nextSum = near[x + 1] * 3 + far[x + 1];
            output[2 * x] = (byte)((thisSum * 3 + lastSum + 8) >> 4);
            output[2 * x + 1] = (byte)((thisSum * 3 + nextSum + 7) >> 4);
            lastSum = thisSum;
            thisSum = nextSum;
        }

        output[2 * w - 2] = (byte)((thisSum * 3 + lastSum + 8) >> 4);
        output[2 * w - 1] = (byte)((thisSum * 4 + 7) >> 4);
    }

    /// <summary>libjpeg's h1v2_fancy_upsample for one output row: 3/4 of the nearer input row and 1/4 of the further one.</summary>
    internal static void FancyH1V2(ReadOnlySpan<byte> near, ReadOnlySpan<byte> far, int bias, Span<byte> output, bool vectors = true)
    {
        int width = output.Length, x = 0;
        if (vectors && Shuffles)
        {
            var add = Vector128.Create((ushort)bias);
            for (; x + 16 <= width; x += 16)
            {
                var (n0, n1) = Vector128.Widen(Vector128.Create<byte>(near[x..]));
                var (f0, f1) = Vector128.Widen(Vector128.Create<byte>(far[x..]));
                Vector128.Narrow((n0 * 3 + f0 + add) >> 2, (n1 * 3 + f1 + add) >> 2).CopyTo(output[x..]);
            }
        }

        for (; x < width; x++)
        {
            output[x] = (byte)((near[x] * 3 + far[x] + bias) >> 2);
        }
    }

    /// <summary>The column sums (3 near + far) of 16 inputs from <paramref name="x"/>, as two halves.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static (Vector128<ushort> Low, Vector128<ushort> High) Sums(ReadOnlySpan<byte> near, ReadOnlySpan<byte> far, int x)
    {
        var (n0, n1) = Vector128.Widen(Vector128.Create<byte>(near[x..]));
        var (f0, f1) = Vector128.Widen(Vector128.Create<byte>(far[x..]));
        return (n0 * 3 + f0, n1 * 3 + f1);
    }
}
