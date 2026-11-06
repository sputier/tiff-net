using System.Buffers.Binary;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.Arm;
using System.Runtime.Intrinsics.X86;

namespace Eleris.TiffNet.Internal.Codecs;

/// <summary>
/// Undoes the predictors of TIFF on a decoded row, in place: the horizontal differencing of TIFF 6.0 (section 14) on integer
/// samples of 8 to 64 bits, and the floating point predictor of Adobe's Technical Note 3 on samples of 16 to 64 bits. The row
/// stays in the byte order of the file, as the unpacking of the samples expects it.
/// </summary>
/// <remarks>
/// Each row is undone where it lies, through typed spans, without allocating: the floating point predictor uses a row of scratch
/// given by the caller. Accumulating is a chain of dependent additions, about one sample per cycle (1 to 3 GB/s): fast next to
/// the decompression that precedes it, but not negligible for 8-bit and floating point samples, whose accumulation runs byte by
/// byte; vectorising it is measured with the benchmarks of the library (issue #48).
/// </remarks>
internal static class Predictors
{
    /// <summary>Undoes horizontal differencing: each sample is the sum of itself and the sample <paramref name="stride"/> before.</summary>
    /// <param name="row">A row of whole samples, in the byte order of the file.</param>
    /// <param name="bytesPerSample">1, 2, 4 or 8.</param>
    /// <param name="stride">The samples per pixel of the row (1 for a plane).</param>
    /// <param name="littleEndian">Whether the file is little endian.</param>
    public static void UndoHorizontal(Span<byte> row, int bytesPerSample, int stride, bool littleEndian)
    {
        bool swap = littleEndian != BitConverter.IsLittleEndian && bytesPerSample > 1;
        switch (bytesPerSample)
        {
            case 1:
                Accumulate(row, stride);
                break;
            case 2:
                Accumulate(Native<ushort>(row, swap), stride);
                break;
            case 4:
                Accumulate(Native<uint>(row, swap), stride);
                break;
            default:
                Accumulate(Native<ulong>(row, swap), stride);
                break;
        }

        if (swap)
        {
            // Back to the byte order of the file.
            Swap(row, bytesPerSample);
        }
    }

    /// <summary>
    /// Undoes the floating point predictor: the bytes of the row are summed with a stride of <paramref name="stride"/> bytes, then
    /// put back together: the row holds the most significant bytes of all its samples, then the next ones, and so on.
    /// </summary>
    /// <param name="row">A row of whole samples.</param>
    /// <param name="scratch">At least the length of <paramref name="row"/>.</param>
    /// <param name="bytesPerSample">2, 4 or 8.</param>
    /// <param name="stride">The samples per pixel of the row (1 for a plane).</param>
    /// <param name="littleEndian">Whether the file is little endian: the samples are written in that order.</param>
    public static void UndoFloatingPoint(Span<byte> row, Span<byte> scratch, int bytesPerSample, int stride, bool littleEndian)
    {
        // Summed in place, then copied: measured faster than summing into the scratch (issue #106).
        Accumulate(row, stride);
        row.CopyTo(scratch);

        int count = row.Length / bytesPerSample;
        ref byte planes = ref MemoryMarshal.GetReference(scratch);
        ref byte output = ref MemoryMarshal.GetReference(row);
        bool vectors = littleEndian && BitConverter.IsLittleEndian;
        switch (bytesPerSample)
        {
            case 2:
                for (int i = vectors ? Interleave2(scratch, row, count) : 0; i < count; i++)
                {
                    ushort v = (ushort)(Unsafe.Add(ref planes, i) << 8 | Unsafe.Add(ref planes, count + i));
                    Unsafe.WriteUnaligned(ref Unsafe.Add(ref output, i * 2), littleEndian ? v : BinaryPrimitives.ReverseEndianness(v));
                }

                break;
            case 4:
                for (int i = vectors ? Interleave4(scratch, row, count) : 0; i < count; i++)
                {
                    uint v = (uint)Unsafe.Add(ref planes, i) << 24 | (uint)Unsafe.Add(ref planes, count + i) << 16
                        | (uint)Unsafe.Add(ref planes, 2 * count + i) << 8 | Unsafe.Add(ref planes, 3 * count + i);
                    Unsafe.WriteUnaligned(ref Unsafe.Add(ref output, i * 4), littleEndian ? v : BinaryPrimitives.ReverseEndianness(v));
                }

                break;
            default:
                for (int i = vectors ? Interleave8(scratch, row, count) : 0; i < count; i++)
                {
                    ulong v = 0;
                    for (int b = 0; b < 8; b++)
                    {
                        v = v << 8 | Unsafe.Add(ref planes, b * count + i);
                    }

                    Unsafe.WriteUnaligned(ref Unsafe.Add(ref output, i * 8), littleEndian ? v : BinaryPrimitives.ReverseEndianness(v));
                }

                break;
        }

        if (!BitConverter.IsLittleEndian)
        {
            // The values were written for a little-endian machine.
            Swap(row, bytesPerSample);
        }
    }

