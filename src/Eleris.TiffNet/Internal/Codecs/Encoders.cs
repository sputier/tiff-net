using System.IO.Compression;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace Eleris.TiffNet.Internal.Codecs;

/// <summary>The PackBits encoder of TIFF 6.0 (section 9): each row on its own, runs of 3 or more as a repeated byte.</summary>
internal static class PackBitsEncoder
{
    /// <summary>The most bytes a row of <paramref name="length"/> bytes encodes to: one header per 128 literal bytes.</summary>
    public static long MaxEncoded(long length) => length + (length + 127) / 128;

    /// <summary>Encodes one row; returns the bytes written into <paramref name="destination"/> (at least <see cref="MaxEncoded"/>).</summary>
    public static int EncodeRow(ReadOnlySpan<byte> row, Span<byte> destination)
    {
        int i = 0, o = 0, n = row.Length;
        while (i < n)
        {
            // The run: the bytes equal to the first, up to 128 (searched with vectors).
            byte value = row[i];
            var reach = row.Slice(i, Math.Min(128, n - i));
            int run = reach.IndexOfAnyExcept(value);
            if (run < 0)
            {
                run = reach.Length;
            }

            if (run >= 3)
            {
                destination[o++] = (byte)(1 - run);
                destination[o++] = value;
                i += run;
                continue;
            }

            // Literal bytes until a run of 3 begins (or 128 bytes): 16 places at a time while no run begins in them, then the
            // loop from the first place that may begin one.
            int j = SkipLiterals(row, i, Math.Min(i + 128, n - 2));
            while (j < n && j - i < 128 && !(j + 2 < n && row[j] == row[j + 1] && row[j] == row[j + 2]))
            {
                j++;
            }

            destination[o++] = (byte)(j - i - 1);
            row[i..j].CopyTo(destination[o..]);
            o += j - i;
            i = j;
        }

        return o;
    }

    /// <summary>
    /// The first place from <paramref name="start"/> where three equal bytes may begin, looked for 16 places at a time while the
    /// 18 bytes they need are below <paramref name="limit"/> + 2: a place before it begins none.
    /// </summary>
    private static int SkipLiterals(ReadOnlySpan<byte> row, int start, int limit)
    {
        int j = start;
        if (!Vector128.IsHardwareAccelerated)
        {
            return j;
        }

        for (; j + 16 <= limit; j += 16)
        {
            var a = Vector128.Create<byte>(row[j..]);
            var b = Vector128.Create<byte>(row[(j + 1)..]);
            var c = Vector128.Create<byte>(row[(j + 2)..]);
            uint begins = (Vector128.Equals(a, b) & Vector128.Equals(a, c)).ExtractMostSignificantBits();
            if (begins != 0)
            {
                return j + BitOperations.TrailingZeroCount(begins);
            }
        }

        return j;
    }
}

/// <summary>
/// The LZW encoder of TIFF 6.0 (section 13), as libtiff writes it: codes of 9 to 12 bits, most significant bit first, the width
/// growing one code early for the decoder, a clear code when the table is full; a hash table finds the strings.
/// </summary>
internal static class LzwEncoder
{
    private const int Clear = 256, End = 257, FirstFree = 258, Full = 4094, HashBits = 13, HashSize = 1 << HashBits;

    /// <summary>The most bytes <paramref name="length"/> bytes encode to (12 bits a byte at worst, the clear codes and the end).</summary>
    public static long MaxEncoded(long length) => length * 3 / 2 + length / 2000 * 2 + 16;

