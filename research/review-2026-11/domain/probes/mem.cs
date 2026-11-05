using System.Diagnostics;
using Eleris.TiffNet;
var r = new Random(2);
var px = new float[4000 * 3000 * 3];
for (int i = 0; i < px.Length; i++) px[i] = (float)(0.1 + 0.01 * r.NextDouble());
foreach (var c in new[] { TiffCompression.None, TiffCompression.Deflate, TiffCompression.Lzw })
{
    var path = Path.Combine(args[0], $"mem_{c}.tif");
    long a0 = GC.GetTotalAllocatedBytes(true);
    var img = TiffImage.Create(4000, 3000, 3, px);
    long a1 = GC.GetTotalAllocatedBytes(true);
    var sw = Stopwatch.StartNew();
    img.Save(path, new TiffWriteOptions { Compression = c });
    long a2 = GC.GetTotalAllocatedBytes(true); var ts = sw.ElapsedMilliseconds; sw.Restart();
    var back = TiffImage.Read(path);
    long a3 = GC.GetTotalAllocatedBytes(true);
    Console.WriteLine($"{c}: image {px.Length * 4 / 1e6:F0} MB; Create copies {(a1 - a0) / 1e6:F0} MB; Save {ts} ms allocs {(a2 - a1) / 1e6:F0} MB; Read {sw.ElapsedMilliseconds} ms allocs {(a3 - a2) / 1e6:F0} MB; file {new FileInfo(path).Length / 1e6:F0} MB");
    File.Delete(path);
}
