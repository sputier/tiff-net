using System.Buffers.Binary;
using System.Diagnostics;
using Eleris.TiffNet;

/// <summary>Structure-aware TIFF fuzzer over the public facade, with a watchdog for hangs and per-exec allocation tracking.</summary>
public static class Fuzzer
{
    static volatile byte[] _current;
    static long _startTicks; // 0 = idle
    static string _out;
    static System.IO.MemoryMappedFiles.MemoryMappedViewAccessor _mm;
    static readonly object _lock = new();

    static readonly ushort[] KnownTags = {
        254,255,256,257,258,259,262,263,264,265,266,269,270,271,272,273,274,277,278,279,280,281,282,283,284,285,286,287,288,289,290,291,292,293,296,297,
        301,305,306,315,316,317,318,319,320,321,322,323,324,325,326,327,328,330,332,333,334,336,337,338,339,340,341,342,343,344,346,347,
        400,401,402,403,404,405,512,513,514,515,517,518,519,520,521,529,530,531,532,700,32781,32932,32953,32954,32955,32956,32995,32996,32997,32998,
        33421,33422,33423,33432,33550,33723,33922,34016,34017,34018,34019,34020,34021,34022,34023,34024,34025,34026,34027,34028,34029,34030,34031,34032,
        34232,34377,34665,34675,34732,34750,34853,34908,34909,34910,34911,34929,37439,37724,40001,50215,50216,50217,50218,50219,50220,50221,50222,50225,50706,50784,65000,65535 };
    static readonly long[] Interesting = { 0, 1, 2, 3, 4, 7, 8, 15, 16, 17, 31, 32, 63, 64, 127, 128, 255, 256, 511, 512, 1023, 1024, 4095, 4096, 32767, 32768, 65535, 65536,
        0x7FFF_FFFF, 0x8000_0000, 0xFFFF_FFFF, 0xFFFF_FFFE, 0x1_0000_0000, 0x7FFF_FFFF_FFFF_FFFF, -1, -2, 0x10000000, 0x01000000 };
    static readonly int[] Compressions = { 1, 2, 3, 4, 5, 6, 7, 8, 32773, 32766, 32809, 32895, 32896, 32897, 32898, 32908, 32909, 32946, 34661, 34676, 34677, 34712, 0, 65535 };

    record struct Entry(int Pos, ushort Tag, ushort Type, ulong Count, ulong Value, int ValuePos, int ValueSize);
    sealed class Parsed { public bool LE, Big; public int IfdPos; public List<Entry> Entries = new(); public int NextPos; }

    static Parsed Parse(byte[] d)
    {
        if (d.Length < 16) return null;
        var p = new Parsed();
        if (d[0] == (byte)'I' && d[1] == (byte)'I') p.LE = true; else if (d[0] == (byte)'M' && d[1] == (byte)'M') p.LE = false; else return null;
        int ver = R16(d, 2, p.LE);
        p.Big = ver == 43;
        ulong ifd = p.Big ? R64(d, 8, p.LE) : R32(d, 4, p.LE);
        if (ifd == 0 || ifd >= (ulong)d.Length - 8) return null;
        p.IfdPos = (int)ifd;
        int es = p.Big ? 20 : 12;
        ulong n = p.Big ? R64(d, p.IfdPos, p.LE) : R16(d, p.IfdPos, p.LE);
        int pos = p.IfdPos + (p.Big ? 8 : 2);
        for (ulong i = 0; i < n && pos + es <= d.Length; i++, pos += es)
        {
            ushort tag = R16(d, pos, p.LE), type = R16(d, pos + 2, p.LE);
            ulong count = p.Big ? R64(d, pos + 4, p.LE) : R32(d, pos + 4, p.LE);
            int vp = pos + (p.Big ? 12 : 8); int vs = p.Big ? 8 : 4;
            ulong value = p.Big ? R64(d, vp, p.LE) : R32(d, vp, p.LE);
            p.Entries.Add(new Entry(pos, tag, type, count, value, vp, vs));
        }
        p.NextPos = pos;
        return p;
    }

