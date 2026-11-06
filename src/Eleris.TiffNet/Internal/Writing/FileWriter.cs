using System.Buffers.Binary;
using Eleris.TiffNet.Internal.Structure;

namespace Eleris.TiffNet.Internal.Writing;

/// <summary>An entry of a directory to write: a tag, its type, its count and its values (little endian).</summary>
internal readonly record struct DirectoryEntry(ushort Code, TiffTagType Type, long Count, byte[] Value)
{
    public static DirectoryEntry Short(ushort code, params ReadOnlySpan<int> values)
    {
        byte[] bytes = new byte[values.Length * 2];
        for (int i = 0; i < values.Length; i++)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(i * 2), checked((ushort)values[i]));
        }

        return new(code, TiffTagType.Short, values.Length, bytes);
    }

    public static DirectoryEntry Long(ushort code, params ReadOnlySpan<long> values)
    {
        byte[] bytes = new byte[values.Length * 4];
        for (int i = 0; i < values.Length; i++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(i * 4), checked((uint)values[i]));
        }

        return new(code, TiffTagType.Long, values.Length, bytes);
    }

    /// <summary>Offsets or byte counts: LONG in classic TIFF, LONG8 in BigTIFF.</summary>
    public static DirectoryEntry Offsets(ushort code, ReadOnlySpan<long> values, bool bigTiff)
    {
        if (!bigTiff)
        {
            return Long(code, values);
        }

        byte[] bytes = new byte[values.Length * 8];
        for (int i = 0; i < values.Length; i++)
        {
            BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(i * 8), (ulong)values[i]);
        }

        return new(code, TiffTagType.Long8, values.Length, bytes);
    }
}

/// <summary>
/// Writes a TIFF file, little endian, classic or BigTIFF: the header, then for each image its strips or tiles and its directory,
/// each directory linked from the previous one (or the header). Values that do not fit in an entry follow the directory, each on
/// an even offset (TIFF 6.0); the entries are sorted by tag.
/// </summary>
/// <remarks>The stream must seek: each link is written once the directory it points to is placed.</remarks>
internal sealed class FileWriter
{
    private readonly Stream _output;
    private readonly long _start;
    private long _link;

    public FileWriter(Stream output, bool bigTiff)
    {
        _output = output;
        BigTiff = bigTiff;
        _start = output.Position;
        Span<byte> header = stackalloc byte[bigTiff ? 16 : 8];
        header.Clear();
        header[0] = header[1] = (byte)'I';
        header[2] = (byte)(bigTiff ? 43 : 42);
        if (bigTiff)
        {
            header[4] = 8;   // the size of an offset
        }

        output.Write(header);
        _link = bigTiff ? 8 : 4;
        Length = header.Length;
    }

    public bool BigTiff { get; }

    /// <summary>The bytes written so far, from the start of the file.</summary>
    public long Length { get; private set; }

    /// <summary>Appends a strip or tile on an even offset; returns its offset in the file.</summary>
    /// <exception cref="InvalidOperationException">A classic file would go beyond 4 GB.</exception>
    public long Append(ReadOnlySpan<byte> data)
    {
        Align();
        long offset = Length;
        _output.Write(data);
        Length += data.Length;
        CheckSize();
        return offset;
    }

    /// <summary>Writes a directory after what is written, links it from the previous one, and returns its offset.</summary>
    public long WriteDirectory(IEnumerable<DirectoryEntry> entries)
    {
        var sorted = entries.OrderBy(e => e.Code).ToList();
        int inline = BigTiff ? 8 : 4;
        int entrySize = BigTiff ? 20 : 12;
        if (!BigTiff && sorted.Count > ushort.MaxValue)
        {
            throw new InvalidOperationException($"A directory of {sorted.Count} entries does not fit classic TIFF.");
        }

        Align();
        long offset = Length;
        long tableSize = (BigTiff ? 8 : 2) + sorted.Count * entrySize + (BigTiff ? 8 : 4);
        var table = new byte[tableSize];
        var values = new MemoryStream();
        long valuesStart = offset + tableSize;
        int at = 0;
        if (BigTiff)
        {
            BinaryPrimitives.WriteUInt64LittleEndian(table, (ulong)sorted.Count);
            at = 8;
        }
        else
        {
            BinaryPrimitives.WriteUInt16LittleEndian(table, (ushort)sorted.Count);
            at = 2;
        }

        foreach (var e in sorted)
        {
            var entry = table.AsSpan(at, entrySize);
            BinaryPrimitives.WriteUInt16LittleEndian(entry, e.Code);
            BinaryPrimitives.WriteUInt16LittleEndian(entry[2..], (ushort)e.Type);
            if (BigTiff)
            {
                BinaryPrimitives.WriteUInt64LittleEndian(entry[4..], (ulong)e.Count);
            }
            else
            {
                BinaryPrimitives.WriteUInt32LittleEndian(entry[4..], checked((uint)e.Count));
            }

            var field = entry[(BigTiff ? 12 : 8)..];
            if (e.Value.Length <= inline)
            {
                e.Value.CopyTo(field);
            }
            else
            {
                if ((valuesStart + values.Length) % 2 != 0)
                {
                    values.WriteByte(0);
                }

                long valueOffset = valuesStart + values.Length;
                WriteOffset(field, valueOffset);
                values.Write(e.Value);
            }

            at += entrySize;
        }

        _output.Write(table);
        values.Position = 0;
        values.CopyTo(_output);
        Length += table.Length + values.Length;
        CheckSize();

        // Link it from the header or the previous directory, then go back to the end.
        Patch(_link, offset);
        _link = offset + tableSize - (BigTiff ? 8 : 4);
        return offset;
    }

    private void WriteOffset(Span<byte> field, long offset)
    {
        if (BigTiff)
        {
            BinaryPrimitives.WriteUInt64LittleEndian(field, (ulong)offset);
        }
        else
        {
            BinaryPrimitives.WriteUInt32LittleEndian(field, checked((uint)offset));
        }
    }

    private void Patch(long at, long offset)
    {
        Span<byte> value = stackalloc byte[BigTiff ? 8 : 4];
        WriteOffset(value, offset);
        long end = _output.Position;
        _output.Position = _start + at;
        _output.Write(value);
        _output.Position = end;
    }

    private void Align()
    {
        if (Length % 2 != 0)
        {
            _output.WriteByte(0);
            Length++;
        }
    }

    private void CheckSize()
    {
        if (!BigTiff && Length > uint.MaxValue)
        {
            throw new InvalidOperationException("The file goes beyond the 4 GB of classic TIFF: write it as BigTIFF (TiffBigTiff.Auto or Always).");
        }
    }
}
