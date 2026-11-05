using BitMiracle.LibTiff.Classic;
using Eleris.TiffNet;

/// <summary>Builds classic little-endian TIFF files entry by entry.</summary>
public sealed class TiffBuilder
{
    readonly List<(ushort tag, ushort type, uint count, byte[] raw)> _e = new();
    public byte[] Payload = Array.Empty<byte>();
    public long PadTo;
    public TiffBuilder Add(ushort tag, ushort type, uint count, byte[] raw) { _e.RemoveAll(x => x.tag == tag); _e.Add((tag, type, count, raw)); return this; }
    public TiffBuilder Short(ushort tag, params int[] v) => Add(tag, 3, (uint)v.Length, v.SelectMany(x => BitConverter.GetBytes((ushort)x)).ToArray());
    public TiffBuilder Long(ushort tag, params long[] v) => Add(tag, 4, (uint)v.Length, v.SelectMany(x => BitConverter.GetBytes((uint)x)).ToArray());
    public TiffBuilder Bytes(ushort tag, byte[] v, ushort type = 7) => Add(tag, type, (uint)v.Length, v);
    public const int PayloadOffset = 8;
    public byte[] Build()
    {
        var ms = new MemoryStream();
        var bw = new BinaryWriter(ms);
        bw.Write((byte)'I'); bw.Write((byte)'I'); bw.Write((ushort)42); bw.Write(0u);
        bw.Write(Payload);
        if (ms.Length % 2 == 1) bw.Write((byte)0);
        long ifd = ms.Length;
        var sorted = _e.OrderBy(x => x.tag).ToList();
        long extra = ifd + 2 + 12 * sorted.Count + 4;
        var ex = new MemoryStream();
        bw.Write((ushort)sorted.Count);
        foreach (var (tag, type, count, raw) in sorted)
        {
            bw.Write(tag); bw.Write(type); bw.Write(count);
            if (raw.Length <= 4) { var b = new byte[4]; raw.CopyTo(b, 0); bw.Write(b); }
            else { bw.Write((uint)(extra + ex.Length)); ex.Write(raw); if (ex.Length % 2 == 1) ex.WriteByte(0); }
        }
        bw.Write(0u);
        bw.Write(ex.ToArray());
        if (PadTo > ms.Length) ms.SetLength(PadTo);
        var r = ms.ToArray();
        BitConverter.GetBytes((uint)ifd).CopyTo(r, 4);
        return r;
    }
}

public static class Gen
{
    // Encode one strip (rows x width x spp x bps) of the given pixel content with LibTiff, return raw strip + JPEGTABLES (if any).
    public static (byte[] raw, byte[] tables) EncodeStrip(Compression c, int width, int rows, int spp, int bps, Photometric ph, byte[] pixels, int predictor = 1, SampleFormat fmt = SampleFormat.UINT)
    {
        var ms = new MemoryStream();
        using (var t = Tiff.ClientOpen("x", "w", ms, new NoCloseStream()))
        {
            t.SetField(TiffTag.IMAGEWIDTH, width); t.SetField(TiffTag.IMAGELENGTH, rows);
            t.SetField(TiffTag.SAMPLESPERPIXEL, spp); t.SetField(TiffTag.BITSPERSAMPLE, bps);
            t.SetField(TiffTag.SAMPLEFORMAT, fmt);
            t.SetField(TiffTag.PHOTOMETRIC, ph); t.SetField(TiffTag.PLANARCONFIG, PlanarConfig.CONTIG);
            t.SetField(TiffTag.COMPRESSION, c); t.SetField(TiffTag.ROWSPERSTRIP, rows);
            if (predictor != 1) t.SetField(TiffTag.PREDICTOR, predictor);
            if (c == Compression.JPEG) t.SetField(TiffTag.JPEGQUALITY, 75);
            if (t.WriteEncodedStrip(0, pixels, pixels.Length) < 0) throw new Exception("encode failed");
            t.Flush();
        }
        ms.Position = 0;
        using var r = Tiff.ClientOpen("x", "r", ms, new NoCloseStream());
        var size = (int)r.RawStripSize(0);
        var raw = new byte[size];
        r.ReadRawStrip(0, raw, 0, size);
        byte[] tables = null;
        var f = r.GetField(TiffTag.JPEGTABLES);
        if (f != null) tables = f[1].GetBytes();
        return (raw, tables);
    }

    public static int Run(string[] a)
    {
        string dir = a[1];
        Directory.CreateDirectory(dir);
        if (a[0] == "bombs") Bombs(dir);
        if (a[0] == "seeds") Seeds(dir);
        if (a[0] == "jpegseeds") JpegSeeds.Run(dir);
        return 0;
    }

