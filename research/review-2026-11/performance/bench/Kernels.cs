using System.Buffers;
using System.Buffers.Binary;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using BitMiracle.LibTiff.Classic;

namespace Bench;

/// <summary>Original LibTiff.Net kernels, copied verbatim (logic) from LibTiff/Internal/CodecWithPredictor.cs and Tiff_Read.cs.</summary>
public static class Original
{
    public static void HorAcc16(byte[] buffer, int offset, int count, int stride)
    {
        short[] wBuffer = Tiff.ByteArrayToShorts(buffer, offset, count);
        int wOffset = 0;
        int wCount = count / 2;
        if (wCount > stride)
        {
            wCount -= stride;
            do
            {
                for (int i = stride; i > 0; i--) { wBuffer[wOffset + stride] += wBuffer[wOffset]; wOffset++; }
                wCount -= stride;
            }
            while (wCount > 0);
        }
        Tiff.ShortsToByteArray(wBuffer, 0, count / 2, buffer, offset);
    }

    public static void HorDiff16(byte[] buffer, int offset, int count, int stride)
    {
        short[] wBuffer = Tiff.ByteArrayToShorts(buffer, offset, count);
        int wOffset = 0;
        int wCount = count / 2;
        if (wCount > stride)
        {
            wCount -= stride;
            wOffset += wCount - 1;
            do
            {
                for (int i = stride; i > 0; i--) { wBuffer[wOffset + stride] -= wBuffer[wOffset]; wOffset--; }
                wCount -= stride;
            }
            while (wCount > 0);
        }
        Tiff.ShortsToByteArray(wBuffer, 0, count / 2, buffer, offset);
    }

    public static void FpAcc(byte[] buffer, int offset, int count, int stride, int bps)
    {
        int wCount = count / bps;
        int left = count;
        int cp = offset;
        while (left > stride)
        {
            for (int i = stride; i > 0; i--) { buffer[cp + stride] += buffer[cp]; cp++; }
            left -= stride;
        }
        byte[] tmp = new byte[count];
        Buffer.BlockCopy(buffer, offset, tmp, 0, count);
        for (int i = 0; i < wCount; i++)
            for (int b = 0; b < bps; b++)
                buffer[offset + bps * i + b] = tmp[(bps - b - 1) * wCount + i];
    }

    public static void FpDiff(byte[] buffer, int offset, int count, int stride, int bps)
    {
        byte[] tmp = new byte[count];
        Buffer.BlockCopy(buffer, offset, tmp, 0, count);
        int wCount = count / bps;
        for (int c = 0; c < wCount; c++)
            for (int b = 0; b < bps; b++)
                buffer[offset + (bps - b - 1) * wCount + c] = tmp[bps * c + b];
        int cp = offset + count - stride - 1;
        for (int c = count; c > stride; c -= stride)
            for (int i = stride; i > 0; i--) { buffer[cp + stride] -= buffer[cp]; cp--; }
    }

    public static void Swab16(byte[] buffer, int offset, int count)
    {
        short[] swabee = Tiff.ByteArrayToShorts(buffer, offset, count);
        Tiff.SwabArrayOfShort(swabee, count / 2);
        Tiff.ShortsToByteArray(swabee, 0, count / 2, buffer, offset);
    }

    public static void Scatter(byte[] source, long sourceOffset, long pixels, byte[] target, long targetOffset, int size, int stride)
    {
        var src = source.AsSpan((int)sourceOffset, (int)(pixels * size));
        var dst = target.AsSpan((int)targetOffset);
        for (int i = 0, t = 0; i < src.Length; i += size, t += stride)
            src.Slice(i, size).CopyTo(dst.Slice(t, size));
    }
}

/// <summary>Proposed replacements: Span, no allocation, no bounds checks in the inner loops.</summary>
public static class Fast
{
    public static void HorAcc16(Span<byte> row, int stride)
    {
        Span<ushort> s = MemoryMarshal.Cast<byte, ushort>(row);
        ref ushort r = ref MemoryMarshal.GetReference(s);
        int n = s.Length;
        if (stride == 1)
        {
            ushort acc = r;
            for (int i = 1; i < n; i++) { acc += Unsafe.Add(ref r, i); Unsafe.Add(ref r, i) = acc; }
        }
        else
        {
            for (int i = stride; i < n; i++) Unsafe.Add(ref r, i) += Unsafe.Add(ref r, i - stride);
        }
    }

    public static void HorDiff16(ReadOnlySpan<byte> srcRow, Span<byte> dstRow, int stride)
    {
        ReadOnlySpan<ushort> s = MemoryMarshal.Cast<byte, ushort>(srcRow);
        Span<ushort> d = MemoryMarshal.Cast<byte, ushort>(dstRow);
        s[..stride].CopyTo(d);
        // Vectorizable: d[i] = s[i] - s[i - stride], no loop-carried dependency.
        int i = stride;
        if (System.Numerics.Vector.IsHardwareAccelerated)
        {
            int vs = System.Numerics.Vector<ushort>.Count;
            for (; i + vs <= s.Length; i += vs)
                (new System.Numerics.Vector<ushort>(s[i..]) - new System.Numerics.Vector<ushort>(s[(i - stride)..])).CopyTo(d[i..]);
        }
        for (; i < s.Length; i++) d[i] = (ushort)(s[i] - s[i - stride]);
    }

