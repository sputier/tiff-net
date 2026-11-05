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
