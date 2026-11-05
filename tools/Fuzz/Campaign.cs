using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;

namespace Eleris.TiffNet.Fuzz;

/// <summary>What the fuzzer runs on each input.</summary>
internal static class Targets
{
    /// <summary>
    /// The targets by name. "none" only exercises the harness (mutations, timing, saving); the readers become targets as they are
    /// written ("read": open the file from memory and read the pixels of every page).
    /// </summary>
    public static readonly IReadOnlyDictionary<string, Action<byte[]>> All = new Dictionary<string, Action<byte[]>>
    {
        ["none"] = static _ => { },
    };
}

/// <summary>One fuzzing input, reproducible from the seed of the campaign and its iteration number.</summary>
internal sealed class Corpus(IReadOnlyList<string> files)
{
    private readonly byte[][] _seeds = [.. files.Select(File.ReadAllBytes)];

    public int Count => _seeds.Length;

    /// <summary>The seed files under the given folders, in a stable order (so that an input can be produced again).</summary>
    public static Corpus Load(IEnumerable<string> folders, long maxBytes = 2_000_000) => new(
        [.. folders.SelectMany(f => Directory.Exists(f) ? Directory.EnumerateFiles(f, "*.tif", SearchOption.AllDirectories) : File.Exists(f) ? [f] : [])
            .Where(f => new FileInfo(f).Length is > 8 and var n && n <= maxBytes)
            .Order(StringComparer.Ordinal)]);

    /// <summary>The input of an iteration: the same campaign seed, iteration and corpus always give the same bytes.</summary>
    public byte[] Input(int campaignSeed, long iteration, MutationMode mode)
    {
        var random = new Random(unchecked((int)(campaignSeed * 1_000_003L + iteration * 7919L)));
        return Mutator.Mutate(_seeds[random.Next(_seeds.Length)], random, mode);
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

    public Dictionary<string, long> Outcomes { get; } = [];

    /// <summary>Runs inputs until <paramref name="duration"/> has passed (or <paramref name="maxIterations"/>).</summary>
    public long Run(TimeSpan duration, long maxIterations = long.MaxValue)
    {
        foreach (string folder in new[] { "crash", "hang", "memory" })
        {
            Directory.CreateDirectory(Path.Combine(output, folder));
        }

        var watchdog = new Thread(Watch) { IsBackground = true };
        watchdog.Start();
        var clock = Stopwatch.StartNew();
        long iterations = 0;
        for (long i = 0; i < maxIterations && clock.Elapsed < duration; i++, iterations++)
        {
            byte[] input = corpus.Input(seed, i, mode);
            Interlocked.Exchange(ref _iteration, i);
            var (outcome, elapsed, allocated) = Execute(input);
            Count(outcome);
            if (outcome.StartsWith("crash", StringComparison.Ordinal))
            {
                Save("crash", input, i, outcome);
            }

            if (elapsed > Slow)
            {
                Count("slow");
                Save("hang", input, i, $"{outcome} after {elapsed.TotalSeconds:F2} s");
            }

            if (allocated > MemoryThreshold)
            {
                Count("memory");
                Save("memory", input, i, $"{outcome}, {allocated >> 20} MB allocated");
            }
        }

        return iterations;
    }

    /// <summary>Runs one input and says what happened: ok, the expected exceptions, or a crash with its type.</summary>
    public (string Outcome, TimeSpan Elapsed, long Allocated) Execute(byte[] input)
    {
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

    private void Count(string outcome)
    {
        Outcomes.TryGetValue(outcome, out long n);
        Outcomes[outcome] = n + 1;
    }

    private void Save(string kind, byte[] input, long iteration, string what)
    {
        lock (_lock)
        {
            string name = Convert.ToHexString(SHA256.HashData(input))[..12];
            string path = Path.Combine(output, kind, name + ".tif");
            string note = string.Create(CultureInfo.InvariantCulture,
                $"seed={seed} iteration={iteration} mode={mode}: {what}{Environment.NewLine}");
            if (!File.Exists(path))
            {
                File.WriteAllBytes(path, input);
            }

            File.AppendAllText(path + ".txt", note);
        }
    }

    private void Watch()
    {
        bool saved = false;
        while (true)
        {
            Thread.Sleep(250);
            long start = Interlocked.Read(ref _startTicks);
            if (start == 0)
            {
                saved = false;
                continue;
            }

            TimeSpan running = Stopwatch.GetElapsedTime(start);
            long iteration = Interlocked.Read(ref _iteration);
            if (running > Slow && !saved)
            {
                Save("hang", corpus.Input(seed, iteration, mode), iteration, $"still running after {running.TotalSeconds:F1} s");
                saved = true;
            }

            if (running > Kill)
            {
                Console.Error.WriteLine($"Killed: iteration {iteration} ran for more than {Kill.TotalSeconds} s.");
                Environment.Exit(3);
            }
        }
    }
}
