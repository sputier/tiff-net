using BenchmarkDotNet.Attributes;

namespace Eleris.TiffNet.Benchmarks;

/// <summary>
/// JPEG in TIFF: 12 MP files of 256 x 256 tiles of quality 90, each tile a stream with its own tables (as tifffile writes them;
/// written by tools/bench_jpeg.py), grey and YCbCr 4:2:0, read whole on one thread and on every core.
/// </summary>
[MemoryDiagnoser]
public class JpegTiffBenchmarks
{
    private TiffFile _file = null!;

    [Params("tiles-grey", "tiles-ycc420")]
    public string Image { get; set; } = "tiles-grey";

    [Params(1, -1)]
    public int Threads { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        string path = Path.Combine(JpegBenchmarks.Folder(), $"{Image}.tif");
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Run python -I tools/bench_jpeg.py first.", path);
        }

        _file = TiffFile.Open(path, TiffReadOptions.Default with { MaxDegreeOfParallelism = Threads });
    }

    [GlobalCleanup]
    public void Cleanup() => _file.Dispose();

    [Benchmark]
    public int Read() => _file.Primary.ReadRawPixels<byte>().Length;
}
