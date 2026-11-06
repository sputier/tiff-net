using Eleris.TiffNet;

// Writes and reads back an image with every compression, layout and sample type family, as an application compiled ahead of
// time would; exits with 1 when a sample differs.
string folder = Path.Combine(Path.GetTempPath(), $"tiffnet-aot-{Environment.ProcessId}");
Directory.CreateDirectory(folder);
int failures = 0;
try
{
    var random = new Random(52);
    ushort[] grey = [.. Enumerable.Range(0, 300 * 200).Select(_ => (ushort)random.Next(65536))];
    float[] rgb = [.. Enumerable.Range(0, 120 * 80 * 3).Select(_ => random.NextSingle())];
    var images = new[]
    {
        TiffImage.Create<ushort>(grey, 300, 200),
        TiffImage.Create<float>(rgb, 120, 80, 3, TiffColorModel.Rgb),
    };

    foreach (var compression in new[] { TiffCompression.None, TiffCompression.PackBits, TiffCompression.Lzw, TiffCompression.Deflate })
    {
        foreach (var layout in new[] { TiffLayout.Strips, TiffLayout.Tiles })
        {
            string path = Path.Combine(folder, $"{compression}-{layout}.tif");
            var options = TiffWriteOptions.Default with { Compression = compression, Layout = layout, TileSize = 64 };
            var file = new TiffFile();
            foreach (var image in images)
            {
                file.Add(image);
            }

            file.Save(path, options);
            using var back = TiffFile.Open(path);
            bool same = back[0].ReadRawPixels<ushort>().AsSpan().SequenceEqual(grey)
                && back[1].ReadRawPixels<float>().AsSpan().SequenceEqual(rgb)
                && back[1].ReadRegion<double>(10, 10, 5, 5, TiffPixelScale.Normalized).Length == 75;
            Console.WriteLine($"{compression,-9} {layout,-7} {(same ? "ok" : "DIFFERENT")}");
            failures += same ? 0 : 1;
        }
    }
}
finally
{
    Directory.Delete(folder, recursive: true);
}

Console.WriteLine(failures == 0 ? "All images read back as written." : $"{failures} image(s) differ.");
return failures == 0 ? 0 : 1;