    /// <summary>
    /// Puts the two byte planes of 16-bit samples (most significant first) back together as little-endian samples, 16 samples at
    /// once (SSE2 or AdvSimd); returns the number done.
    /// </summary>
    private static int Interleave2(ReadOnlySpan<byte> planes, Span<byte> output, int count)
    {
        if (!Sse2.IsSupported && !AdvSimd.Arm64.IsSupported)
        {
            return 0;
        }

        int i = 0;
        for (; i + 16 <= count; i += 16)
        {
            var p0 = Vector128.Create<byte>(planes[i..]);
            var p1 = Vector128.Create<byte>(planes[(count + i)..]);
            var target = output[(i * 2)..];
            ZipLow(p1, p0).CopyTo(target);
            ZipHigh(p1, p0).CopyTo(target[16..]);
        }

        return i;
    }

    /// <summary>
    /// Puts the eight byte planes of 64-bit samples (most significant first) back together as little-endian samples, 16 samples
    /// at once (SSE2 or AdvSimd): pairs of bytes, then of words, then of double words; returns the number done.
    /// </summary>
    private static int Interleave8(ReadOnlySpan<byte> planes, Span<byte> output, int count)
    {
        if (!Sse2.IsSupported && !AdvSimd.Arm64.IsSupported)
        {
            return 0;
        }

        Span<Vector128<uint>> high = stackalloc Vector128<uint>[4];
        Span<Vector128<uint>> low = stackalloc Vector128<uint>[4];
        int i = 0;
        for (; i + 16 <= count; i += 16)
        {
            // Little endian: the least significant byte (plane 7) first. Bytes 7 to 4 of each sample, then 3 to 0.
            Halves(planes, count, i, 7, high);
            Halves(planes, count, i, 3, low);
            var target = output[(i * 8)..];
            for (int j = 0; j < 4; j++)
            {
                ZipLow(high[j], low[j]).AsByte().CopyTo(target[(32 * j)..]);
                ZipHigh(high[j], low[j]).AsByte().CopyTo(target[(32 * j + 16)..]);
            }
        }

        return i;
    }

    /// <summary>
    /// Bytes <paramref name="first"/> to <paramref name="first"/> − 3 of 16 samples from <paramref name="at"/>, as double words of
    /// four samples each (in <paramref name="quarters"/>, in the order of the samples).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Halves(ReadOnlySpan<byte> planes, int count, int at, int first, Span<Vector128<uint>> quarters)
    {
        var a = Vector128.Create<byte>(planes[(first * count + at)..]);
        var b = Vector128.Create<byte>(planes[((first - 1) * count + at)..]);
        var c = Vector128.Create<byte>(planes[((first - 2) * count + at)..]);
        var d = Vector128.Create<byte>(planes[((first - 3) * count + at)..]);
        var abLow = ZipLow(a, b).AsUInt16();
        var abHigh = ZipHigh(a, b).AsUInt16();
        var cdLow = ZipLow(c, d).AsUInt16();
        var cdHigh = ZipHigh(c, d).AsUInt16();
        quarters[0] = ZipLow(abLow, cdLow).AsUInt32();
        quarters[1] = ZipHigh(abLow, cdLow).AsUInt32();
        quarters[2] = ZipLow(abHigh, cdHigh).AsUInt32();
        quarters[3] = ZipHigh(abHigh, cdHigh).AsUInt32();
    }

