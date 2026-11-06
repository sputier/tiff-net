namespace Eleris.TiffNet.Tests.Interop;

// The corpus is the independent oracle of the readers: these tests make sure it is complete and well described. The tests that
// read its files come with the readers.
[Trait("Category", "Interop")]
public class InteropCorpusTests
{
    [Fact]
    public void The_manifest_covers_every_dataset_and_storage_variant()
    {
        var files = InteropCorpus.Files;

        Assert.Equal(44, InteropCorpus.Datasets.Count);   // 11 sample types x 1, 2, 3, 4 channels
        Assert.Equal(files.Count, files.Select(f => f.Name).Distinct().Count());
        Assert.All(InteropCorpus.Datasets.Keys, dataset => Assert.Contains(files, f => $"{f.SampleType}-c{f.Channels}" == dataset));
        Assert.All(InteropCorpus.Datasets.Values, hash => Assert.Matches("^[0-9A-F]{64}$", hash));

        // The variants that matter most: every layout in both byte orders, classic and BigTIFF (the first implementation could
        // not read big-endian BigTIFF at all).
        foreach (bool bigTiff in new[] { false, true })
        {
            foreach (bool bigEndian in new[] { false, true })
            {
                Assert.Contains(files, f => f.BigTiff == bigTiff && f.BigEndian == bigEndian && f.Tiled && f.Planar && f.Predictor
                    && f.SampleType == "float32" && f.Compression == "zlib");
            }
        }

        Assert.Contains(files, f => !f.Supported);   // zstd and lzma: refused by design
        Assert.True(files.Count > 5000, $"only {files.Count} files");

        // What tifffile cannot write by itself is encoded by the generator: predictor 2 on 64-bit integers, both byte orders.
        Assert.Contains(files, f => f.SampleType == "int64" && f.Predictor && f.BigEndian && f.Planar);
        Assert.Contains(files, f => f.SampleType == "uint64" && f.Predictor && !f.BigEndian && f.Compression == "lzw");

        // Every kind of extra channel, which the readers must report.
        foreach (var kind in new[] { InteropExtraKind.Unspecified, InteropExtraKind.AssociatedAlpha, InteropExtraKind.UnassociatedAlpha })
        {
            Assert.Contains(files, f => f.ExtraKind == kind && f.Channels == 2);
            Assert.Contains(files, f => f.ExtraKind == kind && f.Channels == 4);
        }

        Assert.All(files, f => Assert.Equal(f.Channels is 2 or 4, f.ExtraKind != InteropExtraKind.None));
    }

    [Fact]
    public void A_file_name_gives_the_parameters_of_the_file()
    {
        var datasets = new Dictionary<string, string> { ["uint16-c3"] = new string('A', 64) };

        var file = InteropCorpus.Parse("uint16-c3-zlib-p1-t0-pl1-b1-be.tif", datasets, ["zlib"]);
        var alpha = InteropCorpus.Parse("uint16-c3a-zlib-p0-t0-pl0-b0-le.tif", datasets, ["zlib"]);

        Assert.Equal(new InteropFile("uint16-c3-zlib-p1-t0-pl1-b1-be.tif", "uint16", 3, InteropExtraKind.None, "zlib", Predictor: true,
            Tiled: false, Planar: true, BigTiff: true, BigEndian: true, Supported: true, new string('A', 64)), file);
        Assert.Equal(InteropExtraKind.AssociatedAlpha, alpha.ExtraKind);
        Assert.Throws<FormatException>(() => InteropCorpus.Parse("other.tif", datasets, ["zlib"]));
    }

    [SkippableFact]
    [Trait("Category", "Interop")]
    public void The_generated_corpus_matches_the_manifest()
    {
        InteropCorpus.RequireGenerated();

        var missing = InteropCorpus.Files.Where(f => !File.Exists(Path.Combine(InteropCorpus.Folder, f.Name))).Select(f => f.Name).ToList();
        Assert.True(missing.Count == 0, $"{missing.Count} files missing, first: {missing.FirstOrDefault()}");
    }
}
