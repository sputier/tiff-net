using BenchmarkDotNet.Attributes;
using Eleris.TiffNet.Internal;
using Eleris.TiffNet.Internal.Jpeg;

namespace Eleris.TiffNet.Benchmarks;

/// <summary>
/// The JPEG decoder on 12 MP images of quality 90 (written by tools/bench_jpeg.py, which also times libjpeg-turbo on them):
/// decoding to the components, and to the image (upsampled, converted to RGB). The target is within 2x of libjpeg-turbo.
/// </summary>
[MemoryDiagnoser]
public class JpegBenchmarks
{
    private byte[] _data = [];
    private byte[] _image = [];

    [Params("grey", "ycc444", "ycc420", "prog420")]
    public string Image { get; set; } = "grey";

    [GlobalSetup]
    public void Setup()
    {
        string path = Path.Combine(Folder(), $"{Image}.jpg");
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Run python -I tools/bench_jpeg.py first.", path);
        }

        _data = File.ReadAllBytes(path);
        var frame = Decode();
        _image = new byte[frame.Width * frame.Height * JpegColour.Channels(frame)];
    }

    [Benchmark]
    public int Components() => Decode().Width;

    [Benchmark]
    public void ToImage()
    {
        var frame = Decode();
        int channels = JpegColour.Channels(frame);
        JpegColour.ToSamples(frame, frame.Height, channels == 3, _image, frame.Width * channels, Budget());
    }

    private JpegFrame Decode() => new JpegDecoder(Budget()).Decode(_data);

    private static ResourceBudget Budget() => new(TiffReadOptions.Default, "bench.jpg");

    private static string Folder()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Eleris.TiffNet.slnx")))
            {
                return Path.Combine(dir.FullName, "benchmarks", "data");
            }
        }

        throw new DirectoryNotFoundException("The repository was not found.");
    }
}
