using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Eleris.TiffNet.Internal.Jpeg;

/// <summary>
/// The samples of a decoded frame as an image: the components upsampled to the size of the frame with libjpeg's default
/// ("fancy") filters, then, for YCbCr, converted to RGB with libjpeg's integer tables, so that the result is what libjpeg-turbo
/// gives (jdsample.c and jdcolor.c).
/// </summary>
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
    private static void Convert(ReadOnlySpan<byte> luma, ReadOnlySpan<byte> blue, ReadOnlySpan<byte> red, Span<byte> row, int width,
        ReadOnlySpan<byte> limit)
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
        for (int x = 0; x < width; x++)
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
            int bias = y % 2 == 0 ? 1 : 2;
            for (int x = 0; x < width; x++)
            {
                target[x] = (byte)((near[x] * 3 + far[x] + bias) >> 2);
            }

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
    private static void FancyH2V1(ReadOnlySpan<byte> input, Span<byte> output)
    {
        int w = input.Length;
        int value = input[0];
        output[0] = (byte)value;
        output[1] = (byte)((value * 3 + input[1] + 2) >> 2);
        for (int x = 1; x < w - 1; x++)
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
    private static void FancyH2V2(ReadOnlySpan<byte> near, ReadOnlySpan<byte> far, Span<byte> output)
    {
        int w = near.Length;
        int thisSum = near[0] * 3 + far[0];
        int nextSum = near[1] * 3 + far[1];
        output[0] = (byte)((thisSum * 4 + 8) >> 4);
        output[1] = (byte)((thisSum * 3 + nextSum + 7) >> 4);
        int lastSum = thisSum;
        thisSum = nextSum;
        for (int x = 1; x < w - 1; x++)
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
}
