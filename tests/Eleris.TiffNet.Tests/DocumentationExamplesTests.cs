using Eleris.TiffNet.Examples;

namespace Eleris.TiffNet.Tests;

// The samples of the guide are compiled with the library; these tests run them, so that a sample that no longer works fails the
// build instead of misleading a reader.
[Collection(nameof(ConsoleRedirection))]
public class DocumentationExamplesTests
{
    [Fact]
    public void Every_example_of_the_guide_runs()
    {
        var started = new List<string>();

        Capture(() => ExampleRunner.RunAll(started.Add));

        Assert.Equal(ExampleRunner.Names, started);
        Assert.NotEmpty(started);
    }

    [Fact]
    public void The_examples_print_what_the_guide_says()
    {
        string text = Capture(ReliabilityExamples.TooLargeOrDamaged);

        Assert.Contains("The file is damaged: The image declares more pixels than the file holds.", text, StringComparison.Ordinal);
        Assert.Contains("The file is too large for this device (MaxPixelBytes).", text, StringComparison.Ordinal);
        Assert.Contains("Pixels: 256 MB, threads: 2", Capture(ReliabilityExamples.UntrustedFiles), StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_guide_examples_print_what_their_pages_show()
    {
        Assert.Contains("3 x 2, UInt16: 0, 1000, 2000, 3000, 4000, 65535", Capture(GettingStartedExamples.FirstImage), StringComparison.Ordinal);

        string pages = Capture(FilesExamples.Pages);
        Assert.Contains("64 x 48, 1 channel(s), Deflate: readable", pages, StringComparison.Ordinal);
        Assert.Contains("8 x 6, 3 channel(s), Deflate: readable", pages, StringComparison.Ordinal);
        Assert.Contains("Primary: 64 x 48", pages, StringComparison.Ordinal);
        Assert.Contains("1 page(s) read from a stream", Capture(FilesExamples.FromStream), StringComparison.Ordinal);

        string conversions = Capture(PixelExamples.Conversions);
        Assert.Contains("111 = 111 = 111", conversions, StringComparison.Ordinal);
        Assert.Contains("Refused: ", conversions, StringComparison.Ordinal);
        Assert.Contains("region 96, channel 1200, cube 3600 samples", Capture(PixelExamples.RegionsAndChannels), StringComparison.Ordinal);
        Assert.Contains("3600 and 3600 samples", await CaptureAsync(PixelExamples.BuffersAndAsync), StringComparison.Ordinal);

        string tags = Capture(MetadataExamples.Tags);
        Assert.Contains("OBJECT  = 'M42     '", tags, StringComparison.Ordinal);
        Assert.Contains("Urania, 300 dpi, a private tag", tags, StringComparison.Ordinal);

        Assert.Equal("12", Capture(LargeImageExamples.Mosaic).Trim());
        Assert.Contains("read on 2 threads, write on 1", Capture(ThreadExamples.Limits), StringComparison.Ordinal);
    }

    private static async Task<string> CaptureAsync(Func<Task> action)
    {
        var previous = Console.Out;
        var writer = new StringWriter();
        Console.SetOut(writer);
        try
        {
            await action();
        }
        finally
        {
            Console.SetOut(previous);
        }

        return writer.ToString();
    }

    private static string Capture(Action action)
    {
        var previous = Console.Out;
        var writer = new StringWriter();
        Console.SetOut(writer);
        try
        {
            action();
        }
        finally
        {
            Console.SetOut(previous);
        }

        return writer.ToString();
    }
}

/// <summary>Tests that redirect the console do not run in parallel with each other.</summary>
[CollectionDefinition(nameof(ConsoleRedirection), DisableParallelization = true)]
public sealed class ConsoleRedirection;
