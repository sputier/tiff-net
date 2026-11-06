using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using BenchmarkDotNet.Attributes;
using Eleris.TiffNet.Internal.Codecs;

namespace Eleris.TiffNet.Benchmarks;

/// <summary>
/// The LZW decoder of the library against the prototype of the review (research/review-2026-11/performance/bench/Kernels.cs),
/// on a strip of 8 MB: 16-bit samples of a smooth image with noise (an astronomical frame) and a flat one (long strings).
/// </summary>
[MemoryDiagnoser]
public class LzwBenchmarks
{
    private byte[] _encoded = [];
    private byte[] _decoded = [];
    private byte[] _output = [];

    [Params("noisy", "flat")]
    public string Image { get; set; } = "noisy";

    [GlobalSetup]
    public void Setup()
    {
        var random = new Random(1);
        var samples = new ushort[2048 * 2048];
        for (int i = 0; i < samples.Length; i++)
        {
            int x = i % 2048, y = i / 2048;
            samples[i] = Image == "flat" ? (ushort)(1000 + y / 64) : (ushort)(1000 + x / 4 + y / 8 + random.Next(16));
        }

        byte[] data = MemoryMarshal.AsBytes(samples.AsSpan()).ToArray();
        _encoded = Encoder.Encode(data);
        _decoded = new byte[data.Length];
        _output = new byte[LzwEncoder.MaxEncoded(data.Length)];
        if (Library() != data.Length || !_decoded.AsSpan().SequenceEqual(data) || Prototype() != data.Length)
        {
            throw new InvalidOperationException("The decoders do not give the data back.");
        }
    }

    [Benchmark(Baseline = true)]
    public int Prototype() => PrototypeLzw.Decode(_encoded, _decoded);

    [Benchmark]
    public int Library() => Lzw.Decode(_encoded, _decoded, out _);

    /// <summary>The encoder of the library on the same data (what a write costs besides the predictor).</summary>
    [Benchmark]
    public int LibraryEncode() => LzwEncoder.Encode(_decoded, _output);

    /// <summary>The prototype of the review, unchanged.</summary>
    private static class PrototypeLzw
    {
        public static int Decode(ReadOnlySpan<byte> src, Span<byte> dst)
        {
            const int Clear = 256, Eoi = 257;
            Span<int> start = stackalloc int[4096];
            Span<short> len = stackalloc short[4096];
            ulong bits = 0;
            int nbits = 0, pos = 0, o = 0, codeBits = 9, free = 258, old = -1;
            while (true)
            {
                while (nbits <= 56 && pos < src.Length) { bits |= (ulong)src[pos++] << (56 - nbits); nbits += 8; }
                if (nbits < codeBits) break;
                int code = (int)(bits >> (64 - codeBits));
                bits <<= codeBits; nbits -= codeBits;
                if (code == Clear) { free = 258; codeBits = 9; old = -1; continue; }
                if (code == Eoi) break;
                if (old == -1)
                {
                    if (code > 255 || o >= dst.Length) break;
                    start[code] = o; len[code] = 1; dst[o++] = (byte)code; old = code; continue;
                }

                int s, l;
                int oldLen = old < 256 ? 1 : len[old];
                int oldStart = o - oldLen;
                if (code < 256) { s = -1; l = 1; }
                else if (code < free) { s = start[code]; l = len[code]; }
                else if (code == free) { s = oldStart; l = oldLen + 1; }
                else return -1;

                if (free < 4096) { start[free] = oldStart; len[free] = (short)(oldLen + 1); }
                if (code < 256) { if (o >= dst.Length) break; dst[o++] = (byte)code; }
                else
                {
                    if (o + l > dst.Length) l = dst.Length - o;
                    if (s + l <= o && l > 16) dst.Slice(s, l).CopyTo(dst.Slice(o));
                    else { ref byte d = ref MemoryMarshal.GetReference(dst); for (int k = 0; k < l; k++) Unsafe.Add(ref d, o + k) = Unsafe.Add(ref d, s + k); }
                    o += l;
                }
                if (++free >= (1 << codeBits) - 1 && codeBits < 12) codeBits++;
                old = code;
                if (o >= dst.Length) break;
            }
            return o;
        }
    }

    /// <summary>A TIFF 6.0 LZW encoder (most significant bit first, width growing one code early), clearing a full table.</summary>
    private static class Encoder
    {
        public static byte[] Encode(ReadOnlySpan<byte> data)
        {
            var output = new List<byte>(data.Length);
            ulong bits = 0;
            int count = 0, width = 9;
            void Put(int code)
            {
                bits = (bits << width) | (uint)code;
                count += width;
                while (count >= 8)
                {
                    output.Add((byte)(bits >> (count - 8)));
                    count -= 8;
                }
            }

            var table = new Dictionary<int, int>();
            int free = 258, current = -1;
            Put(256);
            foreach (byte b in data)
            {
                if (current == -1)
                {
                    current = b;
                    continue;
                }

                if (table.TryGetValue(current << 8 | b, out int code))
                {
                    current = code;
                    continue;
                }

                Put(current);
                table[current << 8 | b] = free++;
                current = b;
                if (free >= 1 << width && width < 12)
                {
                    width++;
                }

                if (free == 4094)
                {
                    Put(256);
                    table.Clear();
                    (free, width) = (258, 9);
                }
            }

            Put(current);
            if (++free >= 1 << width && width < 12)
            {
                width++;
            }

            Put(257);
            if (count > 0)
            {
                output.Add((byte)(bits << (8 - count)));
            }

            return [.. output];
        }
    }
}
