using System.Buffers.Binary;

namespace Eleris.TiffNet.Internal.Jpeg;

/// <summary>One component of a decoded frame: its samples at its own resolution, in a plane padded to whole MCUs.</summary>
internal sealed class JpegComponent
{
    public required int Id { get; init; }

    /// <summary>The horizontal sampling factor (1 to 4).</summary>
    public required int H { get; init; }

    /// <summary>The vertical sampling factor (1 to 4).</summary>
    public required int V { get; init; }

    public required int QuantizationTable { get; init; }

    /// <summary>The width of the component, in samples: the frame width scaled by H / Hmax, rounded up (A.1.1).</summary>
    public required int Width { get; init; }

    public required int Height { get; init; }

    /// <summary>The samples, row after row; the plane covers whole MCUs, beyond <see cref="Width"/> x <see cref="Height"/>.</summary>
    /// <remarks>Allocated with the frame for a sequential image, after the last scan for a progressive one.</remarks>
    public byte[] Plane { get; internal set; } = [];

    /// <summary>The rows of <see cref="Plane"/> (whole MCUs).</summary>
    internal int PlaneRows { get; init; }

    /// <summary>The bytes of a row of <see cref="Plane"/>.</summary>
    public required int Stride { get; init; }

    internal int DcTable { get; set; }

    internal int AcTable { get; set; }

    internal int Prediction { get; set; }

    internal bool Scanned { get; set; }

    /// <summary>The blocks of a row of <see cref="Plane"/> (whole MCUs).</summary>
    internal int BlocksAcross => Stride / 8;

    /// <summary>A progressive image: the coefficients of every block, 64 per block, refined scan after scan.</summary>
    internal short[]? Coefficients { get; init; }

    /// <summary>A progressive image: the bit of each coefficient known so far (-1: none), to tell scans that refine nothing.</summary>
    internal int[]? KnownBits { get; init; }

    /// <summary>The quantization table, latched at the first scan of the component (as libjpeg does).</summary>
    internal ushort[]? Quantization { get; set; }
}

/// <summary>A decoded JPEG frame: its size, its components (before any colour conversion), and what its markers say about colour.</summary>
internal sealed class JpegFrame
{
    public required int Width { get; init; }

    public required int Height { get; init; }

    public required JpegComponent[] Components { get; init; }

    /// <summary>Whether the frame is progressive (SOF2): its coefficients are transformed once its last scan is read.</summary>
    public bool Progressive { get; init; }

    /// <summary>The colour transform of an Adobe APP14 marker (0: none, 1: YCbCr), or null without that marker.</summary>
    public int? AdobeTransform { get; set; }

    /// <summary>Whether the stream has a JFIF APP0 marker (its components are then YCbCr).</summary>
    public bool Jfif { get; set; }
}

/// <summary>
/// A decoder of JPEG streams (ITU-T T.81), sequential (SOF0, SOF1) and progressive (SOF2) with Huffman coding, 8-bit samples,
/// 1 to 3 components with sampling factors up to 4, restart intervals; the tables of an abbreviated stream (TIFF's
/// <c>JPEGTables</c>) are read first. Anything else (lossless, hierarchical, arithmetic coding, 12-bit, 4 components, DNL) is
/// refused as not supported.
/// </summary>
/// <remarks>
/// Defects of the data are <see cref="InvalidDataException"/>; what it decodes to is counted in the budget before it is allocated.
/// The decoding of a scan is bounded by its frame: every block is decoded at most once. As libjpeg does, the MCU in which the data
/// of a scan runs out ends with zeros, and the blocks after it (until a restart marker) are not decoded but grey (128), which
/// costs a fill, not a decoding; a component that no scan holds is grey too. A progressive image keeps its coefficients (counted
/// in the budget before they are allocated) until its last scan; its scans are limited by MaxJpegScans, each costs at most one
/// pass over the blocks of its components, and a scan that refines no coefficient is refused.
/// </remarks>
internal sealed class JpegDecoder(ResourceBudget budget, int maxWidth = ushort.MaxValue, int maxHeight = ushort.MaxValue)
{
    /// <summary>The natural (row by row) index of the k-th coefficient in zig-zag order, with 16 more entries that absorb a run
    /// past the end of a damaged block, as in libjpeg.</summary>
    private static ReadOnlySpan<byte> Natural =>
    [
        0, 1, 8, 16, 9, 2, 3, 10, 17, 24, 32, 25, 18, 11, 4, 5, 12, 19, 26, 33, 40, 48, 41, 34, 27, 20, 13, 6, 7, 14, 21, 28, 35, 42,
        49, 56, 57, 50, 43, 36, 29, 22, 15, 23, 30, 37, 44, 51, 58, 59, 52, 45, 38, 31, 39, 46, 53, 60, 61, 54, 47, 55, 62, 63,
        63, 63, 63, 63, 63, 63, 63, 63, 63, 63, 63, 63, 63, 63, 63, 63,
    ];

