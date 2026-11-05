using System.Buffers.Binary;

namespace Eleris.TiffNet.Tests;

/// <summary>
/// Builds TIFF files byte by byte for the tests: classic or BigTIFF, either byte order, several directories, each entry with the
/// exact type, count and value (or offset) wanted, defects included. Out-of-line values are placed after each directory.
/// </summary>
internal sealed class TiffBuilder(bool bigTiff = false, bool littleEndian = true)
{
    private readonly List<Directory> _directories = [];
    private readonly List<(long Offset, byte[] Bytes)> _blobs = [];

    public bool BigTiff { get; } = bigTiff;

    public bool LittleEndian { get; } = littleEndian;

    /// <summary>Raw data placed right after the header (strips, for example); its offset is <see cref="DataOffset"/>.</summary>
    public byte[] Data { get; set; } = [];

    public long DataOffset => BigTiff ? 16 : 8;

    /// <summary>Adds a directory; directories are chained in the order they are added unless a link says otherwise.</summary>
    public Directory AddDirectory()
    {
        var d = new Directory(this);
        _directories.Add(d);
        return d;
    }

    /// <summary>Puts bytes at a given offset of the file (after the build has laid out the rest), to craft overlaps.</summary>
    public TiffBuilder Patch(long offset, byte[] bytes)
    {
        _blobs.Add((offset, bytes));
        return this;
    }

    public byte[] Build()
    {
        var output = new List<byte>();
        output.AddRange(LittleEndian ? "II"u8.ToArray() : "MM"u8.ToArray());
        output.AddRange(U16(BigTiff ? (ushort)43 : (ushort)42));
        if (BigTiff)
        {
            output.AddRange(U16(8));
            output.AddRange(U16(0));
            output.AddRange(U64(0));
        }
        else
        {
            output.AddRange(U32(0));
        }

        output.AddRange(Data);
        Align(output);
        var offsets = new List<long>();
        var nextFields = new List<int>();
        foreach (var d in _directories)
        {
            Align(output);
            offsets.Add(output.Count);
            nextFields.Add(d.Write(output));
        }

        for (int i = 0; i < _directories.Count; i++)
        {
            long next = _directories[i].NextOverride ?? (i + 1 < _directories.Count ? offsets[i + 1] : 0);
            WriteOffset(output, nextFields[i], next);
        }

        if (_directories.Count > 0)
        {
            WriteOffset(output, BigTiff ? 8 : 4, _directories[0].FirstOverride ?? offsets[0]);
        }

        byte[] bytes = [.. output];
        foreach (var (offset, blob) in _blobs)
        {
            blob.CopyTo(bytes, offset);
        }

        return bytes;
    }

    /// <summary>The offset at which directory <paramref name="index"/> will be written (for links built before the file).</summary>
    public long OffsetOf(int index)
    {
        byte[] built = Build();
        long offset = BigTiff ? (long)BinaryPrimitives.ReadUInt64LittleEndian(built.AsSpan(8)) : BinaryPrimitives.ReadUInt32LittleEndian(built.AsSpan(4));
        if (!LittleEndian)
        {
            offset = BigTiff ? (long)BinaryPrimitives.ReadUInt64BigEndian(built.AsSpan(8)) : BinaryPrimitives.ReadUInt32BigEndian(built.AsSpan(4));
        }

        for (int i = 0; i < index; i++)
        {
            int count = BigTiff ? (int)Read64(built, offset) : Read16(built, offset);
            long nextField = offset + (BigTiff ? 8 : 2) + count * (BigTiff ? 20 : 12);
            offset = BigTiff ? (long)Read64(built, nextField) : Read32(built, nextField);
        }

        return offset;
    }

