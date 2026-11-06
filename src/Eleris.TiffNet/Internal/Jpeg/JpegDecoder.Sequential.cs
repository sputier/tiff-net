using System.Buffers.Binary;

namespace Eleris.TiffNet.Internal.Jpeg;

/// <summary>The scans of a sequential frame: the blocks decoded and transformed as they come.</summary>
internal sealed partial class JpegDecoder
{
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
            if (frame.Progressive)
            {
                // Latched at the first scan of the component: a progressive image is transformed after its last scan.
                component.Quantization ??= (ushort[])_quantization[component.QuantizationTable]!.Clone();
            }
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
}
