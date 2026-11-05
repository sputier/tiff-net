using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Eleris.TiffNet.Internal.Codecs;

/// <summary>
/// The LZW decoder of TIFF 6.0 (section 13): codes of 9 to 12 bits, most significant bit first, the width growing one code
/// early; and the old-style LZW of libtiff before 1990 (least significant bit first, the width growing on time), told apart by
/// its first bytes as libtiff does.
/// </summary>
/// <remarks>
/// The table refers back into the output: an entry is the position and length of a string already written, so that a code is
/// decoded by one copy, without a chain to follow or a stack to reverse. The state lives in locals. The output is bounded by the
/// destination: a stream that would decode to more stops there (<c>overflow</c>), and one that decodes to less returns fewer bytes.
/// </remarks>
internal static class Lzw
{
    private const int Clear = 256, End = 257, FirstFree = 258, MaxCodes = 4096;

    /// <summary>Decodes <paramref name="source"/> into <paramref name="destination"/>; returns the number of bytes written.</summary>
    /// <param name="source">The compressed bytes of a strip or tile.</param>
    /// <param name="destination">The decoded bytes wanted: the decoding stops when it is full.</param>
    /// <param name="overflow">Whether the data goes on beyond the destination (more codes before the end code).</param>
    /// <exception cref="InvalidDataException">A code is not in the table: the data is damaged.</exception>
    public static int Decode(ReadOnlySpan<byte> source, Span<byte> destination, out bool overflow)
    {
        // Old-style data starts with a clear code written least significant bit first: 0x00, then a byte whose low bit is set.
        bool old = source.Length >= 2 && source[0] == 0 && (source[1] & 1) != 0;
        return old ? Decode<LsbFirst>(source, destination, out overflow) : Decode<MsbFirst>(source, destination, out overflow);
    }

    private interface IBitOrder
    {
        /// <summary>1 when the width grows one code early (TIFF 6.0), 0 when it grows when the table needs it (old style).</summary>
        static abstract int EarlyChange { get; }

        /// <summary>Adds the byte to the bits waiting, which hold <paramref name="count"/> bits.</summary>
        static abstract ulong Append(ulong bits, int count, byte value);

        /// <summary>The next code of <paramref name="width"/> bits.</summary>
        static abstract int Peek(ulong bits, int count, int width);

        /// <summary>The bits waiting once a code of <paramref name="width"/> bits is taken.</summary>
        static abstract ulong Consume(ulong bits, int width);
    }

    /// <summary>TIFF 6.0: the bits fill a 64-bit word from the top.</summary>
    private readonly struct MsbFirst : IBitOrder
    {
        public static int EarlyChange => 1;

        public static ulong Append(ulong bits, int count, byte value) => bits | ((ulong)value << (56 - count));

        public static int Peek(ulong bits, int count, int width) => (int)(bits >> (64 - width));

        public static ulong Consume(ulong bits, int width) => bits << width;
    }

    /// <summary>Old style: the bits fill a 64-bit word from the bottom.</summary>
    private readonly struct LsbFirst : IBitOrder
    {
        public static int EarlyChange => 0;

        public static ulong Append(ulong bits, int count, byte value) => bits | ((ulong)value << count);

        public static int Peek(ulong bits, int count, int width) => (int)(bits & ((1UL << width) - 1));

        public static ulong Consume(ulong bits, int width) => bits >> width;
    }

    [SkipLocalsInit]
    private static int Decode<TOrder>(ReadOnlySpan<byte> source, Span<byte> destination, out bool overflow) where TOrder : IBitOrder
    {
        // Entry k (k >= 258) is the string of length[k] bytes written at start[k]; the strings of 0 to 255 are their byte.
        Span<int> start = stackalloc int[MaxCodes];
        Span<short> length = stackalloc short[MaxCodes];
        ref byte output = ref MemoryMarshal.GetReference(destination);
        int capacity = destination.Length;

        ulong bits = 0;
        int count = 0, position = 0, written = 0, width = 9, free = FirstFree, previous = -1;
        overflow = false;
        while (true)
        {
            while (count <= 56 && position < source.Length)
            {
                bits = TOrder.Append(bits, count, source[position++]);
                count += 8;
            }

            if (count < width)
            {
                return written;   // the data ends without an end code, as some writers do
            }

            int code = TOrder.Peek(bits, count, width);
            bits = TOrder.Consume(bits, width);
            count -= width;

            if (code == Clear)
            {
                free = FirstFree;
                width = 9;
                previous = -1;
                continue;
            }

            if (code == End)
            {
                return written;
            }

            if (written == capacity)
            {
                overflow = true;
                return written;
            }

            if (previous == -1)
            {
                // After a clear code: a byte.
                if (code > 255)
                {
                    throw new InvalidDataException($"LZW code {code} follows a clear code; a byte (0 to 255) is expected.");
                }

                Unsafe.Add(ref output, written++) = (byte)code;
                previous = code;
                continue;
            }

            // The previous string is the last thing written.
            int previousLength = previous < 256 ? 1 : length[previous];
            int previousStart = written - previousLength;
            int from, size;
            if (code < 256)
            {
                from = -1;
                size = 1;
            }
            else if (code < free)
            {
                from = start[code];
                size = length[code];
            }
            else if (code == free)
            {
                // The string being defined: the previous one and its own first byte.
                from = previousStart;
                size = previousLength + 1;
            }
            else
            {
                throw new InvalidDataException($"LZW code {code} is not defined yet (the next code is {free}).");
            }

            // The new entry: the previous string and the first byte of this one, which starts where the previous one was written.
            if (free < MaxCodes)
            {
                start[free] = previousStart;
                length[free] = (short)(previousLength + 1);
            }

            if (code < 256)
            {
                Unsafe.Add(ref output, written++) = (byte)code;
            }
            else
            {
                if (size > capacity - written)
                {
                    size = capacity - written;
                    overflow = true;
                }

                if (from + size <= written && size > 16)
                {
                    destination.Slice(from, size).CopyTo(destination.Slice(written));
                }
                else
                {
                    // Short or overlapping (a string repeating its own start): byte by byte, forwards.
                    for (int k = 0; k < size; k++)
                    {
                        Unsafe.Add(ref output, written + k) = Unsafe.Add(ref output, from + k);
                    }
                }

                written += size;
                if (overflow)
                {
                    return written;
                }
            }

            if (++free >= (1 << width) - TOrder.EarlyChange && width < 12)
            {
                width++;
            }

            previous = code;
        }
    }
}