    // Decompression bombs at the facade's 4096x limit: one compressed strip referenced by every strip offset.
    static void Bombs(string dir)
    {
        const int W = 46336, Rps = 16, H = 46336; // 2,147,024,896 bytes gray8
        int nstrips = H / Rps;
        var zeros = new byte[W * Rps];
        foreach (var (name, c, bps, fmt, pred, ph, orient) in new (string, Compression, int, SampleFormat, int, Photometric, int)[]
        {
            ("bomb-deflate-2G.tif", Compression.ADOBE_DEFLATE, 8, SampleFormat.UINT, 1, Photometric.MINISBLACK, 1),
            ("bomb-deflate-2G-botright.tif", Compression.ADOBE_DEFLATE, 8, SampleFormat.UINT, 1, Photometric.MINISBLACK, 3),
            ("bomb-lzw-2G.tif", Compression.LZW, 8, SampleFormat.UINT, 1, Photometric.MINISBLACK, 1),
            ("bomb-packbits-2G.tif", Compression.PACKBITS, 8, SampleFormat.UINT, 1, Photometric.MINISBLACK, 1),
            ("bomb-jpeg-2G.tif", Compression.JPEG, 8, SampleFormat.UINT, 1, Photometric.MINISBLACK, 1),
            ("bomb-deflate-pred2-2G.tif", Compression.ADOBE_DEFLATE, 8, SampleFormat.UINT, 2, Photometric.MINISBLACK, 1),
        })
        {
            var (raw, tables) = EncodeStrip(c, W, Rps, 1, bps, ph, zeros, pred, fmt);
            Write(dir, name, W, H, Rps, nstrips, 1, bps, (int)fmt, pred, (int)c, (int)ph, orient, raw, tables);
        }
        {
            // float32 with floating-point predictor: W2 x H2 x 4 bytes
            const int W2 = 23168, H2 = 23168, R2 = 16;
            var z = new byte[W2 * R2 * 4];
            var (raw, _) = EncodeStrip(Compression.ADOBE_DEFLATE, W2, R2, 1, 32, Photometric.MINISBLACK, z, 3, SampleFormat.IEEEFP);
            Write(dir, "bomb-deflate-fp3-2G.tif", W2, H2, R2, H2 / R2, 1, 32, 3, 3, 8, 1, 1, raw, null);
        }
    }

    static void Write(string dir, string name, int w, int h, int rps, int nstrips, int spp, int bps, int fmt, int pred, int comp, int ph, int orient, byte[] raw, byte[] tables)
    {
        var b = new TiffBuilder { Payload = raw };
        b.Long(256, w).Long(257, h).Short(258, bps).Short(259, comp).Short(262, ph).Short(274, orient).Short(277, spp).Long(278, rps)
         .Short(339, fmt).Long(273, Enumerable.Repeat((long)TiffBuilder.PayloadOffset, nstrips).ToArray())
         .Long(279, Enumerable.Repeat((long)raw.Length, nstrips).ToArray());
        if (pred != 1) b.Short(317, pred);
        if (tables != null) b.Bytes(347, tables);
        long total = (long)w * h * spp * (bps / 8);
        b.PadTo = total / 4096 + 4096;
        var bytes = b.Build();
        File.WriteAllBytes(Path.Combine(dir, name), bytes);
        Console.WriteLine($"{name}: file {bytes.Length} bytes, raw strip {raw.Length}, declares {total} pixel bytes");
    }


    static void Seeds(string dir)
    {
        int n = 0;
        var rnd = new Random(42);
        foreach (var comp in new[] { Compression.NONE, Compression.LZW, Compression.ADOBE_DEFLATE, Compression.PACKBITS, Compression.JPEG })
        foreach (var (bps, fmt) in new[] { (8, SampleFormat.UINT), (16, SampleFormat.UINT), (32, SampleFormat.IEEEFP), (64, SampleFormat.IEEEFP), (32, SampleFormat.INT) })
        foreach (var spp in new[] { 1, 3, 4 })
        foreach (var planar in new[] { PlanarConfig.CONTIG, PlanarConfig.SEPARATE })
        foreach (var tiled in new[] { false, true })
        foreach (var mode in new[] { "w", "wb", "w8" })
        {
            if (comp == Compression.JPEG && (bps != 8 || spp == 4)) continue;
            if (spp == 1 && planar == PlanarConfig.SEPARATE) continue;
            int pred = 1;
            if (comp is Compression.LZW or Compression.ADOBE_DEFLATE) pred = fmt == SampleFormat.IEEEFP ? 3 : (bps <= 32 ? 2 : 1);
            if (rnd.Next(3) == 0) pred = 1;
            if (rnd.Next(2) == 0 && mode != "w") continue; // thin out
            int w = 37, h = 23;
            try
            {
                var bytes = Make(comp, bps, fmt, spp, planar, tiled, mode, pred, w, h, rnd);
                File.WriteAllBytes(Path.Combine(dir, $"seed-{n++:D4}-{comp}-{bps}{fmt}-s{spp}-{planar}-{(tiled ? "tile" : "strip")}-{mode}-p{pred}.tif"), bytes);
            }
            catch (Exception ex) { Console.WriteLine($"skip {comp} {bps} {spp} {planar} {tiled} {mode}: {ex.Message}"); }
        }
        Console.WriteLine($"{n} seeds");
    }