    private ushort Read16(byte[] b, long o) => LittleEndian ? BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan((int)o)) : BinaryPrimitives.ReadUInt16BigEndian(b.AsSpan((int)o));

    private uint Read32(byte[] b, long o) => LittleEndian ? BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan((int)o)) : BinaryPrimitives.ReadUInt32BigEndian(b.AsSpan((int)o));

    private ulong Read64(byte[] b, long o) => LittleEndian ? BinaryPrimitives.ReadUInt64LittleEndian(b.AsSpan((int)o)) : BinaryPrimitives.ReadUInt64BigEndian(b.AsSpan((int)o));

    private static void Align(List<byte> output)
    {
        while (output.Count % 2 != 0)
        {
            output.Add(0);
        }
    }

    private void WriteOffset(List<byte> output, int at, long value)
    {
        byte[] bytes = BigTiff ? U64((ulong)value) : U32((uint)value);
        for (int i = 0; i < bytes.Length; i++)
        {
            output[at + i] = bytes[i];
        }
    }

    public byte[] U16(ushort v)
    {
        var b = new byte[2];
        if (LittleEndian) BinaryPrimitives.WriteUInt16LittleEndian(b, v); else BinaryPrimitives.WriteUInt16BigEndian(b, v);
        return b;
    }

    public byte[] U32(uint v)
    {
        var b = new byte[4];
        if (LittleEndian) BinaryPrimitives.WriteUInt32LittleEndian(b, v); else BinaryPrimitives.WriteUInt32BigEndian(b, v);
        return b;
    }

    public byte[] U64(ulong v)
    {
        var b = new byte[8];
        if (LittleEndian) BinaryPrimitives.WriteUInt64LittleEndian(b, v); else BinaryPrimitives.WriteUInt64BigEndian(b, v);
        return b;
    }

    /// <summary>One directory: entries in the order given (sorted only if asked), with their values.</summary>
    internal sealed class Directory(TiffBuilder file)
    {
        private readonly List<(ushort Code, ushort Type, ulong Count, byte[] Values, ulong? Offset)> _entries = [];

        /// <summary>The offset of the next directory, instead of the following one (a loop, a bad offset).</summary>
        public long? NextOverride { get; set; }

        /// <summary>For the first directory: the offset written in the header, instead of its own.</summary>
        public long? FirstOverride { get; set; }

        /// <summary>A tag with values of the given type, encoded in the byte order of the file.</summary>
        public Directory Add(ushort code, TiffTagType type, params long[] values)
        {
            var bytes = new List<byte>();
            foreach (long v in values)
            {
                bytes.AddRange(type switch
                {
                    TiffTagType.Byte or TiffTagType.SByte or TiffTagType.Ascii or TiffTagType.Undefined => [(byte)v],
                    TiffTagType.Short or TiffTagType.SShort => file.U16((ushort)v),
                    TiffTagType.Long or TiffTagType.SLong or TiffTagType.Ifd or TiffTagType.Float => file.U32((uint)v),
                    TiffTagType.Rational or TiffTagType.SRational => file.U32((uint)v),   // give numerator and denominator
                    _ => file.U64((ulong)v),
                });
            }

            ulong count = type is TiffTagType.Rational or TiffTagType.SRational ? (ulong)values.Length / 2 : (ulong)values.Length;
            _entries.Add((code, (ushort)type, count, [.. bytes], null));
            return this;
        }

        public Directory Text(ushort code, string value) => Raw(code, TiffTagType.Ascii, [.. System.Text.Encoding.UTF8.GetBytes(value), 0]);

        public Directory Raw(ushort code, TiffTagType type, byte[] bytes) => Raw(code, (ushort)type, (ulong)(bytes.Length / Math.Max(1, Size(type))), bytes);

        /// <summary>An entry with any type and count, and these bytes as its values.</summary>
        public Directory Raw(ushort code, ushort type, ulong count, byte[] bytes)
        {
            _entries.Add((code, type, count, bytes, null));
            return this;
        }

        /// <summary>An entry whose value field holds this offset, whatever its count and type (values elsewhere or nowhere).</summary>
        public Directory Pointing(ushort code, TiffTagType type, ulong count, ulong offset)
        {
            _entries.Add((code, (ushort)type, count, [], offset));
            return this;
        }

        /// <summary>A minimal image: width, height, 8-bit grey, one uncompressed strip at the start of the data.</summary>
        public Directory Image(long width, long height, ushort bits = 8, ushort samples = 1, ushort photometric = 1)
        {
            Add(256, TiffTagType.Long, width);
            Add(257, TiffTagType.Long, height);
            Add(258, TiffTagType.Short, Enumerable.Repeat((long)bits, samples).ToArray());
            Add(259, TiffTagType.Short, 1);
            Add(262, TiffTagType.Short, photometric);
            Add(273, TiffTagType.Long, file.DataOffset);
            Add(277, TiffTagType.Short, samples);
            Add(278, TiffTagType.Long, height);
            Add(279, TiffTagType.Long, width * height * samples * bits / 8);
            return this;
        }

        public Directory Sorted()
        {
            _entries.Sort((a, b) => a.Code.CompareTo(b.Code));
            return this;
        }

        public Directory Remove(ushort code)
        {
            _entries.RemoveAll(e => e.Code == code);
            return this;
        }

        private static int Size(TiffTagType type) => type switch
        {
            TiffTagType.Short or TiffTagType.SShort => 2,
            TiffTagType.Long or TiffTagType.SLong or TiffTagType.Float or TiffTagType.Ifd => 4,
            TiffTagType.Rational or TiffTagType.SRational or TiffTagType.Double or TiffTagType.Long8 or TiffTagType.SLong8 or TiffTagType.Ifd8 => 8,
            _ => 1,
        };

        /// <summary>Writes the directory, then its out-of-line values; returns the position of its next-offset field.</summary>
        internal int Write(List<byte> output)
        {
            int start = output.Count;
            int fieldSize = file.BigTiff ? 8 : 4;
            int entrySize = file.BigTiff ? 20 : 12;
            output.AddRange(file.BigTiff ? file.U64((ulong)_entries.Count) : file.U16((ushort)_entries.Count));
            int nextField = start + (file.BigTiff ? 8 : 2) + _entries.Count * entrySize;
            long valuesAt = nextField + fieldSize;
            var values = new List<byte>();
            foreach (var (code, type, count, bytes, offset) in _entries)
            {
                output.AddRange(file.U16(code));
                output.AddRange(file.U16(type));
                output.AddRange(file.BigTiff ? file.U64(count) : file.U32((uint)count));
                if (offset is { } o)
                {
                    output.AddRange(file.BigTiff ? file.U64(o) : file.U32((uint)o));
                }
                else if (bytes.Length <= fieldSize)
                {
                    output.AddRange(bytes);
                    output.AddRange(new byte[fieldSize - bytes.Length]);
                }
                else
                {
                    long at = valuesAt + values.Count;
                    output.AddRange(file.BigTiff ? file.U64((ulong)at) : file.U32((uint)at));
                    values.AddRange(bytes);
                    if (values.Count % 2 != 0)
                    {
                        values.Add(0);
                    }
                }
            }

            output.AddRange(new byte[fieldSize]);
            output.AddRange(values);
            return nextField;
        }
    }
}
