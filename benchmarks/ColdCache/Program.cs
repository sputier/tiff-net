using System.Diagnostics;
using Eleris.TiffNet;

// Whole-image reads on a cold cache: before each read the file is opened without buffering, which makes Windows purge its
// cached pages (no other handle open). The hint of the handle the library reads through: RandomAccess (the library's), None,
// SequentialScan.
string folder = Path.Combine(Path.GetTempPath(), "tiffnet-cold");
Directory.CreateDirectory(folder);
const int W = 6000, H = 4000;
var random = new Random(1);
var samples = new ushort[W * H * 3];
for (int i = 0; i < samples.Length; i++) samples[i] = (ushort)(1000 + 800 * Math.Sin(i / 3 % W / 50.0) * Math.Cos(i / 3 / W / 70.0) + random.Next(40));
var image = TiffImage.Create<ushort>(samples, W, H, 3, TiffColorModel.Rgb);
var files = new (string Name, TiffWriteOptions Options)[]
{
    ("u16rgb-none", TiffWriteOptions.Default with { Compression = TiffCompression.None }),
    ("u16rgb-deflate", TiffWriteOptions.Default),
};
foreach (var (name, options) in files)
{
    new TiffFile().Add(image).Save(Path.Combine(folder, name + ".tif"), options);
}

const FileOptions NoBuffering = (FileOptions)0x20000000;
void Purge(string path)
{
    using var h = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 4096, NoBuffering);
}

foreach (var (name, _) in files)
{
    string path = Path.Combine(folder, name + ".tif");
    Console.WriteLine($"{name}: {new FileInfo(path).Length / 1e6:F0} MB");
    foreach (int threads in new[] { 1, -1 })
    {
        foreach (var hint in new[] { FileOptions.RandomAccess, FileOptions.None, FileOptions.SequentialScan })
        {
            var times = new List<double>();
            var warm = new List<double>();
            for (int k = 0; k < 7; k++)
            {
                Purge(path);
                foreach (var list in new[] { times, warm })
                {
                    var sw = Stopwatch.StartNew();
                    using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1, hint);
                    using var file = TiffFile.Open(stream, TiffReadOptions.Default with { MaxDegreeOfParallelism = threads }, leaveOpen: true);
                    _ = file.Primary.ReadRawPixels<ushort>();
                    list.Add(sw.Elapsed.TotalMilliseconds);
                }
            }

            times.Sort(); warm.Sort();
            Console.WriteLine($"  threads {threads,2} {hint,-14} cold median {times[3],7:F1} ms (min {times[0],6:F1})   warm median {warm[3],6:F1} ms");
        }
    }
}
