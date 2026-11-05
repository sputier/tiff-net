using System.Buffers.Binary;

namespace Eleris.TiffNet.Fuzz;

/// <summary>The kinds of mutation of a TIFF input.</summary>
internal enum MutationMode
{
    /// <summary>A mix of the three others, chosen per input.</summary>
    Mixed,

    /// <summary>The directory: tags, types, counts, offsets, entry counts, links between directories.</summary>
    Directory,

    /// <summary>The compressed data of the strips and tiles (and the JPEG tables) only, the structure left valid.</summary>
    Payload,

    /// <summary>Bytes anywhere: flips, insertions, deletions, truncation.</summary>
    Havoc,
}

/// <summary>
/// Structure-aware mutations of TIFF files, from the fuzzer of the review of the first implementation. Every mutation is a pure
/// function of the input and of the <see cref="Random"/> it is given, so that an input can be produced again from its seed. A
/// mutation never throws: an input it cannot parse is mutated byte by byte.
/// </summary>
internal static class Mutator
{
    private static readonly ushort[] KnownTags =
    [
        254, 255, 256, 257, 258, 259, 262, 263, 266, 269, 270, 271, 272, 273, 274, 277, 278, 279, 280, 281, 282, 283, 284, 285, 286,
        287, 296, 297, 301, 305, 306, 315, 316, 317, 318, 319, 320, 322, 323, 324, 325, 330, 332, 338, 339, 340, 341, 343, 344, 347,
        512, 513, 514, 515, 529, 530, 531, 532, 700, 32995, 32996, 32997, 32998, 33432, 33723, 34377, 34665, 34675, 34853, 37724,
        42113, 50706, 65000, 65535,
    ];

    private static readonly long[] Interesting =
    [
        0, 1, 2, 3, 4, 7, 8, 15, 16, 17, 31, 32, 63, 64, 127, 128, 255, 256, 511, 512, 1023, 1024, 4095, 4096, 32767, 32768, 65535,
        65536, 0x7FFF_FFFF, 0x8000_0000, 0xFFFF_FFFF, 0xFFFF_FFFE, 0x1_0000_0000, 0x7FFF_FFFF_FFFF_FFFF, -1, -2, 0x1000_0000,
        0x0100_0000,
    ];

    private static readonly int[] Compressions =
        [0, 1, 2, 3, 4, 5, 6, 7, 8, 32766, 32773, 32809, 32895, 32908, 32909, 32946, 34661, 34676, 34677, 34712, 34925, 50000, 65535];

    private readonly record struct Entry(int Position, ushort Tag, ushort Type, ulong Count, ulong Value, int ValuePosition);

    private sealed class Directory
    {
        public bool Little { get; init; }

        public bool Big { get; init; }

        public int Position { get; init; }

        public List<Entry> Entries { get; } = [];

        public int NextPosition { get; set; }

        public int EntrySize => Big ? 20 : 12;
    }

    public static byte[] Mutate(byte[] seed, Random random, MutationMode mode)
    {
        try
        {
            var chosen = mode == MutationMode.Mixed ? (MutationMode)random.Next(1, 4) : mode;
            byte[] input = chosen switch
            {
                MutationMode.Directory => MutateDirectory(seed, random),
                MutationMode.Payload => MutatePayload(seed, random),
                _ => Havoc(seed, random),
            };
            if (random.Next(10) == 0)
            {
                input = chosen == MutationMode.Directory ? MutatePayload(input, random) : MutateDirectory(input, random);
            }

            return input;
        }
        catch (Exception ex) when (ex is ArgumentException or IndexOutOfRangeException or OverflowException)
        {
            // A defect of a mutation must not stop a campaign: fall back to byte mutations, still determined by the random state.
            return Havoc(seed, random);
        }
    }

    private static Directory? Parse(byte[] d)
    {
        if (d.Length < 16)
        {
            return null;
        }

        bool little;
        if (d[0] == 'I' && d[1] == 'I')
        {
            little = true;
        }
        else if (d[0] == 'M' && d[1] == 'M')
        {
            little = false;
        }
        else
        {
            return null;
        }

        bool big = R16(d, 2, little) == 43;
        ulong offset = big ? R64(d, 8, little) : R32(d, 4, little);
        if (offset == 0 || offset >= (ulong)d.Length - 8)
        {
            return null;
        }

        var dir = new Directory { Little = little, Big = big, Position = (int)offset };
        ulong count = big ? R64(d, dir.Position, little) : R16(d, dir.Position, little);
        int position = dir.Position + (big ? 8 : 2);
        for (ulong i = 0; i < count && position + dir.EntrySize <= d.Length; i++, position += dir.EntrySize)
        {
            int valuePosition = position + (big ? 12 : 8);
            dir.Entries.Add(new Entry(position, R16(d, position, little), R16(d, position + 2, little),
                big ? R64(d, position + 4, little) : R32(d, position + 4, little),
                big ? R64(d, valuePosition, little) : R32(d, valuePosition, little), valuePosition));
        }

        dir.NextPosition = position;
        return dir;
    }

