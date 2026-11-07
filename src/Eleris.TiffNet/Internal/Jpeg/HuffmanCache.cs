namespace Eleris.TiffNet.Internal.Jpeg;

/// <summary>
/// The Huffman tables of the blocks of one read, built once and shared by its decoders: without JPEGTables each strip or tile
/// carries its tables (12 KB each, their lookups filled again), the same ones in every block of a writer.
/// </summary>
/// <remarks>
/// A table is found by its bytes in DHT (class and identifier, counts, symbols), compared with those of the tables kept: finding
/// one allocates nothing. At most <see cref="Capacity"/> tables are kept: a file whose blocks define more distinct tables decodes
/// the others as before, one table per definition.
/// </remarks>
internal sealed class HuffmanCache
{
    /// <summary>The most tables kept for a read.</summary>
    public const int Capacity = 64;

    /// <summary>The tables kept, replaced whole when one is added (read without a lock).</summary>
    private volatile (byte[] Definition, HuffmanTable Table)[] _entries = [];
    private readonly Lock _lock = new();

    /// <summary>The table defined by <paramref name="definition"/> (a table of a DHT segment, its first byte included), built.</summary>
    /// <exception cref="InvalidDataException">The table is not valid (as when it is defined).</exception>
    public HuffmanTable? Find(ReadOnlySpan<byte> definition)
    {
        if (Lookup(_entries, definition) is { } found)
        {
            return found;
        }

        lock (_lock)
        {
            var entries = _entries;
            if (Lookup(entries, definition) is { } added)
            {
                return added;
            }

            if (entries.Length >= Capacity)
            {
                return null;
            }

            var table = HuffmanTable.Create(definition.Slice(1, 16), definition[17..], dc: definition[0] >> 4 == 0);
            _entries = [.. entries, (definition.ToArray(), table)];
            return table;
        }
    }

    private static HuffmanTable? Lookup((byte[] Definition, HuffmanTable Table)[] entries, ReadOnlySpan<byte> definition)
    {
        foreach (var (bytes, table) in entries)
        {
            if (definition.SequenceEqual(bytes))
            {
                return table;
            }
        }

        return null;
    }
}