    private readonly ushort[]?[] _quantization = new ushort[4][];
    private readonly HuffmanTable?[] _dc = new HuffmanTable[4];
    private readonly HuffmanTable?[] _ac = new HuffmanTable[4];
    private int _restartInterval;
    private int _scans;
    private JpegFrame? _frame;
    private int? _adobeTransform;
    private bool _jfif;
    private int _endOfBands;
    private long _reserved;

    /// <summary>Gives back to the budget what the frame counted, once its samples are no longer needed.</summary>
    public void Release()
    {
        budget.ReleasePixels(_reserved);
        _reserved = 0;
    }

    /// <summary>Reads the tables of an abbreviated table-specification stream (SOI, DQT and DHT, EOI), as TIFF's JPEGTables holds.</summary>
    /// <exception cref="InvalidDataException">The stream is damaged.</exception>
    public void ReadTables(ReadOnlySpan<byte> tables)
    {
        if (Parse(tables, tablesOnly: true) is not null)
        {
            throw new InvalidDataException("the JPEG tables hold an image.");
        }
    }

    /// <summary>Decodes a stream (or the image part of an abbreviated one, after <see cref="ReadTables"/>).</summary>
    /// <exception cref="InvalidDataException">The stream is damaged.</exception>
    /// <exception cref="NotSupportedException">The stream uses a part of JPEG the library does not handle.</exception>
    /// <exception cref="TiffException">The frame exceeds MaxPixelBytes, or the stream has more scans than MaxJpegScans.</exception>
    public JpegFrame Decode(ReadOnlySpan<byte> data) =>
        Parse(data, tablesOnly: false) ?? throw new InvalidDataException("the JPEG stream holds no image.");

    private JpegFrame? Parse(ReadOnlySpan<byte> data, bool tablesOnly)
    {
        if (data.Length < 2 || data[0] != 0xFF || data[1] != 0xD8)
        {
            throw new InvalidDataException("the JPEG stream does not start with a start-of-image marker.");
        }

        int position = 2;
        while (true)
        {
            int marker = NextMarker(data, ref position);
            if (_scans > 0 && marker is not (-1 or 0xD9 or (>= 0xD0 and <= 0xD7) or 0x01) && !Fits(data, position))
            {
                // A stream cut in a marker segment after its first scans: what is decoded is the image, as libjpeg gives it.
                return End(tablesOnly);
            }

            switch (marker)
            {
                case -1:
                case 0xD9:
                    // The end of the image (or of the data, which libjpeg accepts with a warning).
                    return End(tablesOnly);
                case 0xC0:
                case 0xC1:
                case 0xC2:
                    if (tablesOnly)
                    {
                        throw new InvalidDataException("the JPEG tables hold a frame.");
                    }

                    ReadFrame(Segment(data, ref position), progressive: marker == 0xC2);
                    break;
                case 0xC3 or 0xC5 or 0xC6 or 0xC7 or 0xC9 or 0xCA or 0xCB or 0xCD or 0xCE or 0xCF:
                    throw new NotSupportedException($"the JPEG process of marker SOF{marker - 0xC0} (lossless, hierarchical or arithmetic) is not supported.");
                case 0xCC:
                    throw new NotSupportedException("JPEG arithmetic coding is not supported.");
                case 0xC4:
                    ReadHuffmanTables(Segment(data, ref position));
                    break;
                case 0xDB:
                    ReadQuantizationTables(Segment(data, ref position));
                    break;
                case 0xDD:
                    ReadRestartInterval(Segment(data, ref position));
                    break;
                case 0xDC:
                    throw new NotSupportedException("JPEG streams whose height is given by a DNL marker are not supported.");
                case 0xDA:
                    if (tablesOnly)
                    {
                        throw new InvalidDataException("the JPEG tables hold a scan.");
                    }

                    var header = Segment(data, ref position);
                    position = ReadScan(data, header, position);
                    break;
                case 0xEE:
                    ReadAdobe(Segment(data, ref position));
                    break;
                case 0xE0:
                    _jfif |= Segment(data, ref position).StartsWith("JFIF\0"u8);
                    break;
                case 0xD8:
                    throw new InvalidDataException("the JPEG stream has a second start-of-image marker.");
                case >= 0xD0 and <= 0xD7:
                case 0x01:
                    // A restart marker out of a scan, or TEM: no segment; libjpeg ignores them.
                    break;
                default:
                    // APPn, COM and the reserved markers: a segment to skip.
                    _ = Segment(data, ref position);
                    break;
            }
        }
    }