    private static ulong Pick(Random r, int length) => r.Next(6) switch
    {
        0 => (ulong)Interesting[r.Next(Interesting.Length)],
        1 => (ulong)r.Next(0, Math.Max(1, length + 16)),
        2 => (ulong)(length + r.Next(-8, 8)),
        3 => (ulong)r.NextInt64(),
        4 => (ulong)r.Next(0, 70000),
        _ => (1UL << r.Next(0, 63)) + (ulong)r.Next(-1, 2),
    };

    private static byte[] MutateDirectory(byte[] source, Random r)
    {
        byte[] d = (byte[])source.Clone();
        var dir = Parse(d);
        if (dir is null || dir.Entries.Count == 0)
        {
            return Havoc(d, r);
        }

        int operations = 1 + r.Next(3);
        for (int k = 0; k < operations; k++)
        {
            var e = dir.Entries[r.Next(dir.Entries.Count)];
            switch (r.Next(14))
            {
                case 0:
                    W16(d, e.Position + 2, dir.Little, (ushort)(r.Next(4) == 0 ? r.Next(0, 65536) : r.Next(0, 20)));
                    break;
                case 1:
                    ulong count = r.Next(3) switch
                    {
                        0 => Pick(r, d.Length),
                        1 => e.Count + (ulong)r.Next(-2, 3),
                        _ => e.Count * (ulong)r.Next(2, 5),
                    };
                    WriteOffset(d, dir, e.Position + 4, count);
                    break;
                case 2:
                    WriteOffset(d, dir, e.ValuePosition, Pick(r, d.Length));
                    break;
                case 3:
                    W16(d, e.Position, dir.Little, KnownTags[r.Next(KnownTags.Length)]);
                    break;
                case 4:
                    ulong value = e.Tag switch
                    {
                        259 => (ulong)Compressions[r.Next(Compressions.Length)],
                        262 => (ulong)(r.Next(5) == 0 ? r.Next(0, 65536) : new[] { 0, 1, 2, 3, 4, 5, 6, 8, 9, 10, 32844, 32845 }[r.Next(12)]),
                        258 => (ulong)new[] { 0, 1, 2, 4, 8, 12, 16, 24, 32, 64, 128, 255, 65535 }[r.Next(13)],
                        277 => (ulong)new[] { 0, 1, 2, 3, 4, 5, 255, 32767, 65535 }[r.Next(9)],
                        284 => (ulong)r.Next(0, 4),
                        317 => (ulong)r.Next(0, 5),
                        339 => (ulong)r.Next(0, 8),
                        274 => (ulong)r.Next(0, 10),
                        _ => Pick(r, d.Length),
                    };
                    SetValue(d, dir, e, value);
                    break;
                case 5:
                    var other = dir.Entries[r.Next(dir.Entries.Count)];
                    Buffer.BlockCopy(d, e.Position, d, other.Position, dir.EntrySize);
                    break;
                case 6:
                    var swapped = dir.Entries[r.Next(dir.Entries.Count)];
                    byte[] buffer = new byte[dir.EntrySize];
                    Buffer.BlockCopy(d, e.Position, buffer, 0, dir.EntrySize);
                    Buffer.BlockCopy(d, swapped.Position, d, e.Position, dir.EntrySize);
                    Buffer.BlockCopy(buffer, 0, d, swapped.Position, dir.EntrySize);
                    break;
                case 7:
                    ulong entries = (ulong)dir.Entries.Count;
                    ulong newCount = r.Next(3) switch { 0 => entries + (ulong)r.Next(-3, 4), 1 => Pick(r, d.Length), _ => 0 };
                    if (dir.Big)
                    {
                        W64(d, dir.Position, dir.Little, newCount);
                    }
                    else
                    {
                        W16(d, dir.Position, dir.Little, (ushort)newCount);
                    }

                    break;
                case 8:
                    ulong next = r.Next(3) switch
                    {
                        0 => (ulong)dir.Position,
                        1 => Pick(r, d.Length),
                        _ => (ulong)Math.Max(8, dir.Position - r.Next(0, 64)),
                    };
                    WriteOffset(d, dir, dir.NextPosition, next);
                    break;
                case 9:
                    if (e.Value > 8 && e.Value < (ulong)d.Length)
                    {
                        int at = (int)e.Value + r.Next(0, 16);
                        if (at + 4 <= d.Length)
                        {
                            if (r.Next(2) == 0)
                            {
                                W32(d, at, dir.Little, (uint)Pick(r, d.Length));
                            }
                            else
                            {
                                d[at] ^= (byte)(1 << r.Next(8));
                            }
                        }
                    }

                    break;
                case 10:
                    // Sub-directory pointers (SubIFDs, EXIF, GPS, Interoperability) to the directory itself: loops.
                    W16(d, e.Position, dir.Little, new ushort[] { 330, 34665, 34853, 40965 }[r.Next(4)]);
                    W16(d, e.Position + 2, dir.Little, (ushort)(r.Next(2) == 0 ? 4 : 13));
                    WriteOffset(d, dir, e.ValuePosition, (ulong)dir.Position);
                    break;
                case 11:
                    if (r.Next(2) == 0)
                    {
                        W16(d, 2, dir.Little, dir.Big ? (ushort)42 : (ushort)43);
                    }
                    else
                    {
                        W16(d, 4, dir.Little, (ushort)r.Next(0, 16));
                    }

                    break;
                default:
                    return Havoc(d, r, 1 + r.Next(3));
            }
        }

        return d;
    }

