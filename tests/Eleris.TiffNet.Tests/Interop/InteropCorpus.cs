using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Eleris.TiffNet.Tests.Interop;

/// <summary>The kind of the extra channel of a file with 2 or 4 channels (ExtraSamples).</summary>
internal enum InteropExtraKind
{
    None,
    Unspecified,
    AssociatedAlpha,
    UnassociatedAlpha,
}

/// <summary>One file of the tifffile corpus, with the parameters encoded in its name by tools/make_fixtures.py.</summary>
internal sealed record InteropFile(
    string Name,
    string SampleType,
    int Channels,
    InteropExtraKind ExtraKind,
    string Compression,
    bool Predictor,
    bool Tiled,
    bool Planar,
    bool BigTiff,
    bool BigEndian,
    bool Supported,
    string SamplesSha256);

/// <summary>
/// The corpus written by tifffile (tools/make_fixtures.py): the manifest is committed, the files are generated (CI job
/// <c>interop</c>, or locally with the script). Tests that need the files fail when they are missing, unless
/// <c>TIFFNET_SKIP_INTEROP=1</c>.
/// </summary>
internal static partial class InteropCorpus
{
    public const string SkipVariable = "TIFFNET_SKIP_INTEROP";

    private static readonly Lazy<Manifest> Loaded = new(Load);

    public static int Width => Loaded.Value.Width;

    public static int Height => Loaded.Value.Height;

    public static IReadOnlyDictionary<string, string> Datasets => Loaded.Value.Datasets;

    public static IReadOnlyList<InteropFile> Files => Loaded.Value.Files;

    public static bool Skipped => Environment.GetEnvironmentVariable(SkipVariable) == "1";

    /// <summary>
    /// Requires the corpus: a test that needs it is skipped (reported as such) when TIFFNET_SKIP_INTEROP=1, and fails when the
    /// corpus is missing without it, so that a job that should have generated it does not pass without reading it.
    /// </summary>
    public static void RequireGenerated()
    {
        if (Directory.Exists(Folder))
        {
            return;
        }

        Skip.If(Skipped, $"The interop corpus is not generated ({SkipVariable}=1).");
        Assert.Fail($"The interop corpus is not generated in {Folder}: run python -I tools/make_fixtures.py "
            + $"(pip install -r tools/requirements-interop.txt), or set {SkipVariable}=1 to skip.");
    }

    /// <summary>The folder of the generated files: <c>TIFFNET_INTEROP_DIR</c>, or the default of the script in the repository.</summary>
    public static string Folder => Environment.GetEnvironmentVariable("TIFFNET_INTEROP_DIR") is { Length: > 0 } dir
        ? dir
        : Path.Combine(RepositoryRoot(), "tests", "Fixtures", "interop", "generated");

    public static InteropFile Parse(string name, IReadOnlyDictionary<string, string> datasets, IReadOnlyCollection<string> supported,
        IReadOnlyDictionary<string, string>? lossy = null)
    {
        var m = FileName().Match(name);
        if (!m.Success)
        {
            throw new FormatException($"'{name}' does not follow the naming of tools/make_fixtures.py.");
        }

        string type = m.Groups["type"].Value;
        int channels = int.Parse(m.Groups["channels"].Value, CultureInfo.InvariantCulture);
        string compression = m.Groups["compression"].Value;
        var extra = m.Groups["extra"].Value switch
        {
            "" => InteropExtraKind.None,
            "u" => InteropExtraKind.Unspecified,
            "a" => InteropExtraKind.AssociatedAlpha,
            _ => InteropExtraKind.UnassociatedAlpha,
        };
        return new InteropFile(name, type, channels, extra, compression,
            Predictor: m.Groups["p"].Value == "1", Tiled: m.Groups["t"].Value == "1", Planar: m.Groups["pl"].Value == "1",
            BigTiff: m.Groups["b"].Value == "1", BigEndian: m.Groups["order"].Value == "be",
            Supported: supported.Contains(compression),
            SamplesSha256: lossy is not null && lossy.TryGetValue(name, out string? own) ? own : datasets[$"{type}-c{channels}"]);
    }

    private static Manifest Load()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "interop", "manifest.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;
        var datasets = root.GetProperty("datasets").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString()!);
        var supported = root.GetProperty("supportedCompressions").EnumerateArray().Select(e => e.GetString()!).ToHashSet();
        // A lossy file (JPEG) has its own samples, those tifffile decodes from it.
        var lossy = root.TryGetProperty("jpeg", out var jpeg) ? jpeg.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString()!) : [];
        var files = root.GetProperty("files").EnumerateArray().Select(e => Parse(e.GetString()!, datasets, supported, lossy)).ToList();
        return new Manifest(root.GetProperty("width").GetInt32(), root.GetProperty("height").GetInt32(), datasets, files);
    }

    private static string RepositoryRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Eleris.TiffNet.slnx")))
            {
                return dir.FullName;
            }
        }

        throw new DirectoryNotFoundException("Eleris.TiffNet.slnx was not found above " + AppContext.BaseDirectory);
    }

    [GeneratedRegex(@"^(?<type>[a-z0-9]+)-c(?<channels>\d)(?<extra>[uas]?)-(?<compression>[a-z0-9]+)-p(?<p>[01])-t(?<t>[01])-pl(?<pl>[01])-b(?<b>[01])-(?<order>le|be)\.tif$")]
    private static partial Regex FileName();

    private sealed record Manifest(int Width, int Height, Dictionary<string, string> Datasets, List<InteropFile> Files);
}
