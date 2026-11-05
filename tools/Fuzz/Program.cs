using System.Diagnostics;
using System.Globalization;
using Eleris.TiffNet.Fuzz;

// Structure-aware fuzzer of the readers of Eleris.TiffNet.
//
//   dotnet run -c Release --project tools/Fuzz -- run [--seconds 60] [--seed 1] [--mode mixed] [--target none] [--out fuzz-out] [corpus...]
//   dotnet run -c Release --project tools/Fuzz -- reproduce --seed 1 --iteration 1234 [--mode mixed] [--out input.tif] [corpus...]
//   dotnet run -c Release --project tools/Fuzz -- replay [--target none] file-or-folder...
//
// The corpus defaults to the fixtures of the repository (tests/Fixtures: domain, hostile, and interop/generated when it is
// generated). An input is a pure function of the campaign seed, the iteration and the corpus: "reproduce" writes it again.
// Inputs that crash (an exception other than TiffException or NotSupportedException), take more than 2 s or allocate more than
// 256 MB are saved under the output folder with a note giving their seed and iteration; an input that runs for 60 s stops the
// process (exit code 3).

if (args.Length == 0)
{
    Console.WriteLine("Commands: run, reproduce, replay. See tools/Fuzz/Program.cs.");
    return 2;
}

var options = Options.Parse(args.Skip(1));
var target = Targets.All.TryGetValue(options.Get("target", "none"), out var t)
    ? t
    : throw new ArgumentException($"Unknown target; the targets are: {string.Join(", ", Targets.All.Keys)}.");
var mode = Enum.Parse<MutationMode>(options.Get("mode", "mixed"), ignoreCase: true);
int seed = int.Parse(options.Get("seed", "1"), CultureInfo.InvariantCulture);

switch (args[0])
{
    case "run":
    {
        var corpus = Corpus.Load(options.Corpus);
        string output = options.Get("out", "fuzz-out");
        var duration = TimeSpan.FromSeconds(double.Parse(options.Get("seconds", "60"), CultureInfo.InvariantCulture));
        var campaign = new Campaign(corpus, target, mode, seed, output);
        long iterations = campaign.Run(duration);
        Console.WriteLine($"{iterations} inputs from {corpus.Count} seeds (seed {seed}, {mode}, target {options.Get("target", "none")}):");
        foreach (var (outcome, count) in campaign.Outcomes.OrderByDescending(o => o.Value))
        {
            Console.WriteLine($"  {count,10}  {outcome}");
        }

        return campaign.Outcomes.Keys.Any(o => o.StartsWith("crash", StringComparison.Ordinal) || o is "slow" or "memory") ? 1 : 0;
    }

    case "reproduce":
    {
        var corpus = Corpus.Load(options.Corpus);
        long iteration = long.Parse(options.Get("iteration", "0"), CultureInfo.InvariantCulture);
        string output = options.Get("out", "input.tif");
        File.WriteAllBytes(output, corpus.Input(seed, iteration, mode));
        Console.WriteLine($"{output}: input {iteration} of seed {seed} ({mode}), from {corpus.Count} seeds.");
        return 0;
    }

    case "replay":
    {
        var campaign = new Campaign(Corpus.Load([]), target, mode, seed, Path.GetTempPath());
        foreach (string file in options.Positional.SelectMany(p => Directory.Exists(p) ? Directory.GetFiles(p, "*.tif") : [p]))
        {
            var (outcome, elapsed, allocated) = campaign.Execute(File.ReadAllBytes(file));
            Console.WriteLine($"{Path.GetFileName(file)}: {outcome}, {elapsed.TotalSeconds:F2} s, {allocated >> 20} MB");
        }

        return 0;
    }

    default:
        Console.WriteLine($"Unknown command '{args[0]}'.");
        return 2;
}

/// <summary>"--name value" options and positional arguments.</summary>
internal sealed class Options
{
    private readonly Dictionary<string, string> _named = [];

    public List<string> Positional { get; } = [];

    /// <summary>The corpus folders: the positional arguments, or the fixtures of the repository.</summary>
    public IEnumerable<string> Corpus => Positional.Count > 0 ? Positional : DefaultCorpus();

    public static Options Parse(IEnumerable<string> arguments)
    {
        var options = new Options();
        string[] args = [.. arguments];
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i].StartsWith("--", StringComparison.Ordinal) && i + 1 < args.Length)
            {
                options._named[args[i][2..]] = args[++i];
            }
            else
            {
                options.Positional.Add(args[i]);
            }
        }

        return options;
    }

    public string Get(string name, string fallback) => _named.TryGetValue(name, out string? value) ? value : fallback;

    private static IEnumerable<string> DefaultCorpus()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            string fixtures = Path.Combine(dir.FullName, "tests", "Fixtures");
            if (File.Exists(Path.Combine(dir.FullName, "Eleris.TiffNet.slnx")))
            {
                return [Path.Combine(fixtures, "domain"), Path.Combine(fixtures, "hostile"), Path.Combine(fixtures, "interop", "generated")];
            }
        }

        throw new DirectoryNotFoundException("Eleris.TiffNet.slnx was not found above " + AppContext.BaseDirectory);
    }
}
