namespace Eleris.TiffNet.Internal.Jpeg;

/// <summary>
/// A Huffman table of ITU-T T.81 (annex C), validated when it is built and decoded through a lookup table of the first
/// <see cref="LookupBits"/> bits of a code, with a bounded slow path for the longer codes (at most 16 bits, F.2.2.3).
/// </summary>
/// <remarks>
/// A table is refused when it declares more than 256 symbols, when its codes do not fit their lengths (an over-subscribed tree),
/// or, for a DC table, when a symbol is above 15 (the size of a DC difference): libjpeg refuses the same tables.
/// </remarks>
internal sealed class HuffmanTable
{
    /// <summary>The bits of a code looked up at once; longer codes take the slow path.</summary>
    public const int LookupBits = 9;

    /// <summary>For the next <see cref="LookupBits"/> bits: the length of their code (high byte) and its symbol, or 0.</summary>
    private readonly ushort[] _lookup = new ushort[1 << LookupBits];

    /// <summary>The largest code of each length (index 1 to 16), or -1 when there is none.</summary>
    private readonly int[] _maxCode = new int[17];

    /// <summary>What to add to a code of each length to get the index of its symbol in <see cref="_values"/>.</summary>
    private readonly int[] _offset = new int[17];

    private readonly byte[] _values;

    private HuffmanTable(byte[] values) => _values = values;

    /// <summary>Builds a table from the counts of codes of each length (16 bytes) and the symbols, in the order of DHT.</summary>
    /// <exception cref="InvalidDataException">The table is not valid.</exception>
    public static HuffmanTable Create(ReadOnlySpan<byte> counts, ReadOnlySpan<byte> symbols, bool dc)
    {
        int total = 0;
        foreach (byte count in counts)
        {
            total += count;
        }

        if (total > 256 || total > symbols.Length)
        {
            throw new InvalidDataException($"a Huffman table declares {total} symbols; at most 256 are allowed.");
        }

        var table = new HuffmanTable(symbols[..total].ToArray());
        if (dc)
        {
            foreach (byte symbol in table._values)
            {
                if (symbol > 15)
                {
                    throw new InvalidDataException($"a DC Huffman table holds the symbol {symbol}; a DC difference has at most 15 bits.");
                }
            }
        }

        // Canonical codes (C.2): consecutive within a length, doubled from one length to the next.
        int code = 0, index = 0;
        for (int length = 1; length <= 16; length++)
        {
            int count = counts[length - 1];
            table._offset[length] = index - code;
            table._maxCode[length] = count == 0 ? -1 : code + count - 1;
            for (int k = 0; k < count; k++, code++, index++)
            {
                if (code >= 1 << length)
                {
                    throw new InvalidDataException("a Huffman table has more codes of a length than the length allows.");
                }

                if (length <= LookupBits)
                {
                    int shift = LookupBits - length;
                    ushort entry = (ushort)(length << 8 | table._values[index]);
                    table._lookup.AsSpan(code << shift, 1 << shift).Fill(entry);
                }
            }

            // The next code must still fit the length: a full length (all its codes used, the last of all ones) is refused too,
            // as libjpeg does.
            if (code >= 1 << length)
            {
                throw new InvalidDataException("a Huffman table has more codes of a length than the length allows.");
            }

            code <<= 1;
        }

        return table;
    }

    /// <summary>Decodes the symbol of the code at the head of 16 bits (most significant first); gives its length.</summary>
    /// <returns>The symbol, or 0 for a code that is not in the table (as libjpeg does, with a length of 16).</returns>
    public int Decode(int next16, out int length)
    {
        int entry = _lookup[next16 >> (16 - LookupBits)];
        if (entry != 0)
        {
            length = entry >> 8;
            return entry & 0xFF;
        }

        for (length = LookupBits + 1; length <= 16; length++)
        {
            int code = next16 >> (16 - length);
            if (code <= _maxCode[length])
            {
                return _values[code + _offset[length]];
            }
        }

        length = 16;
        return 0;
    }

    /// <summary>The tables of annex K.3, used when a stream has none (as libjpeg-turbo does, for motion JPEG).</summary>
    public static HuffmanTable Standard(bool dc, bool luminance) => (dc, luminance) switch
    {
        (true, true) => Create([0, 1, 5, 1, 1, 1, 1, 1, 1, 0, 0, 0, 0, 0, 0, 0], [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11], dc: true),
        (true, false) => Create([0, 3, 1, 1, 1, 1, 1, 1, 1, 1, 1, 0, 0, 0, 0, 0], [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11], dc: true),
        (false, true) => Create([0, 2, 1, 3, 3, 2, 4, 3, 5, 5, 4, 4, 0, 0, 1, 0x7D], StandardAcLuminance, dc: false),
        _ => Create([0, 2, 1, 2, 4, 4, 3, 4, 7, 5, 4, 4, 0, 1, 2, 0x77], StandardAcChrominance, dc: false),
    };

