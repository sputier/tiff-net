using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Eleris.TiffNet.Fuzz;

/// <summary>What the fuzzer runs on each input.</summary>
internal static class Targets
{
    /// <summary>
    /// The targets by name. "none" only exercises the harness (mutations, timing, saving); "open" reads the structure of a file
    /// from memory and every property of its pages and sub-images; "read" also reads their pixels; "save" writes the readable pages
    /// again (in strips, then in planar tiles) and opens what it wrote; "jpeg" decodes a JPEG stream (its seeds are the *.jpg files
    /// of the corpus, mutated byte by byte).
    /// </summary>
    public static readonly IReadOnlyDictionary<string, Action<byte[]>> All = new Dictionary<string, Action<byte[]>>
    {
        ["none"] = static _ => { },
        ["open"] = Open,
        ["read"] = Read,
        ["save"] = Save,
        ["jpeg"] = Jpeg,
    };

    /// <summary>The seed files of a target: JPEG streams for "jpeg", TIFF files otherwise.</summary>
    public static string PatternOf(string target) => target == "jpeg" ? "*.jpg" : "*.tif";

    /// <summary>Decodes a JPEG stream, its defects reported as the TIFF reader reports them (a TiffException).</summary>
    private static void Jpeg(byte[] input)
    {
        try
        {
            var frame = new Internal.Jpeg.JpegDecoder(new Internal.ResourceBudget(Options, "fuzz.jpg")).Decode(input);
            _ = frame.Components[0].Plane[^1];
        }
        catch (InvalidDataException ex)
        {
            throw new TiffException(ex.Message);
        }
    }

    /// <summary>Opens the file, then reads the pixels of every page and sub-image it can, as doubles and raw.</summary>
    private static void Read(byte[] input)
    {
        using var file = TiffFile.Read(new MemoryStream(input, writable: false), Options);
        var pending = new Stack<TiffImage>(file);
        while (pending.TryPop(out var image))
        {
            // An image of more samples than an array holds is read by region: whole reads refuse it, as documented.
            if (image.UnsupportedReason is null
                && (Int128)image.Width * image.Height * image.Channels <= Array.MaxLength)
            {
                _ = image.ReadPixels<double>(TiffPixelScale.Normalized);
                _ = image.ReadPixels<double>(order: TiffPixelOrder.Planar);
                _ = image.ReadPixels<float>(order: TiffPixelOrder.Planar);
                _ = ReadRaw(image);
                _ = image.ReadChannel<double>(image.Channels - 1, TiffPixelScale.Normalized);
                if (image.Width > 2 && image.Height > 2)
                {
                    _ = image.ReadRegion<double>(1, 1, image.Width - 2, image.Height - 2);
                    _ = image.ReadRegion<float>(image.Width / 2, 1, image.Width - image.Width / 2, image.Height - 2, order: TiffPixelOrder.Planar);
                }

                // The span overload decodes on the calling thread only.
                var buffer = new double[image.Width * image.Height * image.Channels];
                image.ReadPixels<double>(buffer);
            }

            foreach (var sub in image.SubImages)
            {
                pending.Push(sub);
            }
        }
    }

    /// <summary>Opens the file and saves its readable pages twice, as an application converting files it receives would.</summary>
    private static void Save(byte[] input)
    {
        using var file = TiffFile.Read(new MemoryStream(input, writable: false), Options);
        var pages = new TiffFile();
        foreach (var image in file.Where(i => i.UnsupportedReason is null))
        {
            pages.Add(image);
        }

        if (pages.Count == 0)
        {
            return;
        }

        foreach (var options in new[]
        {
            TiffWriteOptions.Default with { MaxDegreeOfParallelism = 2 },
            TiffWriteOptions.Default with { Layout = TiffLayout.Tiles, PlanarConfiguration = TiffPixelOrder.Planar, Compression = TiffCompression.Lzw },
        })
        {
            using var output = new MemoryStream();
            pages.Save(output, options);
            using var back = TiffFile.Read(new MemoryStream(output.ToArray(), writable: false));
            _ = back.Primary;
        }
    }

