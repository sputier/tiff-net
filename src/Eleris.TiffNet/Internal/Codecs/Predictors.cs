using System.Buffers.Binary;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Eleris.TiffNet.Internal.Codecs;

/// <summary>
/// Undoes the predictors of TIFF on a decoded row, in place: the horizontal differencing of TIFF 6.0 (section 14) on integer
/// samples of 8 to 64 bits, and the floating point predictor of Adobe's Technical Note 3 on samples of 16 to 64 bits. The row
/// stays in the byte order of the file, as the unpacking of the samples expects it.
/// </summary>
/// <remarks>
/// Each row is undone where it lies, through typed spans, without allocating: the floating point predictor uses a row of scratch
/// given by the caller. Accumulating is a chain of dependent additions, about one sample per cycle: far faster than the
/// decompression that precedes it, which it never limits.
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
        Accumulate(row, stride);
        row.CopyTo(scratch);
        int count = row.Length / bytesPerSample;
        ref byte planes = ref MemoryMarshal.GetReference(scratch);
        ref byte output = ref MemoryMarshal.GetReference(row);
        switch (bytesPerSample)
        {
            case 2:
                for (int i = 0; i < count; i++)
                {
                    ushort v = (ushort)(Unsafe.Add(ref planes, i) << 8 | Unsafe.Add(ref planes, count + i));
                    Unsafe.WriteUnaligned(ref Unsafe.Add(ref output, i * 2), littleEndian ? v : BinaryPrimitives.ReverseEndianness(v));
                }

                break;
            case 4:
                for (int i = 0; i < count; i++)
                {
                    uint v = (uint)Unsafe.Add(ref planes, i) << 24 | (uint)Unsafe.Add(ref planes, count + i) << 16
                        | (uint)Unsafe.Add(ref planes, 2 * count + i) << 8 | Unsafe.Add(ref planes, 3 * count + i);
                    Unsafe.WriteUnaligned(ref Unsafe.Add(ref output, i * 4), littleEndian ? v : BinaryPrimitives.ReverseEndianness(v));
                }

                break;
            default:
                for (int i = 0; i < count; i++)
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
