using BenchmarkDotNet.Attributes;

namespace Eleris.TiffNet.Benchmarks;

/// <summary>
/// The conversions of samples that <see cref="PathBenchmarks"/> does not cover: float and 32-bit integer images read normalised
/// or as doubles. 24 MP RGB, uncompressed and read from memory, so that what is measured is the conversion. Results:
/// benchmarks/results.
/// </summary>
[MemoryDiagnoser]
public class ConversionBenchmarks
{
    private const int Width = 6000, Height = 4000;
    private TiffFile _file = null!;

    /// <summary>The stored samples: f32 or i32, RGB.</summary>
    [Params("f32-rgb", "i32-rgb")]
    public string Source { get; set; } = "f32-rgb";

    [GlobalSetup]
    public void Setup()
    {
        var random = new Random(1);
        int count = Width * Height * 3;
        var image = Source == "f32-rgb"
            ? TiffImage.Create<float>(Enumerable.Range(0, count).Select(_ => random.NextSingle()).ToArray(), Width, Height, 3, TiffColorModel.Rgb)
            : TiffImage.Create<int>(Enumerable.Range(0, count).Select(_ => random.Next(-1_000_000, 1_000_000)).ToArray(), Width, Height, 3, TiffColorModel.Rgb);
        using var stream = new MemoryStream();
        new TiffFile().Add(image).Save(stream, TiffWriteOptions.Default with { Compression = TiffCompression.None });
        var options = TiffReadOptions.Default with { MaxPixelBytes = 1L << 31, MaxInputBytes = 1L << 31 };
        _file = TiffFile.Read(new MemoryStream(stream.ToArray()), options);
    }

    [GlobalCleanup]
    public void Cleanup() => _file.Dispose();

    [Benchmark]
    public int NormalizedFloat() => _file.Primary.ReadPixels<float>(TiffPixelScale.Normalized).Length;

    [Benchmark]
    public int StoredDouble() => _file.Primary.ReadPixels<double>().Length;
}