    private static byte[] MutatePayload(byte[] source, Random r)
    {
        byte[] d = (byte[])source.Clone();
        var dir = Parse(d);
        if (dir is null)
        {
            return Havoc(d, r);
        }

        var offsets = Values(d, dir, dir.Entries.FirstOrDefault(e => e.Tag is 273 or 324));
        var counts = Values(d, dir, dir.Entries.FirstOrDefault(e => e.Tag is 279 or 325));
        var regions = new List<(int Start, int Length)>();
        for (int i = 0; i < Math.Min(offsets.Count, counts.Count); i++)
        {
            if (offsets[i] < (ulong)d.Length && counts[i] > 0)
            {
                regions.Add(((int)offsets[i], (int)Math.Min(counts[i], (ulong)d.Length - offsets[i])));
            }
        }

        var tables = dir.Entries.FirstOrDefault(e => e.Tag == 347);
        if (tables.Tag == 347 && tables.Value < (ulong)d.Length && tables.Count > 4)
        {
            regions.Add(((int)tables.Value, (int)Math.Min(tables.Count, (ulong)d.Length - tables.Value)));
        }

        regions.RemoveAll(x => x.Length <= 0);
        if (regions.Count == 0)
        {
            return Havoc(d, r);
        }

        int operations = 1 + r.Next(r.Next(2) == 0 ? 2 : 8);
        for (int k = 0; k < operations; k++)
        {
            var (start, length) = regions[r.Next(regions.Count)];
            int at = start + r.Next(length);
            int room = start + length - at;
            switch (r.Next(6))
            {
                case 0:
                    d[at] ^= (byte)(1 << r.Next(8));
                    break;
                case 1:
                    d[at] = (byte)r.Next(256);
                    break;
                case 2:
                    d[at] = new byte[] { 0, 0xFF, 0x7F, 0x80, 1 }[r.Next(5)];
                    break;
                case 3:
                    r.NextBytes(d.AsSpan(at, Math.Min(r.Next(1, 32), room)));
                    break;
                case 4:
                    int from = start + r.Next(length);
                    Buffer.BlockCopy(d, from, d, at, Math.Min(Math.Min(r.Next(1, 64), room), start + length - from));
                    break;
                default:
                    d.AsSpan(at, Math.Min(r.Next(1, 16), room)).Fill((byte)(r.Next(2) == 0 ? 0 : 0xFF));
                    break;
            }
        }

        return d;
    }

    private static List<ulong> Values(byte[] d, Directory dir, Entry e)
    {
        var values = new List<ulong>();
        if (e.Tag == 0 || e.Count == 0 || e.Count > 100_000)
        {
            return values;
        }

        int width = e.Type switch { 3 => 2, 16 => 8, _ => 4 };
        bool inline = (ulong)width * e.Count <= (ulong)(dir.Big ? 8 : 4);
        long start = inline ? e.ValuePosition : (long)e.Value;
        for (ulong i = 0; i < e.Count; i++)
        {
            long at = start + (long)i * width;
            if (at < 0 || at + width > d.Length)
            {
                break;
            }

            values.Add(width switch
            {
                2 => R16(d, (int)at, dir.Little),
                8 => R64(d, (int)at, dir.Little),
                _ => R32(d, (int)at, dir.Little),
            });
        }

        return values;
    }