    /// <summary>
    /// Puts the four byte planes of 32-bit samples (most significant first) back together as little-endian samples, 16 samples at
    /// once (SSE2 or AdvSimd); returns the number done.
    /// </summary>
    private static int Interleave4(ReadOnlySpan<byte> planes, Span<byte> output, int count)
    {
        if (!Sse2.IsSupported && !AdvSimd.Arm64.IsSupported)
        {
            return 0;
        }

        int i = 0;
        for (; i + 16 <= count; i += 16)
        {
            // Little endian: the least significant byte (plane 3) first.
            var p0 = Vector128.Create<byte>(planes[i..]);
            var p1 = Vector128.Create<byte>(planes[(count + i)..]);
            var p2 = Vector128.Create<byte>(planes[(2 * count + i)..]);
            var p3 = Vector128.Create<byte>(planes[(3 * count + i)..]);
            var low32 = ZipLow(p3, p2).AsUInt16();
            var low10 = ZipLow(p1, p0).AsUInt16();
            var high32 = ZipHigh(p3, p2).AsUInt16();
            var high10 = ZipHigh(p1, p0).AsUInt16();
            var target = output[(i * 4)..];
            ZipLow(low32, low10).AsByte().CopyTo(target);
            ZipHigh(low32, low10).AsByte().CopyTo(target[16..]);
            ZipLow(high32, high10).AsByte().CopyTo(target[32..]);
            ZipHigh(high32, high10).AsByte().CopyTo(target[48..]);
        }

        return i;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector128<byte> ZipLow(Vector128<byte> a, Vector128<byte> b) => Sse2.IsSupported ? Sse2.UnpackLow(a, b) : AdvSimd.Arm64.ZipLow(a, b);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector128<byte> ZipHigh(Vector128<byte> a, Vector128<byte> b) => Sse2.IsSupported ? Sse2.UnpackHigh(a, b) : AdvSimd.Arm64.ZipHigh(a, b);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector128<ushort> ZipLow(Vector128<ushort> a, Vector128<ushort> b) => Sse2.IsSupported ? Sse2.UnpackLow(a, b) : AdvSimd.Arm64.ZipLow(a, b);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector128<ushort> ZipHigh(Vector128<ushort> a, Vector128<ushort> b) => Sse2.IsSupported ? Sse2.UnpackHigh(a, b) : AdvSimd.Arm64.ZipHigh(a, b);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector128<uint> ZipLow(Vector128<uint> a, Vector128<uint> b) => Sse2.IsSupported ? Sse2.UnpackLow(a, b) : AdvSimd.Arm64.ZipLow(a, b);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector128<uint> ZipHigh(Vector128<uint> a, Vector128<uint> b) => Sse2.IsSupported ? Sse2.UnpackHigh(a, b) : AdvSimd.Arm64.ZipHigh(a, b);

    /// <summary>The samples of the row in the order of the machine (swapped in place when needed).</summary>
    private static Span<T> Native<T>(Span<byte> row, bool swap) where T : unmanaged
    {
        if (swap)
        {
            Swap(row, Unsafe.SizeOf<T>());
        }

        return MemoryMarshal.Cast<byte, T>(row);
    }

    private static void Accumulate<T>(Span<T> values, int stride) where T : unmanaged, IBinaryInteger<T>
    {
        if (PrefixSum.Vectorised<T>(stride))
        {
            PrefixSum.Run(values, stride);
            return;
        }

        ref T v = ref MemoryMarshal.GetReference(values);
        int n = values.Length;
        if (stride == 1)
        {
            // The usual case (grey, or a plane): the running sum stays in a register.
            if (n == 0)
            {
                return;
            }

            T sum = v;
            for (int i = 1; i < n; i++)
            {
                sum += Unsafe.Add(ref v, i);
                Unsafe.Add(ref v, i) = sum;
            }

            return;
        }

        for (int i = stride; i < n; i++)
        {
            Unsafe.Add(ref v, i) += Unsafe.Add(ref v, i - stride);
        }
    }

    private static void Swap(Span<byte> bytes, int size)
    {
        switch (size)
        {
            case 2:
                BinaryPrimitives.ReverseEndianness(MemoryMarshal.Cast<byte, ushort>(bytes), MemoryMarshal.Cast<byte, ushort>(bytes));
                break;
            case 4:
                BinaryPrimitives.ReverseEndianness(MemoryMarshal.Cast<byte, uint>(bytes), MemoryMarshal.Cast<byte, uint>(bytes));
                break;
            case 8:
                BinaryPrimitives.ReverseEndianness(MemoryMarshal.Cast<byte, ulong>(bytes), MemoryMarshal.Cast<byte, ulong>(bytes));
                break;
        }
    }
}
