using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace Eleris.TiffNet.Internal.Codecs;

/// <summary>A decoder that gives its bytes as a stream, read in order (the base of <see cref="LzwStream"/> and <see cref="PackBitsStream"/>).</summary>
internal abstract class DecodingStream(Stream input) : Stream
{
    private readonly byte[] _input = new byte[InputBytes];
    private int _position, _length;
    private bool _inputEnded;

    /// <summary>The compressed bytes read at once from the input.</summary>
    protected const int InputBytes = 64 * 1024;

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    /// <summary>The compressed bytes read and not consumed yet (empty at the end of the input once <see cref="Refill"/> says so).</summary>
    protected ReadOnlySpan<byte> Pending => _input.AsSpan(_position, _length - _position);

    /// <summary>Consumes <paramref name="count"/> bytes of <see cref="Pending"/>.</summary>
    protected void Consume(int count) => _position += count;

    /// <summary>Whether compressed bytes are pending, reading the next block of the input when none is.</summary>
    protected bool Refill()
    {
        if (_position < _length)
        {
            return true;
        }

        if (_inputEnded)
        {
            return false;
        }

        _length = input.ReadAtLeast(_input, _input.Length, throwOnEndOfStream: false);
        _position = 0;
        _inputEnded = _length < _input.Length;
        return _length > 0;
    }

    /// <summary>Reads until at least <paramref name="count"/> bytes are pending (when the input holds them), keeping those pending.</summary>
    protected void Ensure(int count)
    {
        if (_length - _position >= count || _inputEnded)
        {
            return;
        }

        _input.AsSpan(_position, _length - _position).CopyTo(_input);
        _length -= _position;
        _position = 0;
        int got = input.ReadAtLeast(_input.AsSpan(_length), _input.Length - _length, throwOnEndOfStream: false);
        _inputEnded = got < _input.Length - _length;
        _length += got;
    }

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public abstract override int Read(Span<byte> buffer);

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            input.Dispose();
        }

        base.Dispose(disposing);
    }
}

/// <summary>
/// PackBits (TIFF 6.0, section 9) decoded as a stream: a run cut by a read resumes at the next. A run cut by the end of the data
/// gives what there is, as <see cref="PackBits.Decode"/> does.
/// </summary>
/// <remarks>
/// One difference with the whole decoder, for a strict read only: data that ends with the header of a run and nothing of its
/// bytes is not seen as data beyond the block (it decodes to nothing), where <see cref="PackBits.Decode"/> counts it as such.
/// </remarks>
internal sealed class PackBitsStream(Stream input) : DecodingStream(input)
{
    private int _literal;   // literal bytes of the run left to copy
    private int _repeat;    // copies of _value left to give
    private byte _value;
    private bool _ended;

    public override int Read(Span<byte> buffer)
    {
        int total = 0;
        while (total < buffer.Length)
        {
            if (_repeat > 0)
            {
                int n = Math.Min(_repeat, buffer.Length - total);
                buffer.Slice(total, n).Fill(_value);
                _repeat -= n;
                total += n;
                continue;
            }

            if (_ended || !Refill())
            {
                _ended = true;
                break;
            }

            if (_literal > 0)
            {
                var pending = Pending;
                int n = Math.Min(Math.Min(_literal, buffer.Length - total), pending.Length);
                pending[..n].CopyTo(buffer[total..]);
                Consume(n);
                _literal -= n;
                total += n;
                continue;
            }

            sbyte header = (sbyte)Pending[0];
            Consume(1);
            if (header >= 0)
            {
                _literal = header + 1;
            }
            else if (header != -128)
            {
                // 1 - header copies of the next byte; the data may end before it.
                if (!Refill())
                {
                    _ended = true;
                    break;
                }

                (_value, _repeat) = (Pending[0], 1 - header);
                Consume(1);
            }

            // -128 is a no-operation.
        }

        return total;
    }
}

