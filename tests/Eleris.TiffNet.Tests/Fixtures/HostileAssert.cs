using System.Diagnostics;
using System.Text.Json;
using Xunit.Sdk;

namespace Eleris.TiffNet.Tests.Fixtures;

/// <summary>One hostile file of tests/Fixtures/hostile, the options it is read with, and what reading it must do.</summary>
internal sealed record HostileCase(string Path, string Finding, string Attack, string Outcome, TiffReadOptions Options, TimeSpan MaxTime,
    long MaxAllocatedBytes)
{
    public override string ToString() => System.IO.Path.GetFileName(Path);
}

/// <summary>
/// Asserts that reading a hostile input fails the way the design says, quickly and without allocating much: the outcome expected
/// by the manifest, within a time and an allocation bound. An outcome is "TiffException" (damaged: no limit named),
/// "TiffException:MaxPixelBytes" (that limit), "TiffException:*" (any limit, or none), "NotSupportedException", or alternatives
/// separated by "|". Allocations are counted on every thread (decoding is parallel), so the tests that use it belong to the
/// <see cref="Measurement"/> collection, which does not run in parallel with other tests.
/// </summary>
internal static class HostileAssert
{
    private static readonly Lazy<IReadOnlyList<HostileCase>> Loaded = new(Load);

    public static string Folder => System.IO.Path.Combine(AppContext.BaseDirectory, "Fixtures", "hostile");

    /// <summary>Every hostile file: the files of the audit and the crafted ones.</summary>
    public static IReadOnlyList<HostileCase> Cases => Loaded.Value;

    public static void FailsWithinBounds(HostileCase hostile, Action read) =>
        FailsWithinBounds(read, hostile.Outcome, hostile.MaxTime, hostile.MaxAllocatedBytes, hostile.ToString());

    /// <summary>
    /// For a step that does not reach the defect of every file (opening a file whose defect is in its pixels): the step may
    /// succeed, but if it fails it must fail as expected, and either way within the bounds.
    /// </summary>
    public static void OpensOrFailsWithinBounds(HostileCase hostile, Action read) =>
        FailsWithinBounds(read, hostile.Outcome, hostile.MaxTime, hostile.MaxAllocatedBytes, hostile.ToString(), successAllowed: true);

    /// <summary>The time bounds scale with TIFFNET_TIME_FACTOR (1 by default; a slow or shared runner can set more).</summary>
    private static readonly double TimeFactor =
        double.TryParse(Environment.GetEnvironmentVariable("TIFFNET_TIME_FACTOR"), System.Globalization.CultureInfo.InvariantCulture, out double f) && f > 0 ? f : 1;

    public static void FailsWithinBounds(Action read, string outcome, TimeSpan maxTime, long maxAllocatedBytes, string what,
        bool successAllowed = false)
    {
        // A read over its time, with the expected outcome, is measured once more: the first run of a path after a build pays for
        // compiling it, which is not what the bound is about. Its outcome and its allocations (the larger) stand.
        maxTime *= TimeFactor;
        var (thrown, elapsed, allocated) = Measure(read);
        if (elapsed > maxTime && (thrown is null ? successAllowed : outcome.Split('|').Any(expected => Matches(expected, thrown))))
        {
            long first = allocated;
            (_, elapsed, allocated) = Measure(read);
            allocated = Math.Max(first, allocated);
        }

        if (thrown is null && !successAllowed)
        {
            throw new XunitException($"{what}: no exception, {outcome} expected.");
        }

        if (thrown is null)
        {
            CheckBounds(what, elapsed, maxTime, allocated, maxAllocatedBytes);
            return;
        }

        string actual = thrown is TiffException { Limit: { } limit } ? $"TiffException:{limit}" : thrown.GetType().Name;
        if (!outcome.Split('|').Any(expected => Matches(expected, thrown)))
        {
            throw new XunitException($"{what}: {actual} ({thrown.Message}) instead of {outcome}.");
        }

        CheckBounds(what, elapsed, maxTime, allocated, maxAllocatedBytes);
    }

    private static (Exception? Thrown, TimeSpan Elapsed, long Allocated) Measure(Action read)
    {
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
        return (thrown, clock.Elapsed, GC.GetTotalAllocatedBytes(precise: true) - before);
    }

    private static void CheckBounds(string what, TimeSpan elapsed, TimeSpan maxTime, long allocated, long maxAllocatedBytes)
    {
        if (elapsed > maxTime)
        {
            throw new XunitException($"{what}: took {elapsed.TotalSeconds:F2} s, more than {maxTime.TotalSeconds:F2} s.");
        }

        if (allocated > maxAllocatedBytes)
        {
            throw new XunitException($"{what}: {allocated / 1048576.0:F1} MB allocated, more than {maxAllocatedBytes / 1048576.0:F1} MB.");
        }
    }

    private static bool Matches(string expected, Exception thrown)
    {
        string[] parts = expected.Split(':', 2);
        if (thrown.GetType().Name != parts[0])
        {
            return false;
        }

        string? limit = (thrown as TiffException)?.Limit;
        return parts.Length == 1 ? limit is null : parts[1] == "*" || parts[1] == limit;
    }

    private static TiffReadOptions OptionsOf(JsonElement options)
    {
        var result = TiffReadOptions.Default;
        foreach (var option in options.EnumerateObject())
        {
            result = option.Name switch
            {
                nameof(TiffReadOptions.MaxPixelBytes) => result with { MaxPixelBytes = option.Value.GetInt64() },
                nameof(TiffReadOptions.MaxInputBytes) => result with { MaxInputBytes = option.Value.GetInt64() },
                nameof(TiffReadOptions.MaxTagBytes) => result with { MaxTagBytes = option.Value.GetInt64() },
                nameof(TiffReadOptions.MaxDirectories) => result with { MaxDirectories = option.Value.GetInt32() },
                nameof(TiffReadOptions.MaxTagsPerDirectory) => result with { MaxTagsPerDirectory = option.Value.GetInt32() },
                nameof(TiffReadOptions.MaxJpegScans) => result with { MaxJpegScans = option.Value.GetInt32() },
                _ => throw new InvalidDataException($"Unknown option {option.Name} in the hostile manifest."),
            };
        }

        return result;
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
                    v.GetProperty("attack").GetString()!, v.GetProperty("outcome").GetString()!, OptionsOf(v.GetProperty("options")),
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