    private static byte[] Havoc(byte[] source, Random r, int operations = -1)
    {
        var d = new List<byte>(source);
        if (operations < 0)
        {
            operations = 1 + r.Next(r.Next(2) == 0 ? 4 : 16);
        }

        for (int k = 0; k < operations && d.Count > 0; k++)
        {
            int at = r.Next(d.Count);
            switch (r.Next(9))
            {
                case 0:
                    d[at] ^= (byte)(1 << r.Next(8));
                    break;
                case 1:
                    d[at] = (byte)r.Next(256);
                    break;
                case 2:
                    if (at + 4 <= d.Count)
                    {
                        byte[] v = BitConverter.GetBytes((uint)Pick(r, d.Count));
                        for (int i = 0; i < 4; i++)
                        {
                            d[at + i] = v[i];
                        }
                    }

                    break;
                case 3:
                    if (at + 2 <= d.Count)
                    {
                        byte[] v = BitConverter.GetBytes((ushort)Pick(r, d.Count));
                        d[at] = v[0];
                        d[at + 1] = v[1];
                    }

                    break;
                case 4:
                    d.RemoveRange(at, Math.Min(r.Next(1, 64), d.Count - at));
                    break;
                case 5:
                    int inserted = r.Next(1, 32);
                    var bytes = new byte[inserted];
                    r.NextBytes(bytes);
                    d.InsertRange(at, bytes);
                    break;
                case 6:
                    if (r.Next(8) == 0)
                    {
                        d.RemoveRange(at, d.Count - at);
                    }

                    break;
                case 7:
                    int source2 = r.Next(d.Count);
                    int length = Math.Min(r.Next(1, 64), Math.Min(d.Count - source2, d.Count - at));
                    for (int i = 0; i < length; i++)
                    {
                        d[at + i] = d[source2 + i];
                    }

                    break;
                default:
                    d[at] = new byte[] { 0, 0xFF, 0x7F, 0x80, 1 }[r.Next(5)];
                    break;
            }
        }

        return [.. d];
    }

    private static void SetValue(byte[] d, Directory dir, Entry e, ulong value)
    {
        int size = e.Type switch { 1 or 2 or 6 or 7 => 1, 3 or 8 => 2, 16 or 17 or 18 => 8, _ => 4 };
        switch (size)
        {
            case 1 when e.ValuePosition < d.Length:
                d[e.ValuePosition] = (byte)value;
                break;
            case 2:
                W16(d, e.ValuePosition, dir.Little, (ushort)value);
                break;
            case 8 when dir.Big:
                W64(d, e.ValuePosition, dir.Little, value);
                break;
            default:
                W32(d, e.ValuePosition, dir.Little, (uint)value);
                break;
        }
    }

    private static void WriteOffset(byte[] d, Directory dir, int position, ulong value)
    {
        if (dir.Big)
        {
            W64(d, position, dir.Little, value);
        }
        else
        {
            W32(d, position, dir.Little, (uint)value);
        }
    }

    private static ushort R16(byte[] d, int o, bool little) =>
        little ? BinaryPrimitives.ReadUInt16LittleEndian(d.AsSpan(o)) : BinaryPrimitives.ReadUInt16BigEndian(d.AsSpan(o));

    private static uint R32(byte[] d, int o, bool little) =>
        little ? BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(o)) : BinaryPrimitives.ReadUInt32BigEndian(d.AsSpan(o));

    private static ulong R64(byte[] d, int o, bool little) =>
        little ? BinaryPrimitives.ReadUInt64LittleEndian(d.AsSpan(o)) : BinaryPrimitives.ReadUInt64BigEndian(d.AsSpan(o));

    private static void W16(byte[] d, int o, bool little, ushort v)
    {
        if (o < 0 || o + 2 > d.Length)
        {
            return;
        }

        if (little)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(d.AsSpan(o), v);
        }
        else
        {
            BinaryPrimitives.WriteUInt16BigEndian(d.AsSpan(o), v);
        }
    }

    private static void W32(byte[] d, int o, bool little, uint v)
    {
        if (o < 0 || o + 4 > d.Length)
        {
            return;
        }

        if (little)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(d.AsSpan(o), v);
        }
        else
        {
            BinaryPrimitives.WriteUInt32BigEndian(d.AsSpan(o), v);
        }
    }

    private static void W64(byte[] d, int o, bool little, ulong v)
    {
        if (o < 0 || o + 8 > d.Length)
        {
            return;
        }

        if (little)
        {
            BinaryPrimitives.WriteUInt64LittleEndian(d.AsSpan(o), v);
        }
        else
        {
            BinaryPrimitives.WriteUInt64BigEndian(d.AsSpan(o), v);
        }
    }
}
