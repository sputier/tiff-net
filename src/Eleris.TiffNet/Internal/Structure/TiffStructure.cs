using System.Buffers.Binary;

namespace Eleris.TiffNet.Internal.Structure;

/// <summary>The header of a TIFF file: byte order, classic or BigTIFF, and where the first directory is.</summary>
internal readonly record struct TiffHeader(bool LittleEndian, bool BigTiff, long FirstDirectory)
{
    /// <summary>Reads and checks the header.</summary>
    /// <exception cref="TiffException">The data is not TIFF.</exception>
    public static TiffHeader Read(TiffSource source)
    {
        if (source.Length < 8)
        {
            throw new TiffException($"{source.Name}: not a TIFF file ({source.Length} bytes, a header has at least 8).");
        }

        Span<byte> header = stackalloc byte[16];
        source.ReadExactly(0, header[..8]);
        bool little = (header[0], header[1]) switch
        {
            ((byte)'I', (byte)'I') => true,
            ((byte)'M', (byte)'M') => false,
            _ => throw new TiffException($"{source.Name}: not a TIFF file (no byte order mark 'II' or 'MM')."),
        };

        var order = new ByteOrder(little);
        ushort version = order.UInt16(header[2..]);
        if (version == 42)
        {
            return new TiffHeader(little, BigTiff: false, order.UInt32(header[4..]));
        }

        if (version != 43)
        {
            throw new TiffException($"{source.Name}: not a TIFF file (version {version}; 42 for TIFF, 43 for BigTIFF).");
        }

        if (source.Length < 16)
        {
            throw new TiffException($"{source.Name}: the BigTIFF header is truncated.");
        }

        source.ReadExactly(8, header[8..16]);
        if (order.UInt16(header[4..]) != 8 || order.UInt16(header[6..]) != 0)
        {
            throw new TiffException($"{source.Name}: the BigTIFF header declares an offset size other than 8.");
        }

        ulong first = order.UInt64(header[8..]);
        return new TiffHeader(little, BigTiff: true, first > long.MaxValue ? -1 : (long)first);
    }
}

/// <summary>Reads integers in the byte order of the file.</summary>
internal readonly record struct ByteOrder(bool Little)
{
    public ushort UInt16(ReadOnlySpan<byte> b) => Little ? BinaryPrimitives.ReadUInt16LittleEndian(b) : BinaryPrimitives.ReadUInt16BigEndian(b);

    public uint UInt32(ReadOnlySpan<byte> b) => Little ? BinaryPrimitives.ReadUInt32LittleEndian(b) : BinaryPrimitives.ReadUInt32BigEndian(b);

    public ulong UInt64(ReadOnlySpan<byte> b) => Little ? BinaryPrimitives.ReadUInt64LittleEndian(b) : BinaryPrimitives.ReadUInt64BigEndian(b);
}

/// <summary>
/// One entry of a directory: the tag, the type and count of its values, and where its values are in the file. Values short
/// enough to fit in the entry are in the entry itself, so every value is read the same way: at <see cref="DataOffset"/>.
/// </summary>
/// <param name="Code">The tag.</param>
/// <param name="Type">The type of the values.</param>
/// <param name="Count">The number of values.</param>
/// <param name="DataOffset">Where the values are in the file (in the entry itself when they fit in it).</param>
/// <param name="Valid">Whether the values are within the file. An entry whose values are not is kept so that a structural tag
/// fails with a clear message; other tags are dropped.</param>
internal readonly record struct TagEntry(ushort Code, TiffTagType Type, long Count, long DataOffset, bool Valid)
{
    public long ByteLength => Count * TagTypes.Size(Type);
}

/// <summary>The sizes and kinds of the field types.</summary>
internal static class TagTypes
{
    /// <summary>The size of one value, or 0 for a type that is not defined by TIFF 6.0 or BigTIFF.</summary>
    public static int Size(TiffTagType type) => type switch
    {
        TiffTagType.Byte or TiffTagType.Ascii or TiffTagType.SByte or TiffTagType.Undefined => 1,
        TiffTagType.Short or TiffTagType.SShort => 2,
        TiffTagType.Long or TiffTagType.SLong or TiffTagType.Float or TiffTagType.Ifd => 4,
        TiffTagType.Rational or TiffTagType.SRational or TiffTagType.Double or TiffTagType.Long8 or TiffTagType.SLong8 or TiffTagType.Ifd8 => 8,
        _ => 0,
    };

    public static bool IsInteger(TiffTagType type) => type is TiffTagType.Byte or TiffTagType.Short or TiffTagType.Long
        or TiffTagType.SByte or TiffTagType.SShort or TiffTagType.SLong or TiffTagType.Ifd or TiffTagType.Long8 or TiffTagType.SLong8
        or TiffTagType.Ifd8;
}

