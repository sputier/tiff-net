using System.Security.Cryptography;
using System.Text.Json;

namespace Eleris.TiffNet.Tests.Fixtures;

/// <summary>
/// The domain fixtures (tests/Fixtures/domain): small committed files written by Siril, tifffile and Pillow, described by
/// tools/make_domain_fixtures.py in manifest.json. A page's description is what tifffile reads; <c>Expect</c> holds what this
/// library must deliver when it differs (samples after orientation, palette or MinIsWhite, the primary page, a refusal).
/// </summary>
internal static class DomainFixtures
{
    private static readonly Lazy<IReadOnlyDictionary<string, JsonElement>> Loaded = new(Load);

    public static string Folder => Path.Combine(AppContext.BaseDirectory, "Fixtures", "domain");

    public static IReadOnlyDictionary<string, JsonElement> Files => Loaded.Value;

    public static string PathOf(string name) => Path.Combine(Folder, name);

    public static JsonElement Page(string name, int index = 0) => Files[name].GetProperty("pages")[index];

    public static JsonElement? Expect(string name) => Files[name].TryGetProperty("expect", out var e) ? e : null;

    public static string FileSha256(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    private static Dictionary<string, JsonElement> Load()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(Folder, "manifest.json")));
        return document.RootElement.GetProperty("files").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone());
    }
}
