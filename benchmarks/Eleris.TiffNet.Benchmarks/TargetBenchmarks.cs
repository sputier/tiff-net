using BenchmarkDotNet.Attributes;

namespace Eleris.TiffNet.Benchmarks;

/// <summary>
/// The scenarios of the performance targets (docs/design/architecture.md): 24 MP u16 grey and f32 RGB, written and read with
/// Deflate and the predictor that suits them (and u16 uncompressed), on one thread and on every core. The images are smooth with
/// noise, as astronomical frames are; f32 RGB also at the level Fastest. Results: benchmarks/results.
/// </summary>
[MemoryDiagnoser]
public class TargetBenchmarks
{
    private const int Width = 6000, Height = 4000;
    private TiffImage _image = null!;
    private string _path = "";

    [Params("u16", "u16-none", "f32-rgb", "f32-rgb-fastest")]
    public string Scenario { get; set; } = "u16";

    [Params(1, -1)]
    public int Threads { get; set; }

    private TiffWriteOptions Options => Scenario switch
    {
        "u16-none" => TiffWriteOptions.Default with { Compression = TiffCompression.None },
        "f32-rgb-fastest" => TiffWriteOptions.Default with { Level = TiffCompressionLevel.Fastest },
        _ => TiffWriteOptions.Default,
    } with
    { MaxDegreeOfParallelism = Threads };

    [GlobalSetup]
    public void Setup()
    {
        var random = new Random(1);
        int channels = Scenario.StartsWith("f32-rgb", StringComparison.Ordinal) ? 3 : 1;
        double Value(int x, int y, int c) => 1000 + 800 * Math.Sin(x / 50.0 + c) * Math.Cos(y / 70.0) + random.Next(40);
        if (channels == 3)
        {
            var samples = new float[Width * Height * 3];
            for (int i = 0; i < samples.Length; i++)
            {
                samples[i] = (float)(Value(i / 3 % Width, i / 3 / Width, i % 3) / 2000);
            }

            _image = TiffImage.Create<float>(samples, Width, Height, 3, TiffColorModel.Rgb);
        }
        else
        {
            var samples = new ushort[Width * Height];
            for (int i = 0; i < samples.Length; i++)
            {
                samples[i] = (ushort)Value(i % Width, i / Width, 0);
            }

            _image = TiffImage.Create<ushort>(samples, Width, Height);
        }

        _path = Path.Combine(Path.GetTempPath(), $"tiffnet-bench-{Scenario}.tif");
        new TiffFile().Add(_image).Save(_path, Options);
    }

    [GlobalCleanup]
    public void Cleanup() => File.Delete(_path);

    [Benchmark]
    public long Write()
    {
        using var stream = new MemoryStream(64 << 20);
        new TiffFile().Add(_image).Save(stream, Options);
        return stream.Length;
    }

    [Benchmark]
    public int Read()
    {
        using var file = TiffFile.Open(_path, TiffReadOptions.Default with { MaxDegreeOfParallelism = Threads });
        return file.Primary.SampleType == TiffSampleType.Float32 ? file.Primary.ReadRawPixels<float>().Length : file.Primary.ReadRawPixels<ushort>().Length;
    }
}
