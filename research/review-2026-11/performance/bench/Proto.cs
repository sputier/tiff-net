using System.Buffers;
using System.IO.Compression;
using BitMiracle.LibTiff.Classic;
using Eleris.TiffNet;

namespace Bench;

/// <summary>
/// "What could be reached" prototype: strips read with RandomAccess straight from the file, decoded directly into the final
/// array (System.IO.Compression for Deflate, the Fast LZW decoder), predictor undone in place with the Fast kernels, one strip per
/// task. Not a reader (no validation, contiguous strips only) - a measured upper bound for the backlog.
/// </summary>
internal static partial class Proto
{
    internal sealed record Info(int Width, int Height, int Spp, int Bps, int Rps, Compression Comp, int Predictor, ulong[] Off, ulong[] Cnt);

    public static Info Inspect(string path)
    {
        using var t = Tiff.Open(path, "rc");
        int pred = t.GetField(TiffTag.PREDICTOR) is { } p ? p[0].ToInt() : 1;
        return new Info(t.GetField(TiffTag.IMAGEWIDTH)[0].ToInt(), t.GetField(TiffTag.IMAGELENGTH)[0].ToInt(),
            t.GetFieldDefaulted(TiffTag.SAMPLESPERPIXEL)[0].ToInt(), t.GetFieldDefaulted(TiffTag.BITSPERSAMPLE)[0].ToInt(),
            t.GetFieldDefaulted(TiffTag.ROWSPERSTRIP)[0].ToInt(), (Compression)t.GetFieldDefaulted(TiffTag.COMPRESSION)[0].ToInt(), pred,
            (ulong[])t.m_dir.td_stripoffset.Clone(), (ulong[])t.m_dir.td_stripbytecount.Clone());
    }

    public static byte[] Read(string path, int parallelism)
    {
        var info = Inspect(path); // header parse via LibTiff (cheap)
        int bytesPerSample = info.Bps / 8;
        int rowBytes = info.Width * info.Spp * bytesPerSample;
        var data = GC.AllocateUninitializedArray<byte>(checked(rowBytes * info.Height));
        using var handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.Read, FileOptions.RandomAccess);
        int strips = info.Off.Length;
        Parallel.For(0, strips, new ParallelOptions { MaxDegreeOfParallelism = parallelism }, s =>
        {
            int firstRow = s * info.Rps;
            int rows = Math.Min(info.Rps, info.Height - firstRow);
            Span<byte> dst = data.AsSpan(firstRow * rowBytes, rows * rowBytes);
            if (info.Comp == Compression.NONE)
            {
                RandomAccess.Read(handle, dst, (long)info.Off[s]);
                return;
            }

            int n = (int)info.Cnt[s];
            byte[] raw = ArrayPool<byte>.Shared.Rent(n);
            byte[]? tmp = null;
            try
            {
                RandomAccess.Read(handle, raw.AsSpan(0, n), (long)info.Off[s]);
                if (info.Comp is Compression.ADOBE_DEFLATE or Compression.DEFLATE) Zlib.Inflate(raw, n, dst);
                else if (info.Comp == Compression.LZW) Fast.LzwDecode(raw.AsSpan(0, n), dst);
                else throw new NotSupportedException(info.Comp.ToString());

                if (info.Predictor == 2 && bytesPerSample == 2)
                    for (int r = 0; r < rows; r++) Fast.HorAcc16(dst.Slice(r * rowBytes, rowBytes), info.Spp);
                else if (info.Predictor == 3 && bytesPerSample == 4)
                {
                    tmp = ArrayPool<byte>.Shared.Rent(rowBytes);
                    for (int r = 0; r < rows; r++) Fast.FpAcc32(dst.Slice(r * rowBytes, rowBytes), info.Spp, tmp);
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(raw);
                if (tmp != null) ArrayPool<byte>.Shared.Return(tmp);
            }
        });
        return data;
    }