/// <summary>A directory as stored: its entries by tag (the first of duplicates), and the offset of the next directory.</summary>
internal sealed class RawDirectory(long offset, Dictionary<ushort, TagEntry> entries, long next)
{
    public long Offset { get; } = offset;

    public IReadOnlyDictionary<ushort, TagEntry> Entries { get; } = entries;

    /// <summary>The next directory of the chain, or 0.</summary>
    public long Next { get; } = next;

    public bool TryGet(ushort code, out TagEntry entry) => Entries.TryGetValue(code, out entry);
}

/// <summary>
/// Reads the directories and the values of a file, within the limits of a <see cref="ResourceBudget"/>: every count and offset
/// is checked against the size of the file before anything is read or allocated, every directory is counted, loops between
/// directories are detected.
/// </summary>
internal sealed class StructureReader
{
    private readonly HashSet<long> _visited = [];

    public StructureReader(TiffSource source, ResourceBudget budget)
    {
        Source = source;
        Budget = budget;
        Header = TiffHeader.Read(source);
        Order = new ByteOrder(Header.LittleEndian);
    }

    public TiffSource Source { get; }

    public ResourceBudget Budget { get; }

    public TiffHeader Header { get; }

    public ByteOrder Order { get; }

    private bool Strict => Budget.Options.Strict;

    private int CountSize => Header.BigTiff ? 8 : 2;

    /// <summary>The memory an entry takes in the table of a directory (its key, its value, the links of the table).</summary>
    private const int EntryMemory = 48;

    private int EntrySize => Header.BigTiff ? 20 : 12;

    private int OffsetSize => Header.BigTiff ? 8 : 4;

    /// <summary>
    /// Reads the directory at <paramref name="offset"/>, or returns <see langword="null"/> when it was read already (a loop) or
    /// its offset is outside the file, which ends a chain (both are a <see cref="TiffException"/> with <c>Strict</c>).
    /// </summary>
    public RawDirectory? ReadDirectory(long offset)
    {
        if (offset < (Header.BigTiff ? 16 : 8) || offset > Source.Length - CountSize)
        {
            return Strict ? throw Budget.Invalid($"a directory offset ({offset}) is outside the file.") : null;
        }

        if (!_visited.Add(offset))
        {
            return Strict ? throw Budget.Invalid($"the directories form a loop (offset {offset} is reached twice).") : null;
        }

        if (Strict && offset % 2 != 0)
        {
            throw Budget.Invalid($"the directory at offset {offset} does not start on a word boundary.");
        }

        Budget.CountDirectory();
        Budget.ThrowIfCancelled();

        Span<byte> countBytes = stackalloc byte[8];
        Source.ReadExactly(offset, countBytes[..CountSize]);
        ulong count = Header.BigTiff ? Order.UInt64(countBytes) : Order.UInt16(countBytes);
        Budget.CheckTagsPerDirectory(count > long.MaxValue ? long.MaxValue : (long)count);
        long entriesStart = offset + CountSize;
        long entriesLength = (long)count * EntrySize;
        if (entriesLength > Source.Length - entriesStart)
        {
            throw Budget.Invalid($"the directory at offset {offset} declares {count} entries, more than the file holds.");
        }

        // The bytes of the entries, and the table that holds them once read (an entry of the table takes about 48 bytes).
        Budget.ReserveMetadata(entriesLength + OffsetSize + (long)count * EntryMemory, $"the directory at offset {offset}");
        byte[] bytes = new byte[entriesLength + OffsetSize];
        long available = Math.Min(bytes.Length, Source.Length - entriesStart);
        Source.ReadExactly(entriesStart, bytes.AsSpan(0, (int)available));
        long next = 0;
        if (available == bytes.Length)
        {
            ReadOnlySpan<byte> field = bytes.AsSpan((int)entriesLength);
            ulong raw = Header.BigTiff ? Order.UInt64(field) : Order.UInt32(field);
            next = raw > long.MaxValue ? -1 : (long)raw;
        }
        else if (Strict)
        {
            throw Budget.Invalid($"the directory at offset {offset} has no offset of the next directory.");
        }

        var entries = new Dictionary<ushort, TagEntry>((int)Math.Min(count, 64));
        int previous = -1;
        for (int i = 0; i < (int)count; i++)
        {
            ReadOnlySpan<byte> e = bytes.AsSpan(i * EntrySize, EntrySize);
            ushort code = Order.UInt16(e);
            var type = (TiffTagType)Order.UInt16(e[2..]);
            ulong rawCount = Header.BigTiff ? Order.UInt64(e[4..]) : Order.UInt32(e[4..]);
            if (Strict && code <= previous)
            {
                throw Budget.Invalid(code == previous
                    ? $"tag {code} appears twice in the directory at offset {offset}."
                    : $"the entries of the directory at offset {offset} are not in ascending order (tag {code} after {previous}).");
            }

            previous = Math.Max(previous, code);
            int size = TagTypes.Size(type);
            if (size == 0 || rawCount == 0)
            {
                // A type outside the specification, or no value: nothing to read. Kept out of the directory.
                if (Strict)
                {
                    throw Budget.Invalid(size == 0 ? $"tag {code} has the unknown type {(int)type}." : $"tag {code} has no value.");
                }

                continue;
            }

            long valueField = entriesStart + (long)i * EntrySize + (Header.BigTiff ? 12 : 8);
            bool fits = rawCount <= (ulong)(Source.Length / size);
            long countValue = fits ? (long)rawCount : long.MaxValue / 8;
            long byteLength = countValue * size;
            long dataOffset;
            if (fits && byteLength <= OffsetSize)
            {
                dataOffset = valueField;
            }
            else
            {
                ReadOnlySpan<byte> field = e[(Header.BigTiff ? 12 : 8)..];
                ulong raw = Header.BigTiff ? Order.UInt64(field) : Order.UInt32(field);
                dataOffset = raw > long.MaxValue ? -1 : (long)raw;
                fits = fits && dataOffset >= 0 && dataOffset <= Source.Length && byteLength <= Source.Length - dataOffset;
            }

            // First of duplicates wins, as in libtiff.
            entries.TryAdd(code, new TagEntry(code, type, countValue, dataOffset, fits));
        }

        return new RawDirectory(offset, entries, next);
    }