    static byte[] Make(Compression comp, int bps, SampleFormat fmt, int spp, PlanarConfig planar, bool tiled, string mode, int pred, int w, int h, Random rnd)
    {
        var ms = new MemoryStream();
        int sb = bps / 8;
        using (var t = Tiff.ClientOpen("x", mode, ms, new NoCloseStream()))
        {
            t.SetField(TiffTag.IMAGEWIDTH, w); t.SetField(TiffTag.IMAGELENGTH, h);
            t.SetField(TiffTag.SAMPLESPERPIXEL, spp); t.SetField(TiffTag.BITSPERSAMPLE, bps);
            t.SetField(TiffTag.SAMPLEFORMAT, fmt);
            t.SetField(TiffTag.PHOTOMETRIC, spp >= 3 ? Photometric.RGB : Photometric.MINISBLACK);
            t.SetField(TiffTag.PLANARCONFIG, planar);
            if (spp == 4) t.SetField(TiffTag.EXTRASAMPLES, 1, new short[] { 0 });
            t.SetField(TiffTag.COMPRESSION, comp);
            if (comp == Compression.JPEG) t.SetField(TiffTag.JPEGCOLORMODE, JpegColorMode.RAW);
            if (pred != 1) t.SetField(TiffTag.PREDICTOR, pred);
            int planes = planar == PlanarConfig.SEPARATE ? spp : 1;
            int perPix = planar == PlanarConfig.SEPARATE ? 1 : spp;
            if (tiled)
            {
                t.SetField(TiffTag.TILEWIDTH, 16); t.SetField(TiffTag.TILELENGTH, 16);
                int tb = 16 * 16 * perPix * sb;
                for (int p = 0; p < planes; p++)
                for (int y = 0; y < h; y += 16)
                for (int x = 0; x < w; x += 16)
                {
                    var buf = Content(tb, rnd, sb, fmt);
                    if (t.WriteEncodedTile(t.ComputeTile(x, y, 0, (short)p), buf, tb) < 0) throw new Exception("tile");
                }
            }
            else
            {
                int rps = 8;
                t.SetField(TiffTag.ROWSPERSTRIP, rps);
                int stripsPerPlane = (h + rps - 1) / rps;
                for (int p = 0; p < planes; p++)
                for (int s = 0; s < stripsPerPlane; s++)
                {
                    int rows = Math.Min(rps, h - s * rps);
                    int cnt = rows * w * perPix * sb;
                    var buf = Content(cnt, rnd, sb, fmt);
                    if (t.WriteEncodedStrip(p * stripsPerPlane + s, buf, cnt) < 0) throw new Exception("strip");
                }
            }
            t.Flush();
        }
        var bytes = ms.ToArray();
        // sanity: must be readable by the facade
        TiffImage.Read(new MemoryStream(bytes));
        return bytes;
    }

    static byte[] Content(int n, Random rnd, int sb, SampleFormat fmt)
    {
        var b = new byte[n];
        if (fmt == SampleFormat.IEEEFP)
        {
            for (int i = 0; i + sb <= n; i += sb)
            {
                double v = Math.Sin(i * 0.01) * 100 + rnd.NextDouble();
                if (sb == 4) BitConverter.GetBytes((float)v).CopyTo(b, i); else BitConverter.GetBytes(v).CopyTo(b, i);
            }
        }
        else
        {
            for (int i = 0; i < n; i++) b[i] = (byte)((i / 7) + (rnd.Next(4) == 0 ? rnd.Next(256) : 0));
        }
        return b;
    }
}
public sealed class NoCloseStream : TiffStream { public override void Close(object clientData) { } }
