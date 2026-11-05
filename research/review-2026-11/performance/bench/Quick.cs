using System.Diagnostics;
using System.IO.Compression;
using Eleris.TiffNet;

namespace Bench;

/// <summary>Stopwatch harness: median of N runs, throughput on the decoded pixel bytes, allocations, GC counts.</summary>
public static class Quick
{
    public static void Run(string[] args)
    {
        int n = args.Length > 1 ? int.Parse(args[1]) : 5;
        string filter = args.Length > 2 ? args[2] : "";
        Console.WriteLine($".NET {Environment.Version}, {Environment.ProcessorCount} cores, Server GC={System.Runtime.GCSettings.IsServerGC}");
        Console.WriteLine($"{"case",-44} {"median ms",10} {"min ms",8} {"MB/s",8} {"alloc MB",9} {"gen2",5}  extra");

        if (filter.StartsWith("w:"))
        {
            var parts = filter.Split(':');
            var img = parts[1] == "u16_24" ? Data.U16_24 : Data.F32Rgb_24;
            var c = Enum.Parse<TiffCompression>(parts[2]);
            string target = Data.P($"out_ab_{parts[1]}_{c}.tif");
            Measure($"write {parts[1]} {c} (file)", n, () => img.Save(target, new TiffWriteOptions { Compression = c }), () => Data.Bytes(img),
                () => $"out {new FileInfo(target).Length / 1e6:F1} MB");
            File.Delete(target);
            return;
        }

        foreach (var f in Directory.GetFiles(Data.Dir, "*.tif").OrderBy(x => x))
        {
            string name = Path.GetFileName(f);
            if (!name.Contains(filter)) continue;
            long pixelBytes = 0;
            Measure($"read {name}", n, () => { var i = TiffImage.Read(f); pixelBytes = Data.Bytes(i); }, () => pixelBytes,
                $"file {new FileInfo(f).Length / 1e6:F1} MB");
            if (filter.Length > 0) continue;
            Measure($"  ref File.ReadAllBytes", n, () => File.ReadAllBytes(f), () => new FileInfo(f).Length, "");
        }

        if (filter is "" or "write")
        {
            foreach (var (label, img) in new[] { ("u16_24", Data.U16_24), ("f32rgb_24", Data.F32Rgb_24) })
            {
                long bytes = Data.Bytes(img);
                foreach (var c in Enum.GetValues<TiffCompression>())
                {
                    string target = Data.P($"out_{label}_{c}.tif");
                    Measure($"write {label} {c} (file)", n, () => img.Save(target, new TiffWriteOptions { Compression = c }), () => bytes,
                        () => $"out {new FileInfo(target).Length / 1e6:F1} MB");
                    Measure($"write {label} {c} (MemoryStream)", n, () =>
                    {
                        using var ms = new MemoryStream((int)(bytes + bytes / 8));
                        img.Save(ms, new TiffWriteOptions { Compression = c });
                    }, () => bytes, "");
                    File.Delete(target);
                }

                byte[] raw = img.Data;
                string rt = Data.P($"out_{label}_raw.bin");
                Measure($"  ref File.WriteAllBytes {label}", n, () => File.WriteAllBytes(rt, raw), () => bytes, "");
                File.Delete(rt);
                byte[] z = [];
                foreach (var lvl in new[] { CompressionLevel.Fastest, CompressionLevel.Optimal })
                {
                    Measure($"  ref ZLibStream {lvl} compress {label}", n, () =>
                    {
                        using var ms = new MemoryStream();
                        using (var zs = new ZLibStream(ms, lvl, true)) zs.Write(raw);
                        z = ms.ToArray();
                    }, () => bytes, () => $"ratio {(double)z.Length / bytes:F3}");
                    byte[] dst = new byte[bytes];
                    Measure($"  ref ZLibStream {lvl} decompress {label}", n, () =>
                    {
                        using var zs = new ZLibStream(new MemoryStream(z), CompressionMode.Decompress);
                        zs.ReadExactly(dst);
                    }, () => bytes, "");
                }
            }
        }
    }

    public static void Measure(string label, int n, Action a, Func<long> bytes, string extra) => Measure(label, n, a, bytes, () => extra);

    public static void Measure(string label, int n, Action a, Func<long> bytes, Func<string> extra)
    {
        a(); // warm-up (JIT + OS cache)
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        var times = new List<double>();
        long alloc0 = GC.GetTotalAllocatedBytes(true);
        int g2 = GC.CollectionCount(2);
        for (int i = 0; i < n; i++)
        {
            var sw = Stopwatch.StartNew();
            a();
            times.Add(sw.Elapsed.TotalMilliseconds);
        }

        long alloc = (GC.GetTotalAllocatedBytes(true) - alloc0) / n;
        int gen2 = GC.CollectionCount(2) - g2;
        times.Sort();
        double med = times[times.Count / 2];
        Console.WriteLine($"{label,-44} {med,10:F1} {times[0],8:F1} {bytes() / 1e6 / (med / 1000),8:F0} {alloc / 1e6,9:F1} {gen2,5}  {extra()}");
    }
}
