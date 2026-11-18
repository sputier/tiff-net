using System.Globalization;
using Eleris.TiffNet.Fuzz;

// Structure-aware fuzzer of the readers of Eleris.TiffNet.
//
//   dotnet run -c Release --project tools/Fuzz -- run [--seconds 60] [--seed 1] [--mode mixed] [--target none] [--out fuzz-out] [corpus...]
//   dotnet run -c Release --project tools/Fuzz -- reproduce --seed 1 --iteration 1234 [--mode mixed] [--out input.tif] [corpus...]
//   dotnet run -c Release --project tools/Fuzz -- replay [--target none] [--out fuzz-out] file-or-folder...
//
// The corpus defaults to the fixtures of the repository (tests/Fixtures: domain, hostile, and interop/generated when it is
// generated). An input is a pure function of the campaign seed, the iteration and the corpus, whose identifier is printed and
// written in every note: "reproduce" writes the input again from the same corpus. Inputs that crash (an exception other than
// TiffException or NotSupportedException), take more than 2 s or allocate more than 256 MB are saved under the output folder
// with a note; the input being run is also written to current.txt before it runs, which survives a crash of the process itself.
// An input running for 60 s stops the process (exit code 3). "run" and "replay" exit with 1 when an input misbehaved, so that
// they can be used as gates.

// Numbers in the output must not depend on the culture of the machine.
CultureInfo.DefaultThreadCurrentCulture = CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
try
{
    return Run(args);
}
catch (Exception ex) when (ex is ArgumentException or FormatException or InvalidOperationException)
{
    Console.Error.WriteLine(ex.Message);
    Console.Error.WriteLine("Usage: see tools/Fuzz/Program.cs.");
    return 2;
}

static int Run(string[] args)
{
    if (args.Length == 0)
    {
        throw new ArgumentException("A command is expected: run, reproduce or replay.");
    }

    var options = Options.Parse(args.Skip(1));
    string targetName = options.Get("target", "none");
    var target = Targets.All.TryGetValue(targetName, out var t)
        ? t
        : throw new ArgumentException($"Unknown target '{targetName}'; the targets are: {string.Join(", ", Targets.All.Keys)}.");
    var mode = Enum.TryParse<MutationMode>(options.Get("mode", "mixed"), ignoreCase: true, out var m)
        ? m
        : throw new ArgumentException($"Unknown mode; the modes are: {string.Join(", ", Enum.GetNames<MutationMode>())}.");
    int seed = int.Parse(options.Get("seed", "1"), CultureInfo.InvariantCulture);

    switch (args[0])
    {
        case "run":
        {
            var corpus = LoadCorpus(options, targetName);
            var duration = TimeSpan.FromSeconds(double.Parse(options.Get("seconds", "60"), CultureInfo.InvariantCulture));
            var campaign = new Campaign(corpus, target, mode, seed, options.Get("out", "fuzz-out"));
            long iterations = campaign.Run(duration);
            Console.WriteLine($"{iterations} inputs from corpus {corpus.Id} ({corpus.Count} files), seed {seed}, {mode}, target {targetName}:");
            foreach (var (outcome, count) in campaign.Outcomes.OrderByDescending(o => o.Value))
            {
                Console.WriteLine($"  {count,10}  {outcome}");
            }

            return campaign.Failed ? 1 : 0;
        }

        case "reproduce":
        {
            var corpus = LoadCorpus(options, targetName);
            long iteration = long.Parse(options.Get("iteration", "0"), CultureInfo.InvariantCulture);
            string output = options.Get("out", "input.tif");
            File.WriteAllBytes(output, corpus.Input(seed, iteration, mode));
            Console.WriteLine($"{output}: input {iteration} of seed {seed} ({mode}), corpus {corpus.Id} ({corpus.Count} files).");
            return 0;
        }

        case "replay":
        {
            string[] files = [.. options.Positional.SelectMany(p => Directory.Exists(p) ? Directory.GetFiles(p, Targets.PatternOf(targetName)) : [p])];
            if (files.Length == 0)
            {
                throw new ArgumentException("No file to replay.");
            }

            var campaign = new Campaign(new Corpus([]), target, mode, seed, options.Get("out", "fuzz-out"));
            foreach (var (file, outcome, elapsed, allocated) in campaign.Replay(files))
            {
                Console.WriteLine($"{Path.GetFileName(file)}: {outcome}, {elapsed.TotalSeconds:F2} s, {allocated >> 20} MB");
            }

            return campaign.Failed ? 1 : 0;
        }

        default:
            throw new ArgumentException($"Unknown command '{args[0]}'.");
    }
}

static Corpus LoadCorpus(Options options, string target)
{
    string pattern = Targets.PatternOf(target);
    var corpus = Corpus.Load(options.Corpus, pattern);
    return corpus.Count > 0 ? corpus : throw new ArgumentException($"No seed file ({pattern}) in {string.Join(", ", options.Corpus)}.");
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
            if (args[i].StartsWith("--", StringComparison.Ordinal))
            {
                if (i + 1 >= args.Length || args[i + 1].StartsWith("--", StringComparison.Ordinal))
                {
                    throw new ArgumentException($"{args[i]} needs a value.");
                }

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

    private static string[] DefaultCorpus()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Eleris.TiffNet.slnx")))
            {
                string fixtures = Path.Combine(dir.FullName, "tests", "Fixtures");
                return [Path.Combine(fixtures, "domain"), Path.Combine(fixtures, "unsupported"), Path.Combine(fixtures, "hostile"), Path.Combine(fixtures, "jpeg"), Path.Combine(fixtures, "interop", "generated")];
            }
        }

        throw new DirectoryNotFoundException("Eleris.TiffNet.slnx was not found above " + AppContext.BaseDirectory);
    }
}