    /// <summary>Encodes a strip or tile; returns the bytes written into <paramref name="destination"/> (at least <see cref="MaxEncoded"/>).</summary>
    /// <exception cref="ArgumentException"><paramref name="destination"/> is shorter than <see cref="MaxEncoded"/>.</exception>
    [SkipLocalsInit]
    public static int Encode(ReadOnlySpan<byte> source, Span<byte> destination)
    {
        // The bound is checked once: every code then goes out through a reference, without a check per byte.
        if (destination.Length < MaxEncoded(source.Length))
        {
            throw new ArgumentException($"{source.Length} bytes need {MaxEncoded(source.Length)} bytes of output.", nameof(destination));
        }

        // Open addressing over one table: an entry is its key (prefix code << 8 | byte, plus 1 so that 0 is empty) in the high
        // 20 bits and its code in the low 12 bits; 32 KB, one cache line a probe.
        Span<uint> table = stackalloc uint[HashSize];
        table.Clear();
        ref byte output = ref MemoryMarshal.GetReference(destination);

        ulong bits = 0;
        int count = 0, written = 0, width = 9, free = FirstFree;
        Put(Clear, width, ref bits, ref count, ref output, ref written);
        if (source.IsEmpty)
        {
            Put(End, width, ref bits, ref count, ref output, ref written);
            Flush(ref bits, ref count, ref written, destination);
            return written;
        }

        int prefix = source[0];
        for (int i = 1; i < source.Length; i++)
        {
            byte value = source[i];
            uint key = (uint)(prefix << 8 | value) + 1;
            int slot = (int)((key * 2654435761u) >> (32 - HashBits));
            uint entry;
            while ((entry = table[slot]) != 0 && entry >> 12 != key)
            {
                slot = (slot + 1) & (HashSize - 1);
            }

            if (entry != 0)
            {
                prefix = (int)(entry & 0xFFF);
                continue;
            }

            Put(prefix, width, ref bits, ref count, ref output, ref written);
            table[slot] = key << 12 | (uint)free++;
            prefix = value;
            if (free == Full)
            {
                // The table is full: a clear code, written at the width reached, then 9 bits again.
                Put(Clear, width, ref bits, ref count, ref output, ref written);
                table.Clear();
                free = FirstFree;
                width = 9;
            }
            else if (free > (1 << width) - 1 && width < 12)
            {
                width++;
            }
        }

        // The last string, then as libtiff's LZWPostEncode: the decoder adds an entry for it before the end code, so the table
        // may be full (a clear code, back to 9 bits) or the width may grow first.
        Put(prefix, width, ref bits, ref count, ref output, ref written);
        if (++free == Full)
        {
            Put(Clear, width, ref bits, ref count, ref output, ref written);
            width = 9;
        }
        else if (free > (1 << width) - 1 && width < 12)
        {
            width++;
        }

        Put(End, width, ref bits, ref count, ref output, ref written);
        Flush(ref bits, ref count, ref written, destination);
        return written;
    }

    /// <summary>A code of <paramref name="width"/> bits, most significant first; whole bytes go out.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Put(int code, int width, ref ulong bits, ref int count, ref byte output, ref int written)
    {
        bits = (bits << width) | (uint)code;
        count += width;
        while (count >= 8)
        {
            count -= 8;
            Unsafe.Add(ref output, written++) = (byte)(bits >> count);
        }
    }

    private static void Flush(ref ulong bits, ref int count, ref int written, Span<byte> output)
    {
        if (count > 0)
        {
            output[written++] = (byte)(bits << (8 - count));
            count = 0;
        }
    }
}

/// <summary>
/// The Deflate encoder: a zlib stream written by System.IO.Compression, at the level the options ask for. At the level
/// <see cref="TiffCompressionLevel.Fastest"/>, data through the floating-point predictor is compressed with the run-length
/// strategy of zlib (matches of one byte back only): faster than level 1 and smaller on noisy data, whose byte planes of
/// mantissa hold no longer matches (benchmarks/results, issue #108). The stream is plain Deflate either way.
/// </summary>
internal static class DeflateEncoder
{
    /// <summary>
    /// The most bytes <paramref name="length"/> bytes compress to, at any level: zlib-ng's fastest level expands incompressible
    /// data by about 5.5 %, stored blocks by 5 bytes in 65 535; an eighth and a header leave room for both.
    /// </summary>
    public static long MaxEncoded(long length) => length + length / 8 + 1024;

    /// <summary>Compresses a strip or tile into <paramref name="destination"/> (at least <see cref="MaxEncoded"/>); returns its length.</summary>
    public static int Encode(ReadOnlySpan<byte> source, byte[] destination, TiffCompressionLevel level, TiffPredictor predictor = TiffPredictor.None)
    {
        using var output = new MemoryStream(destination, 0, destination.Length, writable: true);
        using (var zlib = new ZLibStream(output, Options(level, predictor), leaveOpen: true))
        {
            zlib.Write(source);
        }

        return (int)output.Position;
    }

    /// <summary>Compresses into a new array (the tests).</summary>
    public static byte[] Encode(ReadOnlySpan<byte> source, TiffCompressionLevel level, TiffPredictor predictor = TiffPredictor.None)
    {
        byte[] destination = new byte[MaxEncoded(source.Length)];
        return destination[..Encode(source, destination, level, predictor)];
    }

    /// <summary>
    /// zlib levels 1 (fastest), 6 (the default of zlib and libtiff) and 9 (smallest); the run-length strategy for the floating-point
    /// predictor at level 1.
    /// </summary>
    private static ZLibCompressionOptions Options(TiffCompressionLevel level, TiffPredictor predictor) => new()
    {
        CompressionLevel = level switch
        {
            TiffCompressionLevel.Fastest => 1,
            TiffCompressionLevel.SmallestSize => 9,
            _ => 6,
        },
        CompressionStrategy = level == TiffCompressionLevel.Fastest && predictor == TiffPredictor.FloatingPoint
            ? ZLibCompressionStrategy.RunLengthEncoding
            : ZLibCompressionStrategy.Default,
    };
}
