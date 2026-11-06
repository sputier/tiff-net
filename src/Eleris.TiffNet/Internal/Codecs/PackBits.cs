using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace Eleris.TiffNet.Internal.Codecs;

/// <summary>The PackBits decoder of TIFF 6.0 (section 9): runs of a repeated byte and runs of literal bytes.</summary>
internal static class PackBits
{
    /// <summary>Decodes <paramref name="source"/> into <paramref name="destination"/>; returns the number of bytes written.</summary>
    /// <param name="source">The compressed bytes of a strip or tile.</param>
    /// <param name="destination">The decoded bytes wanted: the decoding stops when it is full.</param>
    /// <param name="overflow">Whether the data goes on beyond the destination.</param>
    /// <remarks>A run cut by the end of the data gives what there is, as libtiff does; the caller sees that bytes are missing.</remarks>
    public static int Decode(ReadOnlySpan<byte> source, Span<byte> destination, out bool overflow)
    {
        int position = 0, written = 0;
        overflow = false;
        while (position < source.Length)
        {
            if (written == destination.Length)
            {
                overflow = HasData(source[position..]);
                return written;
            }

            sbyte header = (sbyte)source[position++];
            if (header >= 0)
            {
                // header + 1 literal bytes.
                int size = Math.Min(Math.Min(header + 1, source.Length - position), destination.Length - written);
                overflow = size < header + 1 && position + size < source.Length;
                if (size <= 16 && source.Length - position >= 16 && destination.Length - written >= 16)
                {
                    // A short literal run: one 16-byte copy (the bytes beyond it, inside the destination, are written again later).
                    Unsafe.WriteUnaligned(ref Unsafe.Add(ref MemoryMarshal.GetReference(destination), written),
                        Unsafe.ReadUnaligned<Vector128<byte>>(ref Unsafe.Add(ref MemoryMarshal.GetReference(source), position)));
                }
                else
                {
                    source.Slice(position, size).CopyTo(destination[written..]);
                }

                position += size;
                written += size;
            }
            else if (header != -128)
            {
                // 1 − header copies of the next byte.
                if (position == source.Length)
                {
                    break;
                }

                int size = Math.Min(1 - header, destination.Length - written);
                overflow = size < 1 - header;
                if (size <= 16 && destination.Length - written >= 16)
                {
                    // A short repeat: one 16-byte store.
                    Unsafe.WriteUnaligned(ref Unsafe.Add(ref MemoryMarshal.GetReference(destination), written), Vector128.Create(source[position++]));
                }
                else
                {
                    destination.Slice(written, size).Fill(source[position++]);
                }

                written += size;
            }

            // -128 is a no-operation.
            if (overflow)
            {
                return written;
            }
        }

        return written;
    }

    /// <summary>Whether what is left holds data, not only no-operations.</summary>
    private static bool HasData(ReadOnlySpan<byte> rest)
    {
        foreach (byte b in rest)
        {
            if (b != 0x80)
            {
                return true;
            }
        }

        return false;
    }
}