    static ushort R16(byte[] d, int o, bool le) => le ? BinaryPrimitives.ReadUInt16LittleEndian(d.AsSpan(o)) : BinaryPrimitives.ReadUInt16BigEndian(d.AsSpan(o));
    static uint R32(byte[] d, int o, bool le) => le ? BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(o)) : BinaryPrimitives.ReadUInt32BigEndian(d.AsSpan(o));
    static ulong R64(byte[] d, int o, bool le) => le ? BinaryPrimitives.ReadUInt64LittleEndian(d.AsSpan(o)) : BinaryPrimitives.ReadUInt64BigEndian(d.AsSpan(o));
    static void W16(byte[] d, int o, bool le, ushort v) { if (o < 0 || o + 2 > d.Length) return; if (le) BinaryPrimitives.WriteUInt16LittleEndian(d.AsSpan(o), v); else BinaryPrimitives.WriteUInt16BigEndian(d.AsSpan(o), v); }
    static void W32(byte[] d, int o, bool le, uint v) { if (o < 0 || o + 4 > d.Length) return; if (le) BinaryPrimitives.WriteUInt32LittleEndian(d.AsSpan(o), v); else BinaryPrimitives.WriteUInt32BigEndian(d.AsSpan(o), v); }
    static void W64(byte[] d, int o, bool le, ulong v) { if (o < 0 || o + 8 > d.Length) return; if (le) BinaryPrimitives.WriteUInt64LittleEndian(d.AsSpan(o), v); else BinaryPrimitives.WriteUInt64BigEndian(d.AsSpan(o), v); }

    // Writes a scalar into an entry's inline value slot respecting its type size.
    static void SetValue(byte[] d, Parsed p, Entry e, ulong v, ushort type)
    {
        int sz = type switch { 1 or 2 or 6 or 7 => 1, 3 or 8 => 2, 16 or 17 or 18 => 8, _ => 4 };
        if (sz == 2) W16(d, e.ValuePos, p.LE, (ushort)v);
        else if (sz == 1) { if (e.ValuePos < d.Length) d[e.ValuePos] = (byte)v; }
        else if (sz == 8 && p.Big) W64(d, e.ValuePos, p.LE, v);
        else W32(d, e.ValuePos, p.LE, (uint)v);
    }

    static ulong Pick(Random r, int len)
    {
        switch (r.Next(6))
        {
            case 0: return (ulong)Interesting[r.Next(Interesting.Length)];
            case 1: return (ulong)r.Next(0, Math.Max(1, len + 16));
            case 2: return (ulong)(len + r.Next(-8, 8));
            case 3: return (ulong)r.NextInt64();
            case 4: return (ulong)r.Next(0, 70000);
            default: return (ulong)(1L << r.Next(0, 63)) + (ulong)r.Next(-1, 2);
        }
    }

    static byte[] MutateIfd(byte[] src, Random r)
    {
        var d = (byte[])src.Clone();
        var p = Parse(d);
        if (p == null || p.Entries.Count == 0) return Havoc(d, r);
        int ops = 1 + r.Next(3);
        int es = p.Big ? 20 : 12;
        for (int k = 0; k < ops; k++)
        {
            var e = p.Entries[r.Next(p.Entries.Count)];
            switch (r.Next(14))
            {
                case 0: W16(d, e.Pos + 2, p.LE, (ushort)(r.Next(4) == 0 ? r.Next(0, 65536) : r.Next(0, 20))); break; // type
                case 1: // count
                    {
                        ulong c = r.Next(3) switch { 0 => Pick(r, d.Length), 1 => e.Count + (ulong)r.Next(-2, 3), _ => e.Count * (ulong)r.Next(2, 5) };
                        if (p.Big) W64(d, e.Pos + 4, p.LE, c); else W32(d, e.Pos + 4, p.LE, (uint)c);
                        break;
                    }
                case 2: // raw value/offset
                    if (p.Big) W64(d, e.ValuePos, p.LE, Pick(r, d.Length)); else W32(d, e.ValuePos, p.LE, (uint)Pick(r, d.Length));
                    break;
                case 3: W16(d, e.Pos, p.LE, KnownTags[r.Next(KnownTags.Length)]); break; // retag
                case 4: // typed scalar on key tags
                    {
                        ulong v = e.Tag switch
                        {
                            259 => (ulong)Compressions[r.Next(Compressions.Length)],
                            262 => (ulong)(r.Next(5) == 0 ? r.Next(0, 65536) : new[] { 0, 1, 2, 3, 4, 5, 6, 8, 9, 10, 32844, 32845 }[r.Next(12)]),
                            258 => (ulong)new[] { 0, 1, 2, 4, 8, 12, 16, 24, 32, 64, 128, 255, 65535 }[r.Next(13)],
                            277 => (ulong)new[] { 0, 1, 2, 3, 4, 5, 255, 32767, 65535 }[r.Next(9)],
                            284 => (ulong)r.Next(0, 4),
                            317 => (ulong)r.Next(0, 5),
                            339 => (ulong)r.Next(0, 8),
                            274 => (ulong)r.Next(0, 10),
                            _ => Pick(r, d.Length),
                        };
                        SetValue(d, p, e, v, e.Type);
                        break;
                    }
                case 5: // duplicate entry over another
                    {
                        var o = p.Entries[r.Next(p.Entries.Count)];
                        Buffer.BlockCopy(d, e.Pos, d, o.Pos, es);
                        break;
                    }
                case 6: // swap entries
                    {
                        var o = p.Entries[r.Next(p.Entries.Count)];
                        var tmp = new byte[es];
                        Buffer.BlockCopy(d, e.Pos, tmp, 0, es); Buffer.BlockCopy(d, o.Pos, d, e.Pos, es); Buffer.BlockCopy(tmp, 0, d, o.Pos, es);
                        break;
                    }
                case 7: // dircount
                    {
                        ulong n = (ulong)p.Entries.Count;
                        ulong v = r.Next(3) switch { 0 => n + (ulong)r.Next(-3, 4), 1 => Pick(r, d.Length), _ => 0 };
                        if (p.Big) W64(d, p.IfdPos, p.LE, v); else W16(d, p.IfdPos, p.LE, (ushort)v);
                        break;
                    }
                case 8: // next IFD -> loop / random
                    {
                        ulong v = r.Next(3) switch { 0 => (ulong)p.IfdPos, 1 => Pick(r, d.Length), _ => (ulong)Math.Max(8, p.IfdPos - r.Next(0, 64)) };
                        if (p.Big) W64(d, p.NextPos, p.LE, v); else W32(d, p.NextPos, p.LE, (uint)v);
                        break;
                    }
                case 9: // mutate pointed-to data of an out-of-line entry
                    {
                        if (e.Value > 8 && e.Value < (ulong)d.Length)
                        {
                            int o = (int)e.Value + r.Next(0, 16);
                            if (o + 4 <= d.Length) { if (r.Next(2) == 0) W32(d, o, p.LE, (uint)Pick(r, d.Length)); else d[o] ^= (byte)(1 << r.Next(8)); }
                        }
                        break;
                    }
                case 10: // SubIFD / EXIF pointers to self
                    W16(d, e.Pos, p.LE, new ushort[] { 330, 34665, 34853, 40965 }[r.Next(4)]);
                    W16(d, e.Pos + 2, p.LE, (ushort)(r.Next(2) == 0 ? 4 : 13));
                    if (p.Big) W64(d, e.ValuePos, p.LE, (ulong)p.IfdPos); else W32(d, e.ValuePos, p.LE, (uint)p.IfdPos);
                    break;
                case 11: // bigtiff header confusion
                    if (r.Next(2) == 0) W16(d, 2, p.LE, p.Big ? (ushort)42 : (ushort)43); else W16(d, 4, p.LE, (ushort)r.Next(0, 16));
                    break;
                default:
                    return Havoc(d, r, 1 + r.Next(3));
            }
        }
        return d;
    }

    static byte[] MutatePayload(byte[] src, Random r)
    {
        var d = (byte[])src.Clone();
        var p = Parse(d);
        if (p == null) return Havoc(d, r);
        var offs = Values(d, p, p.Entries.FirstOrDefault(e => e.Tag is 273 or 324));
        var cnts = Values(d, p, p.Entries.FirstOrDefault(e => e.Tag is 279 or 325));
        var regions = new List<(int, int)>();
        for (int i = 0; i < Math.Min(offs.Count, cnts.Count); i++)
            if (offs[i] < (ulong)d.Length && cnts[i] > 0) regions.Add(((int)offs[i], (int)Math.Min(cnts[i], (ulong)d.Length - offs[i])));
        var jt = p.Entries.FirstOrDefault(e => e.Tag == 347);
        if (jt.Tag == 347 && jt.Value < (ulong)d.Length && jt.Count > 4) regions.Add(((int)jt.Value, (int)Math.Min(jt.Count, (ulong)d.Length - jt.Value)));
        regions.RemoveAll(x => x.Item2 <= 0);
        if (regions.Count == 0) return Havoc(d, r);
        int ops = 1 + r.Next(r.Next(2) == 0 ? 2 : 8);
        for (int k = 0; k < ops; k++)
        {
            var (o, n) = regions[r.Next(regions.Count)];
            int at = o + r.Next(n);
            int room = o + n - at;
            switch (r.Next(6))
            {
                case 0: d[at] ^= (byte)(1 << r.Next(8)); break;
                case 1: d[at] = (byte)r.Next(256); break;
                case 2: d[at] = new byte[] { 0, 0xFF, 0x7F, 0x80, 1 }[r.Next(5)]; break;
                case 3: { int len = Math.Min(r.Next(1, 32), room); r.NextBytes(d.AsSpan(at, len)); break; }
                case 4: { int from = o + r.Next(n); int len = Math.Min(Math.Min(r.Next(1, 64), room), o + n - from); Buffer.BlockCopy(d, from, d, at, len); break; }
                default: { int len = Math.Min(r.Next(1, 16), room); d.AsSpan(at, len).Fill((byte)(r.Next(2) == 0 ? 0 : 0xFF)); break; }
            }
        }
        return d;
    }

    static List<ulong> Values(byte[] d, Parsed p, Entry e)
    {
        var l = new List<ulong>();
        if (e.Tag == 0 || e.Count == 0 || e.Count > 100000) return l;
        int w = e.Type switch { 3 => 2, 16 => 8, _ => 4 };
        bool inline = (ulong)w * e.Count <= (ulong)(p.Big ? 8 : 4);
        long start = inline ? e.ValuePos : (long)e.Value;
        for (ulong i = 0; i < e.Count; i++)
        {
            long at = start + (long)i * w;
            if (at < 0 || at + w > d.Length) break;
            l.Add(w == 2 ? R16(d, (int)at, p.LE) : w == 8 ? R64(d, (int)at, p.LE) : R32(d, (int)at, p.LE));
        }
        return l;
    }

    static byte[] Havoc(byte[] src, Random r, int ops = -1)
    {
        var d = new List<byte>(src);
        if (ops < 0) ops = 1 + r.Next(r.Next(2) == 0 ? 4 : 16);
        for (int k = 0; k < ops && d.Count > 0; k++)
        {
            int at = r.Next(d.Count);
            switch (r.Next(9))
            {
                case 0: d[at] ^= (byte)(1 << r.Next(8)); break;
                case 1: d[at] = (byte)r.Next(256); break;
                case 2: if (at + 4 <= d.Count) { var v = BitConverter.GetBytes((uint)Pick(r, d.Count)); for (int i = 0; i < 4; i++) d[at + i] = v[i]; } break;
                case 3: if (at + 2 <= d.Count) { var v = BitConverter.GetBytes((ushort)Pick(r, d.Count)); d[at] = v[0]; d[at + 1] = v[1]; } break;
                case 4: d.RemoveRange(at, Math.Min(r.Next(1, 64), d.Count - at)); break;
                case 5: d.InsertRange(at, Enumerable.Range(0, r.Next(1, 32)).Select(_ => (byte)r.Next(256))); break;
                case 6: if (r.Next(8) == 0) d.RemoveRange(at, d.Count - at); break; // truncate
                case 7: { int from = r.Next(d.Count); int len = Math.Min(r.Next(1, 64), Math.Min(d.Count - from, d.Count - at)); for (int i = 0; i < len; i++) d[at + i] = d[from + i]; break; }
                default: d[at] = new byte[] { 0, 0xFF, 0x7F, 0x80, 1 }[r.Next(5)]; break;
            }
        }
        return d.ToArray();
    }

    public static int Run(string[] a)
    {
        // fuzz <mode> <outdir> <seconds> <rngseed> <seeddir>...
        string mode = a[0]; _out = a[1]; int seconds = int.Parse(a[2]); int rs = int.Parse(a[3]);
        Directory.CreateDirectory(_out);
        foreach (var sub in new[] { "crash", "hang", "oom", "mem" }) Directory.CreateDirectory(Path.Combine(_out, sub));
        var seeds = a.Skip(4).SelectMany(s => Directory.Exists(s) ? Directory.GetFiles(s) : new[] { s })
            .Where(f => new FileInfo(f).Length is > 8 and < 600_000).Select(File.ReadAllBytes).ToArray();
        var r = new Random(rs);
        var stats = new Dictionary<string, long>();
        var crashSigs = new Dictionary<string, long>();
        long execs = 0, maxAlloc = 0, slow = 0, memSaved = 0; double maxSec = 0;
        var watchdog = new Thread(Watch) { IsBackground = true }; watchdog.Start();
        var total = Stopwatch.StartNew();
        var log = new StreamWriter(Path.Combine(_out, "log.txt"), append: true) { AutoFlush = true };
        var mmf = System.IO.MemoryMappedFiles.MemoryMappedFile.CreateFromFile(Path.Combine(_out, "current.bin"), FileMode.OpenOrCreate, null, 1 << 21);
        _mm = mmf.CreateViewAccessor();
        log.WriteLine($"start mode={mode} seeds={seeds.Length} rng={rs}");
        while (total.Elapsed.TotalSeconds < seconds)
        {
            var seed = seeds[r.Next(seeds.Length)];
            string m = mode == "mixed" ? new[] { "ifd", "payload", "havoc" }[r.Next(3)] : mode;
            byte[] input;
            try
            {
                input = m switch { "ifd" => MutateIfd(seed, r), "payload" => MutatePayload(seed, r), _ => Havoc(seed, r) };
                if (r.Next(10) == 0) input = m == "ifd" ? MutatePayload(input, r) : MutateIfd(input, r); // stack
            }
            catch (Exception) { input = Havoc(seed, r); } // harness mutator bug: never let it kill the worker
            string outcome;
            long before = GC.GetAllocatedBytesForCurrentThread();
            _current = input;
            if (_mm != null) { _mm.Write(0, input.Length); _mm.WriteArray(4, input, 0, Math.Min(input.Length, (1 << 21) - 4)); }
            Interlocked.Exchange(ref _startTicks, Stopwatch.GetTimestamp());
            var sw = Stopwatch.StartNew();
            try { TiffImage.Read(new MemoryStream(input, writable: false)); outcome = "ok"; }
            catch (TiffException) { outcome = "tiffex"; }
            catch (OutOfMemoryException ex)
            {
                outcome = "oom";
                string sig = "OOM@" + (ex.StackTrace ?? "").Split('\n').FirstOrDefault()?.Trim();
                crashSigs.TryGetValue(sig, out var c); crashSigs[sig] = c + 1;
                if (c < 3) Save("oom", input, ex.ToString());
            }
            catch (Exception ex)
            {
                outcome = "crash:" + ex.GetType().Name;
                string sig = ex.GetType().Name + "@" + (ex.StackTrace ?? "").Split('\n').FirstOrDefault()?.Trim();
                crashSigs.TryGetValue(sig, out var c); crashSigs[sig] = c + 1;
                if (c < 3) Save("crash", input, ex.ToString());
            }
            sw.Stop();
            Interlocked.Exchange(ref _startTicks, 0);
            long alloc = GC.GetAllocatedBytesForCurrentThread() - before;
            double sec = sw.Elapsed.TotalSeconds;
            if (sec > maxSec) maxSec = sec;
            if (sec > 2) { slow++; Save("hang", input, $"{sec:F2}s alloc={alloc} outcome={outcome}"); }
            if (alloc > maxAlloc) maxAlloc = alloc;
            if (alloc > 1L << 30 && memSaved++ < 30) Save("mem", input, $"alloc={alloc} size={input.Length} {sec:F2}s outcome={outcome}");
            stats.TryGetValue(outcome, out var s); stats[outcome] = s + 1;
            execs++;
            if (execs % 5000 == 0)
                log.WriteLine($"{total.Elapsed.TotalSeconds:F0}s execs={execs} {string.Join(" ", stats.Select(kv => kv.Key + "=" + kv.Value))} maxSec={maxSec:F2} maxAllocMB={maxAlloc >> 20}");
        }
        var summary = $"DONE mode={mode} execs={execs} secs={total.Elapsed.TotalSeconds:F0} {string.Join(" ", stats.Select(kv => kv.Key + "=" + kv.Value))} slow>2s={slow} maxSec={maxSec:F2} maxAllocMB={maxAlloc >> 20}\n"
            + string.Join("\n", crashSigs.Select(kv => $"  {kv.Value} x {kv.Key}"));
        log.WriteLine(summary);
        Console.WriteLine(summary);
        return 0;
    }

    static void Save(string kind, byte[] input, string info)
    {
        lock (_lock)
        {
            var h = Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(input))[..12];
            var path = Path.Combine(_out, kind, h + ".tif");
            if (File.Exists(path)) { File.AppendAllText(path + ".txt", "\n" + info); return; }
            File.WriteAllBytes(path, input);
            File.WriteAllText(path + ".txt", info);
        }
    }

    static void Watch()
    {
        bool saved = false;
        while (true)
        {
            Thread.Sleep(250);
            long st = Interlocked.Read(ref _startTicks);
            if (st == 0) { saved = false; continue; }
            double sec = Stopwatch.GetElapsedTime(st).TotalSeconds;
            if (sec > 2 && !saved) { Save("hang", _current, $"still running after {sec:F1}s (watchdog)"); saved = true; }
            if (sec > 60)
            {
                Save("hang", _current, "killed after 60s");
                File.AppendAllText(Path.Combine(_out, "log.txt"), "WATCHDOG KILL after 60s\n");
                Environment.Exit(3);
            }
        }
    }

    public static int Replay(string[] files)
    {
        foreach (var f in files.SelectMany(s => Directory.Exists(s) ? Directory.GetFiles(s, "*.tif") : new[] { s }))
        {
            var sw = Stopwatch.StartNew();
            string o;
            try { TiffImage.Read(f); o = "ok"; } catch (TiffException) { o = "tiffex"; } catch (Exception ex) { o = ex.GetType().Name + " " + Program.Trunc(ex.ToString()); }
            Console.WriteLine($"{Path.GetFileName(f)} {sw.Elapsed.TotalSeconds:F2}s {o}");
        }
        return 0;
    }
}
