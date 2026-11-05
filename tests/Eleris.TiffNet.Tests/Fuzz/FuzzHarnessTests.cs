using Eleris.TiffNet.Fuzz;
using Eleris.TiffNet.Tests.Fixtures;

namespace Eleris.TiffNet.Tests.Fuzz;

// The fuzzer must be trustworthy before it finds anything: an input it reports must be reproducible from its seed and iteration,
// its mutations must never stop a campaign, and what it calls a crash must be a crash.
public class FuzzHarnessTests
{
    private static readonly string[] Seeds = Directory.GetFiles(DomainFixtures.Folder, "*.tif").Order(StringComparer.Ordinal).ToArray();

    [Fact]
    public void An_input_is_reproduced_from_its_seed_and_iteration()
    {
        var corpus = new Corpus(Seeds);
        var other = new Corpus(Seeds.Reverse().ToArray());

        for (long i = 0; i < 200; i++)
        {
            Assert.Equal(corpus.Input(7, i, MutationMode.Mixed), corpus.Input(7, i, MutationMode.Mixed));
        }

        Assert.NotEqual(corpus.Input(7, 0, MutationMode.Mixed), corpus.Input(8, 0, MutationMode.Mixed));
        // Loading sorts the files, so the order in which they are found does not change the inputs.
        Assert.Equal(Corpus.Load([DomainFixtures.Folder]).Input(3, 42, MutationMode.Directory), corpus.Input(3, 42, MutationMode.Directory));
        Assert.NotEqual(other.Input(3, 42, MutationMode.Directory), corpus.Input(3, 42, MutationMode.Directory));
    }

    [Theory]
    [InlineData("Mixed")]
    [InlineData("Directory")]
    [InlineData("Payload")]
    [InlineData("Havoc")]
    public void Mutations_never_throw_and_change_the_input(string modeName)
    {
        // The mode is internal to the fuzzer: the theory receives its name.
        var mode = Enum.Parse<MutationMode>(modeName);
        var corpus = new Corpus(Seeds);
        byte[][] seeds = Seeds.Select(File.ReadAllBytes).ToArray();
        int changed = 0;
        for (long i = 0; i < 2000; i++)
        {
            byte[] input = corpus.Input(11, i, mode);
            if (!seeds.Any(s => s.AsSpan().SequenceEqual(input)))
            {
                changed++;
            }
        }

        Assert.True(changed > 1900, $"only {changed} of 2000 inputs differ from their seed");
    }

    [Fact]
    public void Outcomes_are_classified()
    {
        string output = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var corpus = new Corpus(Seeds);

        Assert.Equal("ok", new Campaign(corpus, _ => { }, MutationMode.Mixed, 1, output).Execute([1]).Outcome);
        Assert.Equal("TiffException", new Campaign(corpus, _ => throw new TiffException("damaged"), MutationMode.Mixed, 1, output).Execute([1]).Outcome);
        Assert.Equal("TiffException:MaxPixelBytes",
            new Campaign(corpus, _ => throw new TiffException("big", "MaxPixelBytes"), MutationMode.Mixed, 1, output).Execute([1]).Outcome);
        Assert.Equal("NotSupportedException", new Campaign(corpus, _ => throw new NotSupportedException(), MutationMode.Mixed, 1, output).Execute([1]).Outcome);
        Assert.StartsWith("crash:InvalidDataException",
            new Campaign(corpus, _ => throw new InvalidDataException(), MutationMode.Mixed, 1, output).Execute([1]).Outcome, StringComparison.Ordinal);
    }

    [Fact]
    public void A_crashing_input_is_saved_with_its_seed_and_iteration()
    {
        string output = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            var corpus = new Corpus(Seeds);
            var campaign = new Campaign(corpus, input => { if (input.Length % 3 == 0) { throw new InvalidDataException(); } },
                MutationMode.Havoc, 5, output);

            long iterations = campaign.Run(TimeSpan.FromSeconds(30), maxIterations: 300);

            Assert.Equal(300, iterations);
            string[] saved = Directory.GetFiles(Path.Combine(output, "crash"), "*.tif");
            Assert.NotEmpty(saved);
            string note = File.ReadAllText(saved[0] + ".txt");
            var match = System.Text.RegularExpressions.Regex.Match(note, @"seed=5 iteration=(\d+) mode=Havoc");
            Assert.True(match.Success, note);
            Assert.Equal(File.ReadAllBytes(saved[0]), corpus.Input(5, long.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture), MutationMode.Havoc));
        }
        finally
        {
            Directory.Delete(output, recursive: true);
        }
    }
}