    /// <summary>The samples of an image raw, of their type.</summary>
    private static Array ReadRaw(TiffImage image) => image.SampleType switch
    {
        TiffSampleType.UInt8 => image.ReadRawPixels<byte>(),
        TiffSampleType.Int8 => image.ReadRawPixels<sbyte>(),
        TiffSampleType.UInt16 => image.ReadRawPixels<ushort>(TiffPixelOrder.Planar),
        TiffSampleType.Int16 => image.ReadRawPixels<short>(),
        TiffSampleType.UInt32 => image.ReadRawPixels<uint>(),
        TiffSampleType.Int32 => image.ReadRawPixels<int>(),
        TiffSampleType.UInt64 => image.ReadRawPixels<ulong>(),
        TiffSampleType.Int64 => image.ReadRawPixels<long>(),
        TiffSampleType.Float16 => image.ReadRawRegion<Half>(0, 0, image.Width, image.Height),
        TiffSampleType.Float32 => image.ReadRawPixels<float>(),
        _ => image.ReadRawPixels<double>(),
    };

    private static void Open(byte[] input)
    {
        using var file = TiffFile.Read(new MemoryStream(input, writable: false), Options);
        _ = file.Primary;
        foreach (var image in file)
        {
            Touch(image);
        }
    }

    /// <summary>Every property of an image and of its sub-images, without recursion (a hostile chain could be deep).</summary>
    private static void Touch(TiffImage first)
    {
        var pending = new Stack<TiffImage>([first]);
        while (pending.TryPop(out var image))
        {
            _ = (image.Width, image.Height, image.Channels, image.ExtraChannels.Count, image.IsReducedResolution, image.UnsupportedReason);
            if (image.UnsupportedReason is null)
            {
                _ = (image.SampleType, image.ColorModel);
            }

            _ = image.Storage;
            var m = image.Metadata;
            _ = (m.Description, m.Software, m.DateTime, m.Resolution, m.SampleRange, m.IccProfile, m.Xmp, m.Iptc, m.Artist, m.Copyright);
            foreach (var tag in m.Concat(m.Exif).Concat(m.Gps))
            {
                TouchTag(tag);
            }

            foreach (var sub in image.SubImages)
            {
                pending.Push(sub);
            }
        }
    }

    private static void TouchTag(TiffTag tag)
    {
        _ = (tag.Count, tag.AsBytes().Length, tag.ToString());
        try
        {
            _ = tag.Type is TiffTagType.Ascii or TiffTagType.Undefined or TiffTagType.Byte or TiffTagType.SByte
                ? tag.AsStrings().Count
                : tag.AsNumbers<double>().Length;
        }
        catch (InvalidOperationException)
        {
            // A conversion the tag does not allow: documented, not a finding.
        }
    }

    /// <summary>The limits of a campaign: those of a small device, so that a valid but large input is not a finding.</summary>
    // 32 MB per read: the read target makes up to 8 reads of an image, which stay within the 256 MB a well-behaved input may allocate.
    private static readonly TiffReadOptions Options = TiffReadOptions.Default with { MaxPixelBytes = 32L << 20, MaxInputBytes = 16L << 20 };
}

/// <summary>The seed files of a campaign; an input is a pure function of the campaign seed, its iteration and this corpus.</summary>
internal sealed class Corpus
{
    private readonly byte[][] _seeds;