    /// <summary>
    /// The raw bytes of the values of a tag, within <see cref="TiffReadOptions.MaxTagBytes"/>, counted in
    /// <see cref="TiffReadOptions.MaxMetadataBytes"/>.
    /// </summary>
    public byte[] ReadBytes(TagEntry entry)
    {
        Ensure(entry);
        Budget.ReserveMetadata(entry.ByteLength, $"tag {entry.Code}");
        byte[] bytes = new byte[entry.ByteLength];
        Source.ReadExactly(entry.DataOffset, bytes);
        return bytes;
    }

    /// <summary>The values of an integer tag as 64-bit integers (signed types sign-extended, unsigned ones as they are).</summary>
    /// <exception cref="TiffException">The tag is not of an integer type, or its values are not in the file.</exception>
    public long[] ReadIntegers(TagEntry entry)
    {
        if (!TagTypes.IsInteger(entry.Type))
        {
            throw Budget.Invalid($"tag {entry.Code} holds {entry.Type} values where integers are expected.");
        }

        byte[] bytes = ReadBytes(entry);
        Budget.ReserveMetadata(entry.Count * sizeof(long), $"the values of tag {entry.Code}");
        var values = new long[entry.Count];
        int size = TagTypes.Size(entry.Type);
        for (int i = 0; i < values.Length; i++)
        {
            ReadOnlySpan<byte> v = bytes.AsSpan(i * size, size);
            values[i] = entry.Type switch
            {
                TiffTagType.Byte => v[0],
                TiffTagType.SByte => (sbyte)v[0],
                TiffTagType.Short => Order.UInt16(v),
                TiffTagType.SShort => (short)Order.UInt16(v),
                TiffTagType.Long or TiffTagType.Ifd => Order.UInt32(v),
                TiffTagType.SLong => (int)Order.UInt32(v),
                TiffTagType.SLong8 => (long)Order.UInt64(v),
                _ => Order.UInt64(v) > long.MaxValue ? -1 : (long)Order.UInt64(v),   // Long8, Ifd8: a value beyond 2^63 is no size
            };
        }

        return values;
    }

    /// <summary>
    /// The first value of a tag the specification types SHORT (or LONG for <paramref name="max"/> beyond 65535), checked to be
    /// within 0 and <paramref name="max"/>: a larger value (a 64-bit BigTIFF value, for example) is a damaged file, never narrowed.
    /// </summary>
    public long ReadInteger(RawDirectory directory, ushort code, long fallback, long max)
    {
        long value = ReadInteger(directory, code, fallback);
        return value < 0 || value > max ? throw Budget.Invalid($"tag {code} has the value {value}, outside 0 to {max}.") : value;
    }

    /// <summary>The first value of an integer tag, or <paramref name="fallback"/> when the tag is absent.</summary>
    public long ReadInteger(RawDirectory directory, ushort code, long fallback)
    {
        if (!directory.TryGet(code, out var entry))
        {
            return fallback;
        }

        if (entry.Count < 1 || !TagTypes.IsInteger(entry.Type))
        {
            throw Budget.Invalid($"tag {code} holds no integer.");
        }

        return ReadIntegers(entry with { Count = 1 })[0];
    }

    /// <summary>Checks that the values of a tag can be read: within the file and within <see cref="TiffReadOptions.MaxTagBytes"/>.</summary>
    public void Ensure(TagEntry entry)
    {
        if (!entry.Valid)
        {
            throw Budget.Invalid($"the values of tag {entry.Code} ({entry.Count} x {entry.Type}) are not within the file.");
        }

        Budget.CheckTagBytes(entry.Code, entry.ByteLength);
    }
}
