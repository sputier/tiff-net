using System.Diagnostics;
using Eleris.TiffNet;

public static partial class Program
{
    public static int Main(string[] args)
    {
        switch (args[0])
        {
            case "measure": return Measure(args.Skip(1).ToArray());
            case "fuzz": return Fuzzer.Run(args.Skip(1).ToArray());
            case "gen": return Gen.Run(args.Skip(1).ToArray());
            case "replay": return Fuzzer.Replay(args.Skip(1).ToArray());
            case "streamtest": return StreamTest.Run(args.Skip(1).ToArray());
            default: Console.WriteLine("?"); return 2;
        }
    }

    static int Measure(string[] files)
    {
        foreach (var f in files)
        {
            var sw = Stopwatch.StartNew();
            long before = GC.GetTotalAllocatedBytes(true);
            string outcome;
            try
            {
                var img = TiffImage.Read(f);
                outcome = $"OK {img.Width}x{img.Height}x{img.Channels} {img.SampleType}";
            }
            catch (TiffException ex) { outcome = "TiffException: " + Trunc(ex.Message); }
            catch (Exception ex) { outcome = $"!!! {ex.GetType().FullName}: {Trunc(ex.Message)}\n{ex.StackTrace}"; }
            sw.Stop();
            long alloc = GC.GetTotalAllocatedBytes(true) - before;
            var p = Process.GetCurrentProcess(); p.Refresh();
            Console.WriteLine($"{Path.GetFileName(f)} size={new FileInfo(f).Length} time={sw.Elapsed.TotalSeconds:F2}s alloc={alloc / 1048576.0:F1}MB peakWS={p.PeakWorkingSet64 / 1048576.0:F0}MB peakPriv={p.PeakPagedMemorySize64 / 1048576.0:F0}MB -> {outcome}");
        }
        return 0;
    }

    public static string Trunc(string s) => s.Length > 300 ? s[..300] + "..." : s;
}