    /// <summary>The frame at the end of the stream: it must have been scanned; its components that no scan held are grey.</summary>
    private JpegFrame? End(bool tablesOnly)
    {
        if (tablesOnly || _frame is null)
        {
            return null;
        }

        if (_scans == 0)
        {
            throw new InvalidDataException("the JPEG stream has a frame but no scan.");
        }

        foreach (var c in _frame.Components)
        {
            if (_frame.Progressive)
            {
                // Counted with the frame: allocated only now, so that a stream refused in its scans never costs its planes.
                c.Plane = new byte[c.Stride * c.PlaneRows];
            }

            if (_frame.Progressive && c.Quantization is { } quantization)
            {
                // The coefficients are complete: each block is transformed once.
                var coefficients = c.Coefficients!;
                int across = c.BlocksAcross, down = c.PlaneRows / 8;
                for (int by = 0; by < down; by++)
                {
                    budget.ThrowIfCancelled();
                    for (int bx = 0; bx < across; bx++)
                    {
                        JpegIdct.Transform(coefficients.AsSpan((by * across + bx) * 64, 64), quantization,
                            c.Plane.AsSpan(by * 8 * c.Stride + bx * 8), c.Stride);
                    }
                }
            }
            else if (!c.Scanned)
            {
                // libjpeg's coefficients of a component without data are 0, which the IDCT turns into 128.
                c.Plane.AsSpan().Fill(128);
            }
        }

        return _frame;
    }

    /// <summary>The next marker at or after <paramref name="position"/> (garbage before it is skipped, as libjpeg does), or -1.</summary>
    private static int NextMarker(ReadOnlySpan<byte> data, ref int position)
    {
        while (position + 1 < data.Length)
        {
            if (data[position] == 0xFF && data[position + 1] is not (0x00 or 0xFF))
            {
                int marker = data[position + 1];
                position += 2;
                return marker;
            }

            position++;
        }

        position = data.Length;
        return -1;
    }

    /// <summary>Whether the segment at <paramref name="position"/> lies within the data.</summary>
    private static bool Fits(ReadOnlySpan<byte> data, int position) =>
        position + 2 <= data.Length && position + BinaryPrimitives.ReadUInt16BigEndian(data[position..]) <= data.Length;

    /// <summary>The payload of the segment at <paramref name="position"/> (after its 2-byte length), which is then skipped.</summary>
    private static ReadOnlySpan<byte> Segment(ReadOnlySpan<byte> data, ref int position)
    {
        if (position + 2 > data.Length)
        {
            throw new InvalidDataException("a JPEG marker segment is cut by the end of the data.");
        }

        int length = BinaryPrimitives.ReadUInt16BigEndian(data[position..]);
        if (length < 2 || position + length > data.Length)
        {
            throw new InvalidDataException($"a JPEG marker segment of {length} bytes does not fit the data.");
        }

        var payload = data.Slice(position + 2, length - 2);
        position += length;
        return payload;
    }

