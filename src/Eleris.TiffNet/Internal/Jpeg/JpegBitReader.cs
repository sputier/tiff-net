using System.Buffers.Binary;
using System.Runtime.CompilerServices;

namespace Eleris.TiffNet.Internal.Jpeg;

/// <summary>
/// Reads the entropy-coded data of a scan, most significant bit first (F.2.2.5): a 0xFF byte is followed by a stuffed 0x00, and
/// a marker ends the data. Past a marker or the end of the data the reader gives zeros and says that the data is insufficient,
/// as libjpeg does: the MCU being decoded ends with zeros, and the following ones are not decoded (see <see cref="Insufficient"/>).
/// </summary>
internal ref struct JpegBitReader
{
    private readonly ReadOnlySpan<byte> _data;
    private int _position;
    private ulong _bits;
    private int _count;
    private bool _ended;

    public JpegBitReader(ReadOnlySpan<byte> data, int position)
    {
        _data = data;
        _position = position;
    }

    /// <summary>Where the reading stopped: at the marker that ended the data, or after the last byte read.</summary>
    public readonly int Position => _position;

    /// <summary>
    /// Whether bits beyond the data were consumed (libjpeg's <c>insufficient_data</c>): the data of the scan is exhausted until
    /// the next restart marker.
    /// </summary>
    /// <remarks>
    /// The count of bits goes below zero only once the data has ended (until then a refill keeps 56 bits or more ahead), and stays
    /// there: no test on each consumption, the count tells.
    /// </remarks>
    public readonly bool Insufficient => _count < 0;

    /// <summary>The next 16 bits, without consuming them.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int Peek16()
    {
        if (_count < 16)
        {
            Fill();
        }

        return (int)(_bits >> 48);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Skip(int bits)
    {
        _bits <<= bits;
        _count -= bits;
    }

    /// <summary>The next <paramref name="bits"/> bits (1 to 16) as an unsigned value.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int Read(int bits)
    {
        if (_count < bits)
        {
            Fill();
        }

        int value = (int)(_bits >> (64 - bits));
        Skip(bits);
        return value;
    }

    /// <summary>
    /// A difference of <paramref name="size"/> bits (F.2.2.1, EXTEND): the bits as they are when the first is 1, otherwise
    /// minus their complement.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int ReadDifference(int size)
    {
        if (size == 0)
        {
            return 0;
        }

        int value = Read(size);
        return value < 1 << (size - 1) ? value - (1 << size) + 1 : value;
    }

    /// <summary>
    /// Goes to the next restart marker (after <c>DRI</c> MCUs): the bits left in the byte are dropped, and an RSTn marker is
    /// consumed. Anything else is left for the marker parser, and the scan goes on with zeros.
    /// </summary>
    public void Restart()
    {
        // Without a restart marker the scan stays as it was: exhausted if it was.
        bool insufficient = Insufficient;
        _bits = 0;
        _count = 0;
        if (!_ended)
        {
            // Skip what is left before the marker (garbage, which libjpeg skips with a warning).
            while (_position + 1 < _data.Length && !(_data[_position] == 0xFF && _data[_position + 1] is not (0x00 or 0xFF)))
            {
                _position++;
            }
        }

        if (_position + 1 < _data.Length && _data[_position] == 0xFF && _data[_position + 1] is >= 0xD0 and <= 0xD7)
        {
            // The data goes on: decoding resumes, even after an exhausted interval.
            _position += 2;
            _ended = false;
            return;
        }

        _ended = true;
        _count = insufficient ? -1 : 0;
    }

    private void Fill()
    {
        // Fast path: 8 bytes at once when none of them is 0xFF (no stuffing, no marker). The whole bytes that fit are taken; the
        // bits of the next one that come along are those it will bring again, so OR-ing it later changes nothing.
        if (_count <= 48 && _position + 8 <= _data.Length && !_ended)
        {
            ulong next = BinaryPrimitives.ReadUInt64BigEndian(_data[_position..]);
            ulong inverted = ~next;
            if (((inverted - 0x0101010101010101UL) & ~inverted & 0x8080808080808080UL) == 0)
            {
                _bits |= next >> _count;
                _position += (63 - _count) >> 3;
                _count |= 56;
                return;
            }
        }

        while (_count <= 56)
        {
            if (_ended)
            {
                // Zeros: the bits below the valid ones are already 0; consuming them marks the data as insufficient.
                return;
            }

            if (_position >= _data.Length)
            {
                _ended = true;
                continue;
            }

            byte b = _data[_position];
            if (b == 0xFF)
            {
                if (_position + 1 >= _data.Length)
                {
                    _ended = true;
                    continue;
                }

                byte next = _data[_position + 1];
                if (next == 0xFF)
                {
                    // Fill bytes before a marker.
                    _position++;
                    continue;
                }

                if (next != 0x00)
                {
                    // A marker: the data of the scan ends here.
                    _ended = true;
                    continue;
                }

                _position += 2;
            }
            else
            {
                _position++;
            }

            _bits |= (ulong)b << (56 - _count);
            _count += 8;
        }
    }
}