/// <summary>
/// LZW (TIFF 6.0, section 13, and the old style of libtiff, told apart by the first bytes) decoded as a stream, for blocks too
/// large to decode whole. As <see cref="Lzw.Decode"/>, an entry of the table is a string already decoded (its position and
/// length in the output of the generation, since the last clear code): the output of a generation is kept. Before the table is
/// full it is at most <see cref="MaxHistory"/> bytes (the k-th code after a clear gives at most k + 1 bytes), which flat data
/// reaches; once the table is full no entry is added, what follows refers only to that output, and each string decoded after it
/// is given, then dropped.
/// </summary>
internal sealed class LzwStream(Stream input) : DecodingStream(input)
{
    private const int Clear = 256, End = 257, FirstFree = 258, MaxCodes = 4096;

    /// <summary>The output decoded ahead of the reader at most, codes decoded at once before they are given.</summary>
    private const int Batch = 1 << 20;

    /// <summary>
    /// The most output a generation keeps: the first byte, then 3 838 codes of at most 2 to 3 839 bytes until the table is full
    /// (about 7.4 MB); what is decoded ahead of the reader after it, at most a batch and a string; and, before it, what the
    /// previous generation left undelivered when a clear code came, also at most a batch and a string (9.5 MB in all).
    /// </summary>
    internal const int MaxHistory = 1 + (MaxCodes - FirstFree) * (MaxCodes - FirstFree + 1) / 2 + (MaxCodes - FirstFree) + 2 * (Batch + MaxCodes);

    /// <summary>The memory of the stream: its output, its table and the buffer of its input.</summary>
    internal const int Memory = MaxHistory + MaxCodes * 6 + InputBytes;

    private readonly int[] _start = new int[MaxCodes];
    private readonly short[] _length = new short[MaxCodes];
    private readonly byte[] _history = GC.AllocateUninitializedArray<byte>(MaxHistory);
    private bool? _old;
    private ulong _bits;
    private int _count;
    private int _width = 9, _free = FirstFree, _previous = -1, _previousLength;

    /// <summary>The output of the generation decoded so far; the bytes from <see cref="_delivered"/> are not given yet.</summary>
    private int _written, _delivered;

    /// <summary>Once the table is full, the end of the output its entries refer to (-1 before).</summary>
    private int _frozen = -1;
    private bool _ended;

    public override int Read(Span<byte> buffer)
    {
        int total = 0;
        while (total < buffer.Length)
        {
            if (_delivered < _written)
            {
                int n = Math.Min(_written - _delivered, buffer.Length - total);
                _history.AsSpan(_delivered, n).CopyTo(buffer[total..]);
                _delivered += n;
                total += n;
                continue;
            }

            if (_frozen >= 0)
            {
                // Everything given: the strings after the full table are dropped, the output its entries refer to stays.
                _written = _delivered = _frozen;
            }

            if (_ended)
            {
                break;
            }

            // Codes decoded at once until they give what the reader wants (at most a batch), then given together.
            _ended = !DecodeBatch(Math.Min(buffer.Length - total, Batch));
        }

        return total;
    }