    /// <summary>Floating point predictor undo for one row, in place; tmp is a reusable scratch of at least row.Length.</summary>
    public static void FpAcc32(Span<byte> row, int stride, Span<byte> tmp)
    {
        int count = row.Length;
        ref byte r = ref MemoryMarshal.GetReference(row);
        for (int i = stride; i < count; i++) Unsafe.Add(ref r, i) += Unsafe.Add(ref r, i - stride);
        row.CopyTo(tmp);
        int w = count / 4;
        ref byte p0 = ref MemoryMarshal.GetReference(tmp);          // most significant bytes
        ref byte p1 = ref Unsafe.Add(ref p0, w);
        ref byte p2 = ref Unsafe.Add(ref p0, 2 * w);
        ref byte p3 = ref Unsafe.Add(ref p0, 3 * w);
        ref uint dst = ref Unsafe.As<byte, uint>(ref r);
        for (int i = 0; i < w; i++)
        {
            Unsafe.Add(ref dst, i) = (uint)Unsafe.Add(ref p0, i) << 24 | (uint)Unsafe.Add(ref p1, i) << 16
                                   | (uint)Unsafe.Add(ref p2, i) << 8 | Unsafe.Add(ref p3, i);
        }
    }

    public static void FpDiff32(ReadOnlySpan<byte> srcRow, Span<byte> dstRow, int stride)
    {
        int count = srcRow.Length;
        int w = count / 4;
        ref uint src = ref Unsafe.As<byte, uint>(ref MemoryMarshal.GetReference(srcRow));
        ref byte p0 = ref MemoryMarshal.GetReference(dstRow);
        ref byte p1 = ref Unsafe.Add(ref p0, w);
        ref byte p2 = ref Unsafe.Add(ref p0, 2 * w);
        ref byte p3 = ref Unsafe.Add(ref p0, 3 * w);
        for (int i = 0; i < w; i++)
        {
            uint v = Unsafe.Add(ref src, i);
            Unsafe.Add(ref p0, i) = (byte)(v >> 24); Unsafe.Add(ref p1, i) = (byte)(v >> 16);
            Unsafe.Add(ref p2, i) = (byte)(v >> 8); Unsafe.Add(ref p3, i) = (byte)v;
        }
        ref byte r = ref p0;
        for (int i = count - 1; i >= stride; i--) Unsafe.Add(ref r, i) -= Unsafe.Add(ref r, i - stride);
    }

    public static void Swab16(Span<byte> data)
    {
        var s = MemoryMarshal.Cast<byte, ushort>(data);
        BinaryPrimitives.ReverseEndianness(s, s);
    }

    public static void Scatter32(ReadOnlySpan<byte> planeRow, Span<byte> interleavedRow, int channel, int channels)
    {
        ReadOnlySpan<uint> s = MemoryMarshal.Cast<byte, uint>(planeRow);
        ref uint d = ref Unsafe.Add(ref Unsafe.As<byte, uint>(ref MemoryMarshal.GetReference(interleavedRow)), channel);
        for (int i = 0; i < s.Length; i++) Unsafe.Add(ref d, i * channels) = s[i];
    }

    /// <summary>TIFF LZW (MSB-first, early change) decoder; table entries point back into the output (no linked lists).</summary>
    public static int LzwDecode(ReadOnlySpan<byte> src, Span<byte> dst)
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
            int oldStart = o - oldLen; // string(old) was the last thing emitted
            if (code < 256) { s = -1; l = 1; }
            else if (code < free) { s = start[code]; l = len[code]; }
            else if (code == free) { s = oldStart; l = oldLen + 1; }
            else return -1; // corrupt

            // new table entry: string(old) + first byte of string(code); it starts where old was emitted
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

/// <summary>Strip decoding with System.IO.Compression (zlib-ng) directly into the destination.</summary>
public static class Zlib
{
    public static void Inflate(byte[] src, int srcCount, Span<byte> dst)
    {
        using var zs = new ZLibStream(new MemoryStream(src, 0, srcCount, writable: false), CompressionMode.Decompress);
        zs.ReadAtLeast(dst, dst.Length, throwOnEndOfStream: false);
    }

    public static int Deflate(ReadOnlySpan<byte> src, byte[] dst, CompressionLevel level)
    {
        var ms = new MemoryStream(dst, 0, dst.Length, writable: true);
        using (var zs = new ZLibStream(ms, level, leaveOpen: true)) zs.Write(src);
        return (int)ms.Position;
    }
}
