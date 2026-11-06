using System.Buffers.Binary;

namespace Eleris.TiffNet.Internal.Jpeg;

/// <summary>The scans of a progressive frame: the coefficients refined scan after scan.</summary>
internal sealed partial class JpegDecoder
{
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