    private static ReadOnlySpan<byte> StandardAcLuminance =>
    [
        0x01, 0x02, 0x03, 0x00, 0x04, 0x11, 0x05, 0x12, 0x21, 0x31, 0x41, 0x06, 0x13, 0x51, 0x61, 0x07, 0x22, 0x71, 0x14, 0x32, 0x81,
        0x91, 0xA1, 0x08, 0x23, 0x42, 0xB1, 0xC1, 0x15, 0x52, 0xD1, 0xF0, 0x24, 0x33, 0x62, 0x72, 0x82, 0x09, 0x0A, 0x16, 0x17, 0x18,
        0x19, 0x1A, 0x25, 0x26, 0x27, 0x28, 0x29, 0x2A, 0x34, 0x35, 0x36, 0x37, 0x38, 0x39, 0x3A, 0x43, 0x44, 0x45, 0x46, 0x47, 0x48,
        0x49, 0x4A, 0x53, 0x54, 0x55, 0x56, 0x57, 0x58, 0x59, 0x5A, 0x63, 0x64, 0x65, 0x66, 0x67, 0x68, 0x69, 0x6A, 0x73, 0x74, 0x75,
        0x76, 0x77, 0x78, 0x79, 0x7A, 0x83, 0x84, 0x85, 0x86, 0x87, 0x88, 0x89, 0x8A, 0x92, 0x93, 0x94, 0x95, 0x96, 0x97, 0x98, 0x99,
        0x9A, 0xA2, 0xA3, 0xA4, 0xA5, 0xA6, 0xA7, 0xA8, 0xA9, 0xAA, 0xB2, 0xB3, 0xB4, 0xB5, 0xB6, 0xB7, 0xB8, 0xB9, 0xBA, 0xC2, 0xC3,
        0xC4, 0xC5, 0xC6, 0xC7, 0xC8, 0xC9, 0xCA, 0xD2, 0xD3, 0xD4, 0xD5, 0xD6, 0xD7, 0xD8, 0xD9, 0xDA, 0xE1, 0xE2, 0xE3, 0xE4, 0xE5,
        0xE6, 0xE7, 0xE8, 0xE9, 0xEA, 0xF1, 0xF2, 0xF3, 0xF4, 0xF5, 0xF6, 0xF7, 0xF8, 0xF9, 0xFA,
    ];

    private static ReadOnlySpan<byte> StandardAcChrominance =>
    [
        0x00, 0x01, 0x02, 0x03, 0x11, 0x04, 0x05, 0x21, 0x31, 0x06, 0x12, 0x41, 0x51, 0x07, 0x61, 0x71, 0x13, 0x22, 0x32, 0x81, 0x08,
        0x14, 0x42, 0x91, 0xA1, 0xB1, 0xC1, 0x09, 0x23, 0x33, 0x52, 0xF0, 0x15, 0x62, 0x72, 0xD1, 0x0A, 0x16, 0x24, 0x34, 0xE1, 0x25,
        0xF1, 0x17, 0x18, 0x19, 0x1A, 0x26, 0x27, 0x28, 0x29, 0x2A, 0x35, 0x36, 0x37, 0x38, 0x39, 0x3A, 0x43, 0x44, 0x45, 0x46, 0x47,
        0x48, 0x49, 0x4A, 0x53, 0x54, 0x55, 0x56, 0x57, 0x58, 0x59, 0x5A, 0x63, 0x64, 0x65, 0x66, 0x67, 0x68, 0x69, 0x6A, 0x73, 0x74,
        0x75, 0x76, 0x77, 0x78, 0x79, 0x7A, 0x82, 0x83, 0x84, 0x85, 0x86, 0x87, 0x88, 0x89, 0x8A, 0x92, 0x93, 0x94, 0x95, 0x96, 0x97,
        0x98, 0x99, 0x9A, 0xA2, 0xA3, 0xA4, 0xA5, 0xA6, 0xA7, 0xA8, 0xA9, 0xAA, 0xB2, 0xB3, 0xB4, 0xB5, 0xB6, 0xB7, 0xB8, 0xB9, 0xBA,
        0xC2, 0xC3, 0xC4, 0xC5, 0xC6, 0xC7, 0xC8, 0xC9, 0xCA, 0xD2, 0xD3, 0xD4, 0xD5, 0xD6, 0xD7, 0xD8, 0xD9, 0xDA, 0xE2, 0xE3, 0xE4,
        0xE5, 0xE6, 0xE7, 0xE8, 0xE9, 0xEA, 0xF2, 0xF3, 0xF4, 0xF5, 0xF6, 0xF7, 0xF8, 0xF9, 0xFA,
    ];
}
