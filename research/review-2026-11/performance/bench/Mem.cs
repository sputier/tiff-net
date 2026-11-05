using System.Diagnostics;
using Eleris.TiffNet;

namespace Bench;

/// <summary>Peak memory of one read or write, in a fresh process (run once per case).</summary>
public static class Mem
{
    public static void Run(string[] args)
    {
        string mode = args[1];
        if (mode == "gen")
        {
            // 9576 x 6388 float RGB (61 MP, 734 MB of pixels): smooth sky + noise, through the facade.
            int w = 9576, h = 6388;
            var px = new float[(long)w * h * 3];
            var rng = new Random(1);
            for (long i = 0; i < px.Length; i++) px[i] = 0.08f + (float)(rng.NextDouble() * 0.01);
            var img = TiffImage.Create<float>(w, h, 3, px);
            px = null;
            img.Save(Data.P("f32rgb_61_none.tif"));
            img.Save(Data.P("f32rgb_61_zip_p3.tif"), new TiffWriteOptions { Compression = TiffCompression.Deflate });
            return;
        }

        var proc = Process.GetCurrentProcess();
        long baseWs = proc.WorkingSet64;
        var sw = Stopwatch.StartNew();
        long pixels = 0;
        if (mode == "read")
        {
            var img = TiffImage.Read(args[2]);
            pixels = Data.Bytes(img);
            GC.KeepAlive(img);
        }
        else if (mode == "readwrite")
        {
            var img = TiffImage.Read(args[2]);
            pixels = Data.Bytes(img);
            var c = Enum.Parse<TiffCompression>(args[3]);
            string t = Data.P("mem_out.tif");
            img.Save(t, new TiffWriteOptions { Compression = c });
            File.Delete(t);
            GC.KeepAlive(img);
        }

        double ms = sw.Elapsed.TotalMilliseconds;
        proc.Refresh();
        var info = GC.GetGCMemoryInfo();
        Console.WriteLine($"{mode} {Path.GetFileName(args[2])} {(args.Length > 3 ? args[3] : "")}: {ms:F0} ms, pixels {pixels / 1e6:F0} MB, " +
            $"peak WS {proc.PeakWorkingSet64 / 1e6:F0} MB (start {baseWs / 1e6:F0} MB), peak private {proc.PeakPagedMemorySize64 / 1e6:F0} MB, " +
            $"total allocated {GC.GetTotalAllocatedBytes(true) / 1e6:F0} MB, gen0/1/2 {GC.CollectionCount(0)}/{GC.CollectionCount(1)}/{GC.CollectionCount(2)}, " +
            $"heap now {GC.GetTotalMemory(false) / 1e6:F0} MB, fragmented {info.FragmentedBytes / 1e6:F0} MB");
    }
}