    private void ReadQuantizationTables(ReadOnlySpan<byte> s)
    {
        while (s.Length > 0)
        {
            int precision = s[0] >> 4, id = s[0] & 15;
            int size = precision == 0 ? 64 : 128;
            if (precision > 1 || id > 3 || s.Length < 1 + size)
            {
                throw new InvalidDataException("a JPEG quantization table is not valid.");
            }

            var table = new ushort[64];
            for (int k = 0; k < 64; k++)
            {
                table[Natural[k]] = precision == 0 ? s[1 + k] : BinaryPrimitives.ReadUInt16BigEndian(s[(1 + 2 * k)..]);
            }

            _quantization[id] = table;
            s = s[(1 + size)..];
        }
    }

    private void ReadHuffmanTables(ReadOnlySpan<byte> s)
    {
        while (s.Length > 0)
        {
            if (s.Length < 17)
            {
                throw new InvalidDataException("a JPEG Huffman table is cut.");
            }

            int kind = s[0] >> 4, id = s[0] & 15;
            if (kind > 1 || id > 3)
            {
                throw new InvalidDataException($"a JPEG Huffman table has the class {kind} and the identifier {id}.");
            }

            var counts = s.Slice(1, 16);
            int total = 0;
            foreach (byte c in counts)
            {
                total += c;
            }

            if (s.Length < 17 + total)
            {
                throw new InvalidDataException("a JPEG Huffman table is cut.");
            }

            // Defined into the table of that slot (built when a scan uses it): redefinitions cost their bytes only.
            var tables = kind == 0 ? _dc : _ac;
            (tables[id] ??= new HuffmanTable()).Define(counts, s.Slice(17, total), dc: kind == 0);
            s = s[(17 + total)..];
        }
    }

    private void ReadRestartInterval(ReadOnlySpan<byte> s)
    {
        if (s.Length < 2)
        {
            throw new InvalidDataException("a JPEG restart interval is cut.");
        }

        _restartInterval = BinaryPrimitives.ReadUInt16BigEndian(s);
    }

    private void ReadAdobe(ReadOnlySpan<byte> s)
    {
        if (s.Length >= 12 && s.StartsWith("Adobe"u8))
        {
            _adobeTransform = s[11];
        }
    }

