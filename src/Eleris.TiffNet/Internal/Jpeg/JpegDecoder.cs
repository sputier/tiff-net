using System.Buffers.Binary;

namespace Eleris.TiffNet.Internal.Jpeg;

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
internal sealed partial class JpegDecoder(ResourceBudget budget, int maxWidth = ushort.MaxValue, int maxHeight = ushort.MaxValue)
{
    /// <summary>A decoder that starts from tables read once (TIFF's JPEGTables), shared with the decoders of the other blocks.</summary>
    /// <remarks>A table the stream redefines replaces the shared one in this decoder only: the shared tables are never changed.</remarks>
    public JpegDecoder(JpegTables tables, ResourceBudget budget, int maxWidth, int maxHeight)
        : this(budget, maxWidth, maxHeight)
    {
        Array.Copy(tables.Quantization, _quantization, 4);
        Array.Copy(tables.Dc, _dc, 4);
        Array.Copy(tables.Ac, _ac, 4);
        foreach (var table in tables.Dc.Concat(tables.Ac))
        {
            if (table is not null)
            {
                _shared.Add(table);
            }
        }
    }

    /// <summary>
    /// The Huffman tables shared with other decoders (not to be redefined in place): those of JPEGTables and of the cache, at
    /// most 8 + <see cref="HuffmanCache.Capacity"/> whatever the number of DHT segments.
    /// </summary>
    private readonly HashSet<HuffmanTable> _shared = new(ReferenceEqualityComparer.Instance);

    /// <summary>The tables built for the other blocks of the read, shared by its decoders (null: none).</summary>
    public HuffmanCache? Cache { get; init; }

    /// <summary>
    /// Where the rows of a grey sequential frame go (the rows of the block, <see cref="GreyStride"/> bytes each): the plane itself
    /// when its rows have that stride and the buffer holds its padded rows, so that its blocks are transformed in place. The
    /// buffer is asked for only then, once the frame is read.
    /// </summary>
    public IOutputBuffer? GreyOutput { get; init; }

    /// <summary>The bytes of a row of <see cref="GreyOutput"/>.</summary>
    public int GreyStride { get; init; }

    /// <summary>The largest plane taken from the pool: the planes of strips and tiles, decoded block after block.</summary>
    private const int MaxRented = 1 << 20;

    /// <summary>The planes rented from the shared pool, given back by <see cref="Release"/>.</summary>
    private readonly List<byte[]> _rented = [];

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
        foreach (byte[] plane in _rented)
        {
            System.Buffers.ArrayPool<byte>.Shared.Return(plane);
        }

        _rented.Clear();
    }

    /// <summary>
    /// A plane of <paramref name="bytes"/> bytes, from the shared pool up to <see cref="MaxRented"/> bytes (a block of a large
    /// image would otherwise allocate its planes on the large object heap, block after block), cleared when <paramref name="clear"/>: a plane whose every block is decoded,
    /// filled or transformed needs no clearing.
    /// </summary>
    private byte[] RentPlane(int bytes, bool clear)
    {
        if (bytes > MaxRented)
        {
            // A plane of a whole large image: allocated as it is (the pool would round it up and keep it after the read).
            return new byte[bytes];
        }

        byte[] plane = System.Buffers.ArrayPool<byte>.Shared.Rent(bytes);
        _rented.Add(plane);
        if (clear)
        {
            plane.AsSpan(0, bytes).Clear();
        }

        return plane;
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
                // Counted with the frame: allocated only now, so that a stream refused in its scans never costs its planes. Every
                // block of it is transformed or filled below.
                c.Plane = RentPlane(c.Stride * c.PlaneRows, clear: false);
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
                        var block = coefficients.AsSpan((by * across + bx) * 64, 64);
                        var output = c.Plane.AsSpan(by * 8 * c.Stride + bx * 8);
                        if (block[1..].IndexOfAnyExcept((short)0) < 0)
                        {
                            // DC only (all the blocks of a stream that ends early, most of a smooth image): a fill.
                            JpegIdct.TransformDcOnly(block[0] * quantization[0], output, c.Stride);
                        }
                        else
                        {
                            JpegIdct.Transform(block, quantization, output, c.Stride);
                        }
                    }
                }
            }
            else if (!c.Scanned)
            {
                // libjpeg's coefficients of a component without data are 0, which the IDCT turns into 128.
                c.Plane.AsSpan(0, c.Stride * c.PlaneRows).Fill(128);
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

    /// <summary>The known bits of the 64 coefficients of a progressive component before its first scan: none.</summary>
    private static int[] Unknown()
    {
        var known = new int[64];
        Array.Fill(known, -1);
        return known;
    }

    /// <summary>The tables of the decoder, to share with the decoders of other blocks (see <see cref="JpegTables"/>).</summary>
    internal JpegTables Tables()
    {
        foreach (var table in _dc.Concat(_ac))
        {
            table?.Build();
        }

        return new JpegTables([.. _quantization], [.. _dc], [.. _ac]);
    }
}
