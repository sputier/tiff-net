using System.Buffers.Binary;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Eleris.TiffNet.Internal.Structure;

namespace Eleris.TiffNet.Internal.Pixels;

/// <summary>
/// Turns one stored row of a strip or tile into delivered samples: byte order of the machine, low depths unpacked, MinIsWhite
/// inverted, palette indices expanded to RGB. The row holds all the samples of a pixel (interleaved) or one plane.
/// </summary>
internal static class SampleUnpacker
{
    /// <summary>The number of delivered samples per pixel of a stored row: of one plane, or of the whole pixel.</summary>
    public static int DeliveredChannels(ImageDescriptor d, int plane) => d.Planar
        ? (d.Photometric == TiffPhotometric.Palette && plane == 0 ? 3 : 1)
        : d.Channels;

    /// <summary>The number of stored samples per pixel of a stored row.</summary>
    public static int StoredSamples(ImageDescriptor d) => d.Planar ? 1 : d.SamplesPerPixel;

    /// <summary>
    /// Unpacks <paramref name="pixels"/> pixels of <paramref name="raw"/> (a stored row, in the byte order of the file) into
    /// <paramref name="destination"/>. <paramref name="raw"/> may be modified (bit reversal, byte swapping in place).
    /// </summary>
    public static void Unpack<TD>(ImageDescriptor d, int plane, Span<byte> raw, int pixels, Span<TD> destination, bool littleEndian)
        where TD : unmanaged, INumber<TD>
    {
        if (d.ReversedBits)
        {
            ReverseBits(raw);
        }

        int stored = StoredSamples(d);
        int count = pixels * stored;
        int bits = d.BitsPerSample;
        bool palette = d.Photometric == TiffPhotometric.Palette;

        if (!palette && bits == Unsafe.SizeOf<TD>() * 8)
        {
            // Whole bytes per sample, the delivered type: a copy, swapped in the same pass when the file is big endian.
            ReadOnlySpan<byte> values = raw[..(count * (bits / 8))];
            Span<byte> target = MemoryMarshal.AsBytes(destination[..count]);
            if (!littleEndian && bits > 8)
            {
                CopySwapped(values, target, bits / 8);
            }
            else
            {
                values.CopyTo(target);
            }
        }
        else if (!palette)
        {
            // 1 to 7 bits (delivered as bytes) or 9 to 15 bits (delivered as 16-bit words): a stream of bits, most significant first.
            UnpackBits(raw, count, bits, destination);
        }

        if (d.Photometric == TiffPhotometric.MinIsWhite && (!d.Planar || plane == 0))
        {
            // Only the grey samples: the extra samples (alpha) keep their meaning.
            Invert(d, destination[..count], stored);
        }

        if (palette)
        {
            Expand(d, plane, raw, pixels, destination, littleEndian);
        }
    }

    /// <summary>
    /// <paramref name="count"/> samples of <paramref name="bits"/> bits (1 to 7, 9 to 15), most significant bit first: each read
    /// through the 8 bytes that hold it, shifted, rather than bit by bit; the last ones, too close to the end of the row for 8
    /// bytes, bit by bit.
    /// </summary>
    private static void UnpackBits<TD>(ReadOnlySpan<byte> raw, int count, int bits, Span<TD> destination) where TD : unmanaged, INumber<TD>
    {
        _ = destination[count - 1];
        int shift = 64 - bits, i = 0;
        long bit = 0;
        for (; i < count && (bit >> 3) + 8 <= raw.Length; i++, bit += bits)
        {
            ulong window = BinaryPrimitives.ReadUInt64BigEndian(raw[(int)(bit >> 3)..]);
            destination[i] = TD.CreateTruncating((window << (int)(bit & 7)) >> shift);
        }

        for (; i < count; i++)
        {
            destination[i] = TD.CreateTruncating(Bits(raw, i, bits, littleEndian: false));
        }
    }