    private void ReadFrame(ReadOnlySpan<byte> s, bool progressive)
    {
        if (_frame is not null)
        {
            throw new InvalidDataException("the JPEG stream has a second frame.");
        }

        if (s.Length < 6)
        {
            throw new InvalidDataException("a JPEG frame header is cut.");
        }

        int precision = s[0], height = BinaryPrimitives.ReadUInt16BigEndian(s[1..]), width = BinaryPrimitives.ReadUInt16BigEndian(s[3..]);
        int count = s[5];
        if (precision != 8)
        {
            throw new NotSupportedException($"{precision}-bit JPEG is not supported.");
        }

        if (height == 0)
        {
            throw new NotSupportedException("JPEG streams whose height is given by a DNL marker are not supported.");
        }

        if (width == 0)
        {
            throw new InvalidDataException("a JPEG frame has a width of 0.");
        }

        if (width > maxWidth || height > maxHeight)
        {
            // Checked before anything is allocated: a stream cannot declare more than its strip or tile holds.
            throw new InvalidDataException($"a JPEG frame of {width} x {height} exceeds its strip or tile of {maxWidth} x {maxHeight}.");
        }

        if (count is 0 or > 4 || s.Length < 6 + 3 * count)
        {
            throw new InvalidDataException($"a JPEG frame declares {count} components.");
        }

        if (count == 4)
        {
            throw new NotSupportedException("JPEG images of 4 components (CMYK) are not supported.");
        }

        var parts = new (int Id, int H, int V, int Tq)[count];
        int hMax = 1, vMax = 1;
        for (int i = 0; i < count; i++)
        {
            var c = s.Slice(6 + 3 * i, 3);
            int h = c[1] >> 4, v = c[1] & 15;
            if (h is < 1 or > 4 || v is < 1 or > 4 || c[2] > 3)
            {
                throw new InvalidDataException($"a JPEG component has the sampling factors {h} x {v} and the table {c[2]}.");
            }

            for (int j = 0; j < i; j++)
            {
                if (parts[j].Id == c[0])
                {
                    throw new InvalidDataException($"two JPEG components have the identifier {c[0]}.");
                }
            }

            parts[i] = (c[0], h, v, c[2]);
            hMax = Math.Max(hMax, h);
            vMax = Math.Max(vMax, v);
        }

        // The planes cover whole MCUs; they (and the coefficients of a progressive image, 2 bytes each) are counted before they
        // are allocated.
        long mcusAcross = (width + 8L * hMax - 1) / (8 * hMax), mcusDown = (height + 8L * vMax - 1) / (8 * vMax);
        long bytes = 0;
        foreach (var p in parts)
        {
            bytes += mcusAcross * p.H * 8 * mcusDown * p.V * 8 * (progressive ? 3 : 1);
        }

        budget.ReservePixels(bytes, progressive ? "the JPEG planes and coefficients" : "the JPEG planes");
        _reserved += bytes;
        foreach (var p in parts)
        {
            if (mcusAcross * p.H * 8 * mcusDown * p.V * 8 > Array.MaxLength)
            {
                throw new NotSupportedException($"a JPEG frame of {width} x {height} has a component larger than an array.");
            }
        }

        var components = new JpegComponent[count];
        for (int i = 0; i < count; i++)
        {
            var p = parts[i];
            int stride = (int)(mcusAcross * p.H * 8);
            components[i] = new JpegComponent
            {
                Id = p.Id,
                H = p.H,
                V = p.V,
                QuantizationTable = p.Tq,
                Width = (int)((width * (long)p.H + hMax - 1) / hMax),
                Height = (int)((height * (long)p.V + vMax - 1) / vMax),
                Plane = progressive ? [] : new byte[(int)(stride * mcusDown * p.V * 8)],
                PlaneRows = (int)(mcusDown * p.V * 8),
                Stride = stride,
                Coefficients = progressive ? new short[(int)(stride * mcusDown * p.V * 8)] : null,
                KnownBits = progressive ? [.. Enumerable.Repeat(-1, 64)] : null,
            };
        }

        _frame = new JpegFrame { Width = width, Height = height, Components = components, Progressive = progressive };
    }

