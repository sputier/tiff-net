using System.Buffers.Binary;

namespace Eleris.TiffNet.Internal.Jpeg;

/// <summary>The segments of a stream before its scans: tables, restart interval, Adobe marker, frame.</summary>
internal sealed partial class JpegDecoder
{
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
            if (tables[id] is null || Array.IndexOf(_shared, tables[id]) >= 0)
            {
                tables[id] = new HuffmanTable();
            }

            tables[id]!.Define(counts, s.Slice(17, total), dc: kind == 0);
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
                KnownBits = progressive ? Unknown() : null,
            };
        }

        _frame = new JpegFrame { Width = width, Height = height, Components = components, Progressive = progressive };
    }
}
