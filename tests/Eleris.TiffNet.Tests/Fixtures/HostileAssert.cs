using System.Diagnostics;
using System.Text.Json;
using Xunit.Sdk;

namespace Eleris.TiffNet.Tests.Fixtures;

/// <summary>One hostile file of tests/Fixtures/hostile and what reading it must do.</summary>
internal sealed record HostileCase(string Path, string Finding, string Attack, string Outcome, TimeSpan MaxTime, long MaxAllocatedBytes)
{
    public override string ToString() => System.IO.Path.GetFileName(Path);
}

/// <summary>
/// Asserts that reading a hostile input fails the way the design says, quickly and without allocating much: the exception
/// expected by the manifest ("TiffException", "TiffException:MaxPixelBytes" for a limit, "NotSupportedException"), within a
/// time and an allocation bound. Allocations are counted on every thread (decoding is parallel), so the tests that use it belong
/// to the <see cref="Measurement"/> collection, which does not run in parallel with other tests.
/// </summary>
internal static class HostileAssert
{
    private static readonly Lazy<IReadOnlyList<HostileCase>> Loaded = new(Load);

    public static string Folder => System.IO.Path.Combine(AppContext.BaseDirectory, "Fixtures", "hostile");

    /// <summary>Every hostile file: the files of the review and the crafted ones.</summary>
    public static IReadOnlyList<HostileCase> Cases => Loaded.Value;

    public static void FailsWithinBounds(HostileCase hostile, Action read) =>
        FailsWithinBounds(read, hostile.Outcome, hostile.MaxTime, hostile.MaxAllocatedBytes, hostile.ToString());

    public static void FailsWithinBounds(Action read, string outcome, TimeSpan maxTime, long maxAllocatedBytes, string what)
    {
        string[] parts = outcome.Split(':', 2);
        string expectedType = parts[0];
        string? expectedLimit = parts.Length > 1 ? parts[1] : null;

        GC.Collect();
        long before = GC.GetTotalAllocatedBytes(precise: true);
        var clock = Stopwatch.StartNew();
        Exception? thrown = null;
        try
        {
            read();
        }
        catch (Exception ex)
        {
            thrown = ex;
        }

        clock.Stop();
        long allocated = GC.GetTotalAllocatedBytes(precise: true) - before;

        if (thrown is null)
        {
            throw new XunitException($"{what}: no exception, {outcome} expected.");
        }

        if (thrown.GetType().Name != expectedType)
        {
            throw new XunitException($"{what}: {thrown.GetType().Name} ({thrown.Message}) instead of {outcome}.");
        }

        if (expectedLimit is not null && (thrown as TiffException)?.Limit != expectedLimit)
        {
            throw new XunitException($"{what}: the limit is {(thrown as TiffException)?.Limit ?? "none"} instead of {expectedLimit} ({thrown.Message}).");
        }

        if (clock.Elapsed > maxTime)
        {
            throw new XunitException($"{what}: failed after {clock.Elapsed.TotalSeconds:F2} s, more than {maxTime.TotalSeconds:F2} s.");
        }

        if (allocated > maxAllocatedBytes)
        {
            throw new XunitException($"{what}: {allocated / 1048576.0:F1} MB allocated, more than {maxAllocatedBytes / 1048576.0:F1} MB.");
        }
    }

    private static List<HostileCase> Load()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(System.IO.Path.Combine(Folder, "manifest.json")));
        var cases = new List<HostileCase>();
        foreach (string part in new[] { "review", "crafted" })
        {
            foreach (var file in document.RootElement.GetProperty(part).EnumerateObject())
            {
                var v = file.Value;
                cases.Add(new HostileCase(System.IO.Path.Combine(Folder, part, file.Name), v.GetProperty("finding").GetString()!,
                    v.GetProperty("attack").GetString()!, v.GetProperty("outcome").GetString()!,
                    TimeSpan.FromSeconds(v.GetProperty("maxSeconds").GetDouble()),
                    (long)(v.GetProperty("maxAllocatedMB").GetDouble() * 1048576)));
            }
        }

        return cases;
    }
}

/// <summary>Tests that measure time or allocations: they do not run in parallel with other tests.</summary>
[CollectionDefinition(nameof(Measurement), DisableParallelization = true)]
public sealed class Measurement;