    /// <summary>Decodes a scan whose header is <paramref name="header"/>; its data starts at <paramref name="start"/>. Returns where it ends.</summary>
    private int ReadScan(ReadOnlySpan<byte> data, ReadOnlySpan<byte> header, int start)
    {
        var frame = _frame ?? throw new InvalidDataException("a JPEG scan comes before its frame.");
        budget.CheckJpegScans(++_scans);
        int count = header.Length > 0 ? header[0] : 0;
        if (count is < 1 or > 4 || header.Length < 1 + 2 * count + 3)
        {
            throw new InvalidDataException($"a JPEG scan declares {count} components.");
        }

        var scan = new JpegComponent[count];
        for (int i = 0; i < count; i++)
        {
            int id = header[1 + 2 * i], tables = header[2 + 2 * i];
            var component = Array.Find(frame.Components, c => c.Id == id)
                ?? throw new InvalidDataException($"a JPEG scan refers to the component {id}, which the frame does not have.");
            if (Array.IndexOf(scan, component, 0, i) >= 0 || (component.Scanned && !frame.Progressive))
            {
                throw new InvalidDataException($"the JPEG component {id} is twice in a scan, or in two scans of a sequential image.");
            }

            component.DcTable = tables >> 4;
            component.AcTable = tables & 15;
            if (component.DcTable > 3 || component.AcTable > 3)
            {
                throw new InvalidDataException($"a JPEG scan refers to the Huffman tables {tables:X2}.");
            }

            if (_quantization[component.QuantizationTable] is null)
            {
                throw new InvalidDataException($"the JPEG quantization table {component.QuantizationTable} is not defined.");
            }

            // A stream without Huffman tables (motion JPEG) uses those of annex K, as libjpeg-turbo does.
            (_dc[component.DcTable] ??= HuffmanTable.Standard(dc: true, luminance: component.DcTable == 0)).Build();
            (_ac[component.AcTable] ??= HuffmanTable.Standard(dc: false, luminance: component.AcTable == 0)).Build();
            component.Scanned = true;
            component.Prediction = 0;
            component.Quantization ??= (ushort[])_quantization[component.QuantizationTable]!.Clone();
            scan[i] = component;
        }

        if (frame.Progressive)
        {
            int at = 1 + 2 * count;
            return ReadProgressiveScan(data, start, frame, scan, header[at], header[at + 1], header[at + 2] >> 4, header[at + 2] & 15);
        }

        // Ss, Se, Ah and Al of a sequential scan are 0, 63, 0, 0; libjpeg decodes other values as sequential, with a warning.
        var reader = new JpegBitReader(data, start);
        Span<short> block = stackalloc short[64];
        int hMax = frame.Components.Max(c => c.H), vMax = frame.Components.Max(c => c.V);
        int mcu = 0;
        if (count == 1)
        {
            // Not interleaved: the MCU is one block, over the blocks of the component itself.
            var c = scan[0];
            int across = (c.Width + 7) / 8, down = (c.Height + 7) / 8;
            for (int by = 0; by < down; by++)
            {
                budget.ThrowIfCancelled();
                for (int bx = 0; bx < across; bx++)
                {
                    RestartIfDue(ref reader, scan, mcu++);
                    if (reader.Insufficient)
                    {
                        Grey(c, bx, by);
                    }
                    else
                    {
                        DecodeBlock(ref reader, c, block, bx, by);
                    }
                }
            }
        }
        else
        {
            int across = (frame.Width + 8 * hMax - 1) / (8 * hMax), down = (frame.Height + 8 * vMax - 1) / (8 * vMax);
            for (int my = 0; my < down; my++)
            {
                budget.ThrowIfCancelled();
                for (int mx = 0; mx < across; mx++)
                {
                    RestartIfDue(ref reader, scan, mcu++);
                    bool skip = reader.Insufficient;
                    foreach (var c in scan)
                    {
                        for (int v = 0; v < c.V; v++)
                        {
                            for (int h = 0; h < c.H; h++)
                            {
                                if (skip)
                                {
                                    Grey(c, mx * c.H + h, my * c.V + v);
                                }
                                else
                                {
                                    DecodeBlock(ref reader, c, block, mx * c.H + h, my * c.V + v);
                                }
                            }
                        }
                    }
                }
            }
        }

        frame.AdobeTransform = _adobeTransform;
        frame.Jfif = _jfif;
        return reader.Position;
    }

    private void RestartIfDue(ref JpegBitReader reader, JpegComponent[] scan, int mcu)
    {
        if (_restartInterval > 0 && mcu > 0 && mcu % _restartInterval == 0)
        {
            reader.Restart();
            _endOfBands = 0;
            foreach (var c in scan)
            {
                c.Prediction = 0;
            }
        }
    }

    /// <summary>A block without data: its coefficients are 0, which the IDCT gives as 128 (libjpeg skips such MCUs).</summary>
    private static void Grey(JpegComponent c, int bx, int by)
    {
        for (int r = 0; r < 8; r++)
        {
            c.Plane.AsSpan((by * 8 + r) * c.Stride + bx * 8, 8).Fill(128);
        }
    }

