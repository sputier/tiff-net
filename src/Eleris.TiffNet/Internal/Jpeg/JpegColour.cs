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
    private static readonly long[] CrGreen = new long[256], CbGreen = new long[256];

#pragma warning disable CA1810 // The tables are computed once, as libjpeg's build_ycc_rgb_table does.
    static JpegColour()
#pragma warning restore CA1810
    {
        static long Fix(double x) => (long)(x * (1L << ScaleBits) + 0.5);
        for (int i = 0, x = -128; i < 256; i++, x++)
        {
            CrRed[i] = (int)((Fix(1.40200) * x + OneHalf) >> ScaleBits);
            CbBlue[i] = (int)((Fix(1.77200) * x + OneHalf) >> ScaleBits);
            CrGreen[i] = -Fix(0.71414) * x;
            CbGreen[i] = -Fix(0.34414) * x + OneHalf;
        }
    }

    /// <summary>The number of samples per pixel the frame gives: 1 for grey, 3 for colour.</summary>
    public static int Channels(JpegFrame frame) => frame.Components.Length == 1 ? 1 : 3;

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
        if (frame.Components.Length == 1)
        {
            var grey = frame.Components[0];
            for (int y = 0; y < height; y++)
            {
                grey.Plane.AsSpan(y * grey.Stride, width).CopyTo(output.Slice(y * stride, width));
            }

            return;
        }

        // Each component at the size of the frame, then the three are interleaved (and converted).
        int hMax = frame.Components.Max(c => c.H), vMax = frame.Components.Max(c => c.V);
        long reserved = 3L * width * height;
        budget.ReservePixels(reserved, "the upsampled JPEG components");
        try
        {
            var full = new byte[3][];
            for (int i = 0; i < 3; i++)
            {
                full[i] = Upsample(frame.Components[i], hMax, vMax, width, height);
            }

            Interleave(full, width, height, ycbcr, output, stride, budget);
        }
        finally
        {
            budget.ReleasePixels(reserved);
        }
    }

    private static void Interleave(byte[][] full, int width, int height, bool ycbcr, Span<byte> output, int stride, ResourceBudget budget)
    {
        byte[] y0 = full[0], cb0 = full[1], cr0 = full[2];
        for (int y = 0; y < height; y++)
        {
            budget.ThrowIfCancelled();
            var row = output.Slice(y * stride, width * 3);
            int at = y * width;
            for (int x = 0; x < width; x++, at++)
            {
                int luma = y0[at], cb = cb0[at], cr = cr0[at];
                if (ycbcr)
                {
                    row[3 * x] = Clamp(luma + CrRed[cr]);
                    row[3 * x + 1] = Clamp(luma + (int)((CbGreen[cb] + CrGreen[cr]) >> ScaleBits));
                    row[3 * x + 2] = Clamp(luma + CbBlue[cb]);
                }
                else
                {
                    (row[3 * x], row[3 * x + 1], row[3 * x + 2]) = ((byte)luma, (byte)cb, (byte)cr);
                }
            }
        }
    }

    private static byte Clamp(int value) => (byte)Math.Clamp(value, 0, 255);

    /// <summary>
    /// A component at the size of the frame. As libjpeg-turbo chooses: none for full resolution; the triangle ("fancy") filters
    /// for 2:1 horizontally (h2v1, when the component is wider than 2), 2:1 vertically (h1v2) and both (h2v2, wider than 2);
    /// replication for any other whole ratio.
    /// </summary>
    private static byte[] Upsample(JpegComponent c, int hMax, int vMax, int width, int height)
    {
        if (hMax % c.H != 0 || vMax % c.V != 0)
        {
            throw new NotSupportedException($"JPEG sampling factors of {c.H} x {c.V} within {hMax} x {vMax} (fractional) are not supported.");
        }

        int hr = hMax / c.H, vr = vMax / c.V;
        var full = new byte[width * height];
        int w = c.Width, h = c.Height;

        // A row of the component; rows beyond it repeat its last (as libjpeg's context rows do at the bottom and the top).
        ReadOnlySpan<byte> Row(int y) => c.Plane.AsSpan(Math.Clamp(y, 0, h - 1) * c.Stride, w);

        byte[] line = new byte[2 * w + 2];
        for (int oy = 0; oy < height; oy++)
        {
            var target = full.AsSpan(oy * width, width);
            if (hr == 1 && vr == 1)
            {
                Row(oy).CopyTo(target);
            }
            else if (hr == 2 && vr == 1 && w > 2)
            {
                FancyH2V1(Row(oy), line);
                line.AsSpan(0, width).CopyTo(target);
            }
            else if (hr == 1 && vr == 2)
            {
                int sy = oy / 2;
                var near = Row(sy);
                var far = Row(oy % 2 == 0 ? sy - 1 : sy + 1);
                int bias = oy % 2 == 0 ? 1 : 2;
                for (int x = 0; x < width; x++)
                {
                    target[x] = (byte)((near[x] * 3 + far[x] + bias) >> 2);
                }
            }
            else if (hr == 2 && vr == 2 && w > 2)
            {
                int sy = oy / 2;
                FancyH2V2(Row(sy), Row(oy % 2 == 0 ? sy - 1 : sy + 1), line);
                line.AsSpan(0, width).CopyTo(target);
            }
            else
            {
                var source = Row(oy / vr);
                for (int x = 0; x < width; x++)
                {
                    target[x] = source[x / hr];
                }
            }
        }

        return full;
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