    /// <summary>Sample <paramref name="index"/> of <paramref name="bits"/> bits: whole bytes in the byte order of the file, otherwise MSB first.</summary>
    private static ulong Bits(ReadOnlySpan<byte> raw, int index, int bits, bool littleEndian)
    {
        if (bits == 8)
        {
            return raw[index];
        }

        if (bits == 16)
        {
            ReadOnlySpan<byte> two = raw.Slice(index * 2, 2);
            return littleEndian ? BinaryPrimitives.ReadUInt16LittleEndian(two) : BinaryPrimitives.ReadUInt16BigEndian(two);
        }

        long bit = (long)index * bits;
        ulong value = 0;
        for (int b = 0; b < bits; b++, bit++)
        {
            value = (value << 1) | (uint)((raw[(int)(bit >> 3)] >> (7 - (int)(bit & 7))) & 1);
        }

        return value;
    }

    /// <summary>Inverts every <paramref name="stride"/>-th value, starting with the first: the grey samples of the row.</summary>
    private static void Invert<TD>(ImageDescriptor d, Span<TD> values, int stride) where TD : unmanaged, INumber<TD>
    {
        if (d.SampleFormat is 2 or 3)
        {
            // Floating point, and signed grey with 0 as white (not defined well by TIFF): negated.
            for (int i = 0; i < values.Length; i += stride)
            {
                values[i] = -values[i];
            }

            return;
        }

        // max − value, max being the largest value of the stored depth (1 for a bilevel mask).
        TD max = TD.CreateTruncating(d.BitsPerSample >= 64 ? ulong.MaxValue : (1UL << d.BitsPerSample) - 1);
        for (int i = 0; i < values.Length; i += stride)
        {
            values[i] = max - values[i];
        }
    }

    /// <summary>Palette indices (and extra samples, delivered unchanged) to RGB from the colour map.</summary>
    private static void Expand<TD>(ImageDescriptor d, int plane, ReadOnlySpan<byte> raw, int pixels, Span<TD> destination, bool littleEndian)
        where TD : unmanaged, INumber<TD>
    {
        ushort[] map = d.ColorMap!;
        int entries = 1 << d.BitsPerSample;
        int stored = StoredSamples(d);
        int delivered = DeliveredChannels(d, plane);
        for (int p = 0; p < pixels; p++)
        {
            int to = p * delivered;
            if (!d.Planar || plane == 0)
            {
                int index = (int)Bits(raw, p * stored, d.BitsPerSample, littleEndian);
                destination[to] = TD.CreateTruncating(map[index]);
                destination[to + 1] = TD.CreateTruncating(map[entries + index]);
                destination[to + 2] = TD.CreateTruncating(map[2 * entries + index]);
                for (int e = 1; e < stored; e++)
                {
                    destination[to + 2 + e] = TD.CreateTruncating(Bits(raw, p * stored + e, d.BitsPerSample, littleEndian));
                }
            }
            else
            {
                destination[to] = TD.CreateTruncating(Bits(raw, p, d.BitsPerSample, littleEndian));
            }
        }
    }

    /// <summary>Copies samples of <paramref name="size"/> bytes, reversing the order of their bytes.</summary>
    private static void CopySwapped(ReadOnlySpan<byte> source, Span<byte> destination, int size)
    {
        switch (size)
        {
            case 2:
                BinaryPrimitives.ReverseEndianness(MemoryMarshal.Cast<byte, ushort>(source), MemoryMarshal.Cast<byte, ushort>(destination));
                break;
            case 4:
                BinaryPrimitives.ReverseEndianness(MemoryMarshal.Cast<byte, uint>(source), MemoryMarshal.Cast<byte, uint>(destination));
                break;
            default:
                BinaryPrimitives.ReverseEndianness(MemoryMarshal.Cast<byte, ulong>(source), MemoryMarshal.Cast<byte, ulong>(destination));
                break;
        }
    }

    /// <summary>Reverses the order of the bytes of samples of <paramref name="size"/> bytes, in place.</summary>
    internal static void Swap(Span<byte> bytes, int size)
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

    private static void ReverseBits(Span<byte> bytes)
    {
        for (int i = 0; i < bytes.Length; i++)
        {
            byte b = bytes[i];
            b = (byte)(((b & 0xF0) >> 4) | ((b & 0x0F) << 4));
            b = (byte)(((b & 0xCC) >> 2) | ((b & 0x33) << 2));
            bytes[i] = (byte)(((b & 0xAA) >> 1) | ((b & 0x55) << 1));
        }
    }
}