    /// <summary>Decodes block (<paramref name="bx"/>, <paramref name="by"/>) of a component (F.2.2) and transforms it into its plane.</summary>
    private void DecodeBlock(ref JpegBitReader reader, JpegComponent c, scoped Span<short> block, int bx, int by)
    {
        block.Clear();
        var dc = _dc[c.DcTable]!;
        var ac = _ac[c.AcTable]!;

        int size = dc.Decode(reader.Peek16(), out int length);
        reader.Skip(length);
        c.Prediction += reader.ReadDifference(size);
        block[0] = (short)c.Prediction;

        // AC: one lookup gives most coefficients whole; otherwise the code, then the bits of its value.
        ReadOnlySpan<byte> natural = Natural;
        ReadOnlySpan<ushort> lookup = ac.Lookup;
        ReadOnlySpan<int> fast = ac.FastAc;
        int k = 1;
        for (; k < 64; k++)
        {
            int next = reader.Peek16();
            int whole = fast[next >> (16 - HuffmanTable.LookupBits)];
            if (whole != 0)
            {
                k += (whole >> 4) & 15;
                block[natural[k]] = (short)(whole >> 16);
                reader.Skip(whole & 15);
                continue;
            }

            int entry = lookup[next >> (16 - HuffmanTable.LookupBits)];
            int rs;
            if (entry != 0)
            {
                length = entry >> 8;
                rs = entry & 0xFF;
            }
            else
            {
                rs = ac.DecodeLong(next, out length);
            }

            reader.Skip(length);
            int run = rs >> 4, bits = rs & 15;
            if (bits != 0)
            {
                k += run;
                block[natural[k]] = (short)reader.ReadDifference(bits);
            }
            else if (run == 15)
            {
                k += 15;
            }
            else
            {
                break;
            }
        }

        var quantization = _quantization[c.QuantizationTable]!;
        var output = c.Plane.AsSpan(by * 8 * c.Stride + bx * 8);
        if (k == 1 && block[1] == 0)
        {
            // The first AC code was the end of the block: only DC (a flat block, common in smooth areas).
            JpegIdct.TransformDcOnly(block[0] * quantization[0], output, c.Stride);
            return;
        }

        JpegIdct.Transform(block, quantization, output, c.Stride);
    }

    /// <summary>
    /// Decodes a scan of a progressive image (G.1.2): the first bits of the DC coefficients (possibly interleaved) or of a band of
    /// AC coefficients of one component, or one more bit of them. Returns where the scan ends.
    /// </summary>
    private int ReadProgressiveScan(ReadOnlySpan<byte> data, int start, JpegFrame frame, JpegComponent[] scan, int ss, int se, int ah, int al)
    {
        // The parameters libjpeg refuses (G.1.1.1.1).
        bool dc = ss == 0;
        if ((dc ? se != 0 : se < ss || se > 63 || scan.Length != 1) || (ah != 0 && al != ah - 1) || al > 13)
        {
            throw new InvalidDataException($"a progressive JPEG scan has the parameters Ss={ss}, Se={se}, Ah={ah}, Al={al}.");
        }

        // A scan must add bits to some coefficient: one that repeats what is known only costs time (a scan amplification).
        bool progress = false;
        foreach (var c in scan)
        {
            var known = c.KnownBits!;
            for (int k = ss; k <= se; k++)
            {
                progress |= ah == 0 ? known[k] < 0 : known[k] == ah;
                known[k] = al;
            }
        }

        if (!progress)
        {
            throw new InvalidDataException($"a progressive JPEG scan (Ss={ss}, Se={se}, Ah={ah}, Al={al}) refines no coefficient.");
        }

        var reader = new JpegBitReader(data, start);
        _endOfBands = 0;
        int mcu = 0;
        if (scan.Length == 1)
        {
            var c = scan[0];
            int across = (c.Width + 7) / 8, down = (c.Height + 7) / 8;
            for (int by = 0; by < down; by++)
            {
                budget.ThrowIfCancelled();
                for (int bx = 0; bx < across; bx++)
                {
                    RestartIfDue(ref reader, scan, mcu++);
                    if (!reader.Insufficient)
                    {
                        RefineBlock(ref reader, c, c.Coefficients.AsSpan((by * c.BlocksAcross + bx) * 64, 64), ss, se, ah, al);
                    }
                }
            }
        }
        else
        {
            int hMax = frame.Components.Max(c => c.H), vMax = frame.Components.Max(c => c.V);
            int across = (frame.Width + 8 * hMax - 1) / (8 * hMax), down = (frame.Height + 8 * vMax - 1) / (8 * vMax);
            for (int my = 0; my < down; my++)
            {
                budget.ThrowIfCancelled();
                for (int mx = 0; mx < across; mx++)
                {
                    RestartIfDue(ref reader, scan, mcu++);
                    if (reader.Insufficient)
                    {
                        continue;
                    }

                    foreach (var c in scan)
                    {
                        for (int v = 0; v < c.V; v++)
                        {
                            for (int h = 0; h < c.H; h++)
                            {
                                int block = (my * c.V + v) * c.BlocksAcross + mx * c.H + h;
                                RefineBlock(ref reader, c, c.Coefficients.AsSpan(block * 64, 64), ss, se, ah, al);
                            }
                        }
                    }
                }
            }
        }

        frame.AdobeTransform = _adobeTransform;
        frame.Jfif = _jfif;
        return reader.Position;
    }