    public Corpus(IReadOnlyList<string> files)
    {
        _seeds = [.. files.Select(File.ReadAllBytes)];
        // Identifies the corpus in the notes of saved inputs: the same seed and iteration give the same input only with it.
        var hash = new StringBuilder();
        foreach (byte[] seed in _seeds)
        {
            hash.Append(Convert.ToHexString(SHA256.HashData(seed)));
        }

        Id = Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(hash.ToString())))[..16];
    }

    public int Count => _seeds.Length;

    /// <summary>A short hash of the content and order of the seeds.</summary>
    public string Id { get; }

    /// <summary>The seed files under the given folders, in a stable order (so that an input can be produced again).</summary>
    public static Corpus Load(IEnumerable<string> folders, string pattern = "*.tif", long maxBytes = 2_000_000) => new(
        [.. folders.SelectMany(f => Directory.Exists(f) ? Directory.EnumerateFiles(f, pattern, SearchOption.AllDirectories) : File.Exists(f) ? [f] : [])
            .Where(f => new FileInfo(f).Length is > 8 and var n && n <= maxBytes)
            .Order(StringComparer.Ordinal)]);

    /// <summary>The input of an iteration: the same campaign seed, iteration and corpus always give the same bytes.</summary>
    public byte[] Input(int campaignSeed, long iteration, MutationMode mode)
    {
        if (_seeds.Length == 0)
        {
            throw new InvalidOperationException("The corpus is empty.");
        }

        var random = new Random(Mix(campaignSeed, iteration));
        return Mutator.Mutate(_seeds[random.Next(_seeds.Length)], random, mode);
    }

    // SplitMix64: every (seed, iteration) gives an unrelated 32-bit seed, without the repetitions of a linear formula.
    private static int Mix(int seed, long iteration)
    {
        ulong z = unchecked(((ulong)(uint)seed << 32) ^ (ulong)iteration) + 0x9E3779B97F4A7C15UL;
        z = unchecked((z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL);
        z = unchecked((z ^ (z >> 27)) * 0x94D049BB133111EBUL);
        return unchecked((int)(z ^ (z >> 31)));
    }
}

/// <summary>A fuzzing campaign: runs inputs on a target, classifies what happens, saves the inputs that misbehave.</summary>
internal sealed class Campaign(Corpus corpus, Action<byte[]> target, MutationMode mode, int seed, string output)
{
    public static readonly TimeSpan Slow = TimeSpan.FromSeconds(2);
    public static readonly TimeSpan Kill = TimeSpan.FromSeconds(60);
    public const long MemoryThreshold = 256L << 20;

    private readonly Lock _lock = new();
    private long _startTicks;
    private long _iteration;
    private byte[]? _current;

    public Dictionary<string, long> Outcomes { get; } = [];

    /// <summary>Whether an input crashed, hung or allocated too much.</summary>
    public bool Failed => Outcomes.Keys.Any(o => o.StartsWith("crash", StringComparison.Ordinal) || o is "slow" or "memory");

    /// <summary>Runs inputs until <paramref name="duration"/> has passed (or <paramref name="maxIterations"/>).</summary>
    public long Run(TimeSpan duration, long maxIterations = long.MaxValue)
    {
        Directory.CreateDirectory(output);
        // The input being run, written before it runs: if it kills the process (stack overflow, fatal out of memory), this file
        // says which one it was.
        using var current = new FileStream(Path.Combine(output, "current.txt"), FileMode.Create, FileAccess.Write, FileShare.Read);
        using var stop = new CancellationTokenSource();
        var watchdog = StartWatchdog(stop.Token);
        var clock = Stopwatch.StartNew();
        long iterations = 0;
        try
        {
            for (long i = 0; i < maxIterations && clock.Elapsed < duration; i++, iterations++)
            {
                byte[] input = corpus.Input(seed, i, mode);
                Record(current, i);
                Interlocked.Exchange(ref _iteration, i);
                var (outcome, elapsed, allocated) = Execute(input);
                Classify(input, i, outcome, elapsed, allocated);
            }
        }
        finally
        {
            stop.Cancel();
            watchdog.Join();
        }

        return iterations;
    }

    /// <summary>Runs the given files, under the watchdog, and classifies them like campaign inputs.</summary>
    public IReadOnlyList<(string File, string Outcome, TimeSpan Elapsed, long Allocated)> Replay(IEnumerable<string> files)
    {
        using var stop = new CancellationTokenSource();
        var watchdog = StartWatchdog(stop.Token);
        var results = new List<(string, string, TimeSpan, long)>();
        try
        {
            foreach (string file in files)
            {
                byte[] input = File.ReadAllBytes(file);
                var (outcome, elapsed, allocated) = Execute(input);
                Classify(input, -1, outcome, elapsed, allocated);
                results.Add((file, outcome, elapsed, allocated));
            }
        }
        finally
        {
            stop.Cancel();
            watchdog.Join();
        }

        return results;
    }