    /// <summary>Encodes every strip (predictor + zlib-ng) in parallel into pooled buffers, then writes them in order.</summary>
    public static long WriteDeflate(TiffImage img, string path, int parallelism, CompressionLevel level, bool predictor)
    {
        byte[] data = img.Data;
        int bps = (int)(Data.Bytes(img) / ((long)img.Width * img.Height * img.Channels));
        int rowBytes = img.Width * img.Channels * bps;
        int rps = Math.Max(1, 256 * 1024 / rowBytes);
        int strips = (img.Height + rps - 1) / rps;
        var outBufs = new byte[strips][];
        var outLens = new int[strips];
        Parallel.For(0, strips, new ParallelOptions { MaxDegreeOfParallelism = parallelism }, s =>
        {
            int firstRow = s * rps;
            int rows = Math.Min(rps, img.Height - firstRow);
            ReadOnlySpan<byte> src = data.AsSpan(firstRow * rowBytes, rows * rowBytes);
            byte[] scratch = ArrayPool<byte>.Shared.Rent(src.Length);
            try
            {
                ReadOnlySpan<byte> input = src;
                if (predictor)
                {
                    for (int r = 0; r < rows; r++)
                    {
                        var sr = src.Slice(r * rowBytes, rowBytes);
                        var dr = scratch.AsSpan(r * rowBytes, rowBytes);
                        if (bps == 2) Fast.HorDiff16(sr, dr, img.Channels); else Fast.FpDiff32(sr, dr, img.Channels);
                    }
                    input = scratch.AsSpan(0, src.Length);
                }
                byte[] o = ArrayPool<byte>.Shared.Rent(src.Length + src.Length / 100 + 1024);
                outLens[s] = Zlib.Deflate(input, o, level);
                outBufs[s] = o;
            }
            finally { ArrayPool<byte>.Shared.Return(scratch); }
        });
        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20);
        long total = 0;
        for (int s = 0; s < strips; s++)
        {
            fs.Write(outBufs[s], 0, outLens[s]);
            total += outLens[s];
            ArrayPool<byte>.Shared.Return(outBufs[s]);
        }
        return total;
    }

    public static void Run(string[] args)
    {
        int n = args.Length > 1 ? int.Parse(args[1]) : 5;
        Console.WriteLine($"{"case",-60} {"median ms",10} {"min ms",8} {"MB/s",8} {"alloc MB",9} {"gen2",5}  extra");
        foreach (var name in new[] { "u16_24_zip_p2.tif", "u16_24_zip.tif", "u16_24_lzw.tif", "u16_24_lzw_p2.tif", "u16_24_none.tif",
                     "u16_61_zip_p2.tif", "f32rgb_24_zip_p3.tif", "f32rgb_24_lzw_p3.tif", "f32rgb_24_none.tif" })
        {
            string path = Data.P(name);
            var reference = TiffImage.Read(path).Data;
            foreach (int par in new[] { 1, Environment.ProcessorCount })
            {
                var got = Read(path, par);
                bool ok = got.AsSpan().SequenceEqual(reference);
                Quick.Measure($"proto read {name} threads={par}", n, () => Read(path, par), () => reference.Length, ok ? "identical to TiffImage.Read" : "MISMATCH");
            }
        }

        foreach (var (label, img) in new[] { ("u16_24", Data.U16_24), ("f32rgb_24", Data.F32Rgb_24) })
        {
            string t = Data.P("out_proto.tif");
            foreach (var lvl in new[] { CompressionLevel.Optimal, CompressionLevel.Fastest })
                foreach (int par in new[] { 1, Environment.ProcessorCount })
                {
                    long size = 0;
                    Quick.Measure($"proto write {label} deflate {lvl}+pred threads={par}", n, () => size = WriteDeflate(img, t, par, lvl, true),
                        () => Data.Bytes(img), () => $"out {size / 1e6:F1} MB");
                }
            File.Delete(t);
        }
    }
}