    /// <summary>
    /// Decodes codes into the output until <paramref name="want"/> bytes are there to give; false at the end of the data (an end
    /// code, or no more bits). The state is in locals while the codes are decoded, as in <see cref="Lzw.Decode"/>.
    /// </summary>
    private bool DecodeBatch(int want)
    {
        if (_old is not { } old)
        {
            // Old-style data starts with a clear code written least significant bit first: 0x00, then a byte whose low bit is set.
            Ensure(2);
            var first = Pending;
            old = first.Length >= 2 && first[0] == 0 && (first[1] & 1) != 0;
            _old = old;
        }

        ulong bits = _bits;
        int count = _count, width = _width, free = _free, previous = _previous, previousLength = _previousLength;
        int written = _written, delivered = _delivered, frozen = _frozen;
        int early = old ? 0 : 1;
        byte[] history = _history;
        ref byte output = ref MemoryMarshal.GetArrayDataReference(history);
        bool more = true;

        // The compressed bytes pending, consumed from a local position (given back to the buffer when it is refilled, and at the end).
        ReadOnlySpan<byte> input = Pending;
        int position = 0;
        try
        {
            while (written - delivered < want)
            {
                if (count <= 56)
                {
                    if (!old && position + 8 <= input.Length)
                    {
                        // The next 8 bytes at once, below the bits waiting (as Lzw.Decode refills).
                        bits |= BinaryPrimitives.ReadUInt64BigEndian(input[position..]) >> count;
                        position += (63 - count) >> 3;
                        count |= 56;
                    }
                    else
                    {
                        while (count <= 56)
                        {
                            if (position == input.Length)
                            {
                                Consume(position);
                                position = 0;
                                if (!Refill())
                                {
                                    input = default;
                                    break;
                                }

                                input = Pending;
                            }

                            byte value = input[position++];
                            bits = old ? bits | ((ulong)value << count) : bits | ((ulong)value << (56 - count));
                            count += 8;
                        }
                    }
                }

                if (count < width)
                {
                    more = false;   // the data ends without an end code, as some writers do
                    break;
                }

                int code = old ? (int)(bits & ((1UL << width) - 1)) : (int)(bits >> (64 - width));
                bits = old ? bits >> width : bits << width;
                count -= width;

                if (code == Clear)
                {
                    // A new generation: its output starts again, after the bytes of the batch not given yet.
                    free = FirstFree;
                    width = 9;
                    previous = -1;
                    frozen = -1;
                    history.AsSpan(delivered, written - delivered).CopyTo(history);
                    written -= delivered;
                    delivered = 0;
                    continue;
                }

                if (code == End)
                {
                    more = false;
                    break;
                }

                if (previous == -1)
                {
                    if (code > 255)
                    {
                        throw new InvalidDataException($"LZW code {code} follows a clear code; a byte (0 to 255) is expected.");
                    }

                    Room(written, 1);
                    history[written++] = (byte)code;
                    previous = code;
                    previousLength = 1;
                    continue;
                }

                int previousStart = written - previousLength;
                int from, size;
                if (code < 256)
                {
                    from = -1;
                    size = 1;
                }
                else if (code < free)
                {
                    from = _start[code];
                    size = _length[code];
                }
                else if (code == free)
                {
                    from = previousStart;
                    size = previousLength + 1;
                }
                else
                {
                    throw new InvalidDataException($"LZW code {code} is not defined yet (the next code is {free}).");
                }

                if (free < MaxCodes)
                {
                    _start[free] = previousStart;
                    _length[free] = (short)(previousLength + 1);
                }

                Room(written, size);
                if (code < 256)
                {
                    history[written] = (byte)code;
                }
                else if (size <= 16 && history.Length - written >= 16)
                {
                    // A short string: one 16-byte copy (the bytes after it are written again later), its last byte set when it is
                    // the string being defined, as Lzw.Decode copies it.
                    Unsafe.WriteUnaligned(ref Unsafe.Add(ref output, written), Unsafe.ReadUnaligned<Vector128<byte>>(ref Unsafe.Add(ref output, from)));
                    if (from + size == written + 1)
                    {
                        history[written + size - 1] = history[from];
                    }
                }
                else if (from + size == written + 1)
                {
                    // The string being defined: the previous string, then its own first byte.
                    history.AsSpan(from, size - 1).CopyTo(history.AsSpan(written));
                    history[written + size - 1] = history[from];
                }
                else
                {
                    history.AsSpan(from, size).CopyTo(history.AsSpan(written));
                }

                written += size;
                if (free < MaxCodes)
                {
                    // A full table stays full: the next code stays 4096, which no 12-bit code is.
                    if (++free >= (1 << width) - early && width < 12)
                    {
                        width++;
                    }

                    if (free == MaxCodes)
                    {
                        // Its entries refer to the output so far: nothing after it will be referred to.
                        frozen = written;
                    }
                }

                previous = code;
                previousLength = size;
            }
        }
        finally
        {
            Consume(position);
            (_bits, _count, _width, _free, _previous, _previousLength) = (bits, count, width, free, previous, previousLength);
            (_written, _delivered, _frozen) = (written, delivered, frozen);
        }

        return more;
    }

    /// <summary>Checks that <paramref name="size"/> more bytes of output fit after <paramref name="written"/> (they do for every valid stream: see <see cref="MaxHistory"/>).</summary>
    private void Room(int written, int size)
    {
        if (written + size > _history.Length)
        {
            throw new InvalidDataException("an LZW generation decodes to more than its table can refer to.");
        }
    }
}