    /// <summary>Runs one input and says what happened: ok, the expected exceptions, or a crash with its type.</summary>
    public (string Outcome, TimeSpan Elapsed, long Allocated) Execute(byte[] input)
    {
        Volatile.Write(ref _current, input);
        long before = GC.GetTotalAllocatedBytes(precise: false);
        Interlocked.Exchange(ref _startTicks, Stopwatch.GetTimestamp());
        var clock = Stopwatch.StartNew();
        string outcome;
        try
        {
            target(input);
            outcome = "ok";
        }
        catch (TiffException ex)
        {
            outcome = ex.Limit is null ? "TiffException" : $"TiffException:{ex.Limit}";
        }
        catch (NotSupportedException)
        {
            outcome = "NotSupportedException";
        }
        catch (Exception ex)
        {
            string frame = ex.StackTrace?.Split('\n').FirstOrDefault()?.Trim() ?? "";
            outcome = $"crash:{ex.GetType().Name} {frame}";
        }
        finally
        {
            Interlocked.Exchange(ref _startTicks, 0);
        }

        return (outcome, clock.Elapsed, GC.GetTotalAllocatedBytes(precise: false) - before);
    }

    private void Classify(byte[] input, long iteration, string outcome, TimeSpan elapsed, long allocated)
    {
        Count(outcome);
        if (outcome.StartsWith("crash", StringComparison.Ordinal))
        {
            Save("crash", input, iteration, outcome);
        }

        if (elapsed > Slow)
        {
            Count("slow");
            Save("hang", input, iteration, $"{outcome} after {elapsed.TotalSeconds:F2} s");
        }

        if (allocated > MemoryThreshold)
        {
            Count("memory");
            Save("memory", input, iteration, $"{outcome}, {allocated >> 20} MB allocated");
        }
    }

    private void Record(FileStream current, long iteration)
    {
        byte[] line = Encoding.ASCII.GetBytes(Note(iteration, "running").PadRight(200));
        current.Position = 0;
        current.Write(line);
        current.Flush();
    }

    private string Note(long iteration, string what) => iteration < 0
        ? what
        : string.Create(CultureInfo.InvariantCulture,
            $"seed={seed} iteration={iteration} mode={mode} corpus={corpus.Id} ({corpus.Count} files): {what}");

    private void Count(string outcome)
    {
        Outcomes.TryGetValue(outcome, out long n);
        Outcomes[outcome] = n + 1;
    }

    private void Save(string kind, byte[] input, long iteration, string what)
    {
        lock (_lock)
        {
            string folder = Path.Combine(output, kind);
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, Convert.ToHexString(SHA256.HashData(input))[..12] + ".tif");
            if (!File.Exists(path))
            {
                File.WriteAllBytes(path, input);
            }

            File.AppendAllText(path + ".txt", Note(iteration, what) + Environment.NewLine);
        }
    }

    private Thread StartWatchdog(CancellationToken stop)
    {
        var thread = new Thread(() => Watch(stop)) { IsBackground = true, Name = "fuzz watchdog" };
        thread.Start();
        return thread;
    }

    private void Watch(CancellationToken stop)
    {
        bool saved = false;
        while (!stop.WaitHandle.WaitOne(250))
        {
            long start = Interlocked.Read(ref _startTicks);
            if (start == 0)
            {
                saved = false;
                continue;
            }

            TimeSpan running = Stopwatch.GetElapsedTime(start);
            long iteration = Interlocked.Read(ref _iteration);
            if (running > Slow && !saved && Volatile.Read(ref _current) is { } input)
            {
                Save("hang", input, iteration, $"still running after {running.TotalSeconds:F1} s");
                saved = true;
            }

            if (running > Kill)
            {
                Console.Error.WriteLine($"Killed: an input ran for more than {Kill.TotalSeconds} s ({Note(iteration, "see hang/")}).");
                Environment.Exit(3);
            }
        }
    }
}
