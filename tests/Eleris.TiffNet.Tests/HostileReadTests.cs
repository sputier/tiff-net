using Eleris.TiffNet.Tests.Fixtures;

namespace Eleris.TiffNet.Tests;

/// <summary>
/// The hostile gate: every hostile file, opened and then read whole (every image and sub-image, raw), fails with the outcome its
/// manifest expects, within its time and allocation bounds.
/// </summary>
[Collection(nameof(Measurement))]
public class HostileReadTests
{
    /// <summary>
    /// The JPEG decoder is not written yet (issues #30 to #35): until then its hostile files are refused as not supported, an
    /// outcome the gate accepts for them only.
    /// </summary>
    private static string Expected(HostileCase hostile) =>
        hostile.ToString().Contains("jpeg", StringComparison.Ordinal) ? $"{hostile.Outcome}|NotSupportedException" : hostile.Outcome;

    [Fact]
    public void Every_hostile_file_read_whole_fails_as_expected_within_its_bounds()
    {
        Assert.True(HostileAssert.Cases.Count >= 35, $"only {HostileAssert.Cases.Count} hostile files");
        foreach (var hostile in HostileAssert.Cases)
        {
            HostileAssert.FailsWithinBounds(() => ReadWhole(hostile), Expected(hostile), hostile.MaxTime, hostile.MaxAllocatedBytes, hostile.ToString());
        }
    }

    private static void ReadWhole(HostileCase hostile)
    {
        using var file = TiffFile.Open(hostile.Path, hostile.Options);
        var pending = new Stack<TiffImage>(file);
        while (pending.TryPop(out var image))
        {
            _ = PixelTests.RawBytes(image);
            foreach (var sub in image.SubImages)
            {
                pending.Push(sub);
            }
        }
    }
}