    /// <summary>Decodes the part of a scan that belongs to one block (libjpeg's jdphuff.c, the four kinds of scan).</summary>
    private void RefineBlock(ref JpegBitReader reader, JpegComponent c, Span<short> block, int ss, int se, int ah, int al)
    {
        ReadOnlySpan<byte> natural = Natural;
        if (ss == 0)
        {
            if (ah == 0)
            {
                // DC, first bits: a difference from the prediction, scaled by 2^Al.
                int size = _dc[c.DcTable]!.Decode(reader.Peek16(), out int length);
                reader.Skip(length);
                c.Prediction += reader.ReadDifference(size);
                block[0] = (short)(c.Prediction << al);
            }
            else if (reader.Read(1) != 0)
            {
                // DC, one more bit.
                block[0] |= (short)(1 << al);
            }

            return;
        }

        var ac = _ac[c.AcTable]!;
        if (ah == 0)
        {
            // AC, first bits of the band: runs of zeros and values, or a run of whole bands that end here (EOBRUN).
            if (_endOfBands > 0)
            {
                _endOfBands--;
                return;
            }

            for (int k = ss; k <= se; k++)
            {
                int rs = ac.Decode(reader.Peek16(), out int length);
                reader.Skip(length);
                int run = rs >> 4, bits = rs & 15;
                if (bits != 0)
                {
                    k += run;
                    block[natural[k]] = (short)(reader.ReadDifference(bits) << al);
                }
                else if (run == 15)
                {
                    k += 15;
                }
                else
                {
                    _endOfBands = (1 << run) - 1 + (run > 0 ? reader.Read(run) : 0);
                    break;
                }
            }

            return;
        }

        // AC, one more bit: of the coefficients already non-zero (a correction bit each), and new ones of value +-2^Al.
        int plus = 1 << al, minus = -1 << al;
        int position = ss;
        if (_endOfBands == 0)
        {
            for (; position <= se; position++)
            {
                int rs = ac.Decode(reader.Peek16(), out int length);
                reader.Skip(length);
                int run = rs >> 4, bits = rs & 15;
                int value = 0;
                if (bits != 0)
                {
                    // The size of a new coefficient is 1 (libjpeg decodes another size the same way, with a warning).
                    value = reader.Read(1) != 0 ? plus : minus;
                }
                else if (run != 15)
                {
                    _endOfBands = (1 << run) + (run > 0 ? reader.Read(run) : 0);
                    break;
                }

                // Skip `run` zero coefficients, correcting the non-zero ones on the way; the new value goes after them.
                do
                {
                    ref short coefficient = ref block[natural[position]];
                    if (coefficient != 0)
                    {
                        Correct(ref reader, ref coefficient, plus, minus);
                    }
                    else if (--run < 0)
                    {
                        break;
                    }

                    position++;
                }
                while (position <= se);

                if (value != 0)
                {
                    block[natural[position]] = (short)value;
                }
            }
        }

        if (_endOfBands > 0)
        {
            // The band ends in this block: only correction bits for the non-zero coefficients left.
            for (; position <= se; position++)
            {
                ref short coefficient = ref block[natural[position]];
                if (coefficient != 0)
                {
                    Correct(ref reader, ref coefficient, plus, minus);
                }
            }

            _endOfBands--;
        }
    }

    /// <summary>One correction bit of a non-zero coefficient: adds 2^Al away from zero when the bit is set and not there yet.</summary>
    private static void Correct(ref JpegBitReader reader, ref short coefficient, int plus, int minus)
    {
        if (reader.Read(1) != 0 && (coefficient & plus) == 0)
        {
            coefficient += (short)(coefficient >= 0 ? plus : minus);
        }
    }
}
