namespace Eleris.TiffNet.Examples;

/// <summary>The examples of the guide pages, each self-contained: it writes the files it reads, in the temporary folder.</summary>
public static class GettingStartedExamples
{
    /// <summary>A first image: create, save, open, read.</summary>
    public static void FirstImage()
    {
        string path = Path.Combine(Path.GetTempPath(), "first.tif");
        #region Example:GettingStartedExamples.FirstImage
        // 3 x 2 grey pixels of 16 bits, rows from the top.
        ushort[] pixels = [0, 1000, 2000, 3000, 4000, 65535];
        var image = TiffImage.Create<ushort>(pixels, width: 3, height: 2);
        new TiffFile().Add(image).Save(path);   // Deflate with a predictor, by default

        using var file = TiffFile.Open(path);
        float[] values = file.Primary.ReadPixels<float>();
        Console.WriteLine($"{file.Primary.Width} x {file.Primary.Height}, {file.Primary.SampleType}: {string.Join(", ", values)}");
        #endregion
        File.Delete(path);
    }
}

/// <summary>The examples of the page "Files and pages".</summary>
public static class FilesExamples
{
    /// <summary>The pages of a file and the main image.</summary>
    public static void Pages()
    {
        string path = Path.Combine(Path.GetTempPath(), "pages.tif");
        new TiffFile()
            .Add(TiffImage.Create<byte>(new byte[64 * 48], 64, 48))
            .Add(TiffImage.Create<float>(new float[8 * 6 * 3], 8, 6, 3, TiffColorModel.Rgb))
            .Save(path);
        #region Example:FilesExamples.Pages
        using var file = TiffFile.Open(path);
        foreach (var page in file)
        {
            string support = page.UnsupportedReason ?? "readable";
            Console.WriteLine($"{page.Width} x {page.Height}, {page.Channels} channel(s), {page.Storage!.Compression}: {support}");
        }

        // The first page that is neither a thumbnail nor a mask.
        Console.WriteLine($"Primary: {file.Primary.Width} x {file.Primary.Height}");
        #endregion
        File.Delete(path);
    }

    /// <summary>A file from a stream, in memory.</summary>
    public static void FromStream()
    {
        using var source = new MemoryStream();
        new TiffFile().Add(TiffImage.Create<byte>(new byte[16], 4, 4)).Save(source);
        source.Position = 0;
        #region Example:FilesExamples.FromStream
        // The stream is read into memory (at most MaxInputBytes): it can be closed afterwards, and need not seek.
        using var file = TiffFile.Read(source);
        Console.WriteLine($"{file.Count} page(s) read from a stream");
        #endregion
    }
}

/// <summary>The examples of the page "Reading pixels".</summary>
public static class PixelExamples
{
    private static string Sample()
    {
        string path = Path.Combine(Path.GetTempPath(), "pixels.tif");
        ushort[] rgb = new ushort[40 * 30 * 3];
        for (int i = 0; i < rgb.Length; i++)
        {
            rgb[i] = (ushort)(i * 37 % 65536);
        }

        new TiffFile().Add(TiffImage.Create<ushort>(rgb, 40, 30, 3, TiffColorModel.Rgb)).Save(path);
        return path;
    }

    /// <summary>Stored values, converted values, normalised values.</summary>
    public static void Conversions()
    {
        string path = Sample();
        #region Example:PixelExamples.Conversions
        using var file = TiffFile.Open(path);
        var image = file.Primary;                                            // u16 RGB

        ushort[] raw = image.ReadRawPixels<ushort>();                         // exactly the stored type
        int[] wider = image.ReadPixels<int>();                                // any type that holds every value
        float[] unit = image.ReadPixels<float>(TiffPixelScale.Normalized);    // divided by 65535: 0 to 1
        Console.WriteLine($"{raw[3]} = {wider[3]} = {unit[3] * 65535:F0}");

        try
        {
            _ = image.ReadPixels<byte>();                                      // 16-bit values do not fit in a byte
        }
        catch (InvalidOperationException e)
        {
            Console.WriteLine($"Refused: {e.Message}");
        }
        #endregion
        File.Delete(path);
    }

    /// <summary>A region, a channel, planar order.</summary>
    public static void RegionsAndChannels()
    {
        string path = Sample();
        #region Example:PixelExamples.RegionsAndChannels
        using var file = TiffFile.Open(path);
        var image = file.Primary;

        // Only the strips or tiles under the region are read.
        float[] crop = image.ReadRegion<float>(x: 10, y: 5, width: 8, height: 4);

        // One channel: for an image stored in planes, only its plane is read.
        float[] green = image.ReadChannel<float>(1);

        // [c][y][x], the order of a FITS cube (NAXIS1 = width, NAXIS2 = height, NAXIS3 = channels).
        float[] cube = image.ReadPixels<float>(order: TiffPixelOrder.Planar);

        Console.WriteLine($"region {crop.Length}, channel {green.Length}, cube {cube.Length} samples");
        #endregion
        File.Delete(path);
    }

    /// <summary>Into a buffer of the application, and asynchronously with cancellation.</summary>
    public static async Task BuffersAndAsync()
    {
        string path = Sample();
        #region Example:PixelExamples.BuffersAndAsync
        using var file = TiffFile.Open(path);
        var image = file.Primary;

        // No array allocated for the pixels: the caller's buffer, decoded on every core (a Span<T> is filled on the calling thread).
        var buffer = new double[image.Width * image.Height * image.Channels];
        image.ReadPixels<double>(buffer);

        // On a thread of the pool, cancellable between strips or tiles.
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        float[] pixels = await image.ReadPixelsAsync<float>(cancellationToken: cancellation.Token);
        Console.WriteLine($"{buffer.Length} and {pixels.Length} samples");
        #endregion
        File.Delete(path);
    }
}

/// <summary>The examples of the page "Metadata and Astro-TIFF".</summary>
public static class MetadataExamples
{
    /// <summary>Typed and generic tags, kept when the file is saved again.</summary>
    public static void Tags()
    {
        string path = Path.Combine(Path.GetTempPath(), "metadata.tif");
        #region Example:MetadataExamples.Tags
        var image = TiffImage.Create<float>(new float[16], 4, 4);
        var metadata = image.Metadata;
        metadata.Description = "SIMPLE  =                    T\nOBJECT  = 'M42     '\nEND";   // an Astro-TIFF FITS header
        metadata.Software = "Urania";
        metadata.Resolution = new TiffResolution(300, 300, TiffResolutionUnit.Inch);
        metadata.Set(TiffTag.FromString(65000, "a private tag"));
        new TiffFile().Add(image).Save(path);

        using var file = TiffFile.Open(path);
        var read = file.Primary.Metadata;
        Console.WriteLine(read.Description!.Split('\n')[1]);
        Console.WriteLine($"{read.Software}, {read.Resolution!.X} dpi, {read[65000]!.AsString()}");
        #endregion
        File.Delete(path);
    }
}

/// <summary>The examples of the page "Writing".</summary>
public static class WritingExamples
{
    /// <summary>Options: compression, predictor, layout, BigTIFF.</summary>
    public static void Options()
    {
        string path = Path.Combine(Path.GetTempPath(), "options.tif");
        var image = TiffImage.Create<float>(new float[512 * 512 * 3], 512, 512, 3, TiffColorModel.Rgb);
        #region Example:WritingExamples.Options
        var options = TiffWriteOptions.Default with
        {
            Compression = TiffCompression.Deflate,          // Deflate (default), LZW, PackBits or None
            Level = TiffCompressionLevel.Fastest,           // of Deflate: faster, files larger
            Layout = TiffLayout.Tiles,                      // 256 x 256 tiles (TileSize)
            PlanarConfiguration = TiffPixelOrder.Planar,    // each channel in its own plane
        };
        new TiffFile().Add(image).Save(path, options);
        #endregion
        File.Delete(path);
    }

    /// <summary>Saving an image as its file stored it.</summary>
    public static void AsStored()
    {
        string source = Path.Combine(Path.GetTempPath(), "stored.tif");
        string target = Path.Combine(Path.GetTempPath(), "edited.tif");
        new TiffFile().Add(TiffImage.Create<ushort>(new ushort[64 * 64], 64, 64))
            .Save(source, TiffWriteOptions.Default with { Compression = TiffCompression.Lzw, Layout = TiffLayout.Tiles, TileSize = 32 });
        #region Example:WritingExamples.AsStored
        using var file = TiffFile.Open(source);
        file.Primary.Metadata.Software = "Urania";

        // Same compression, predictor, layout and BigTIFF as the original (JPEG becomes Deflate: it is not written).
        file.Save(target, TiffWriteOptions.From(file.Primary.Storage!));
        #endregion
        File.Delete(source);
        File.Delete(target);
    }
}

/// <summary>The examples of the page "Large images".</summary>
public static class LargeImageExamples
{
    /// <summary>A mosaic written tile by tile.</summary>
    public static void Mosaic()
    {
        string path = Path.Combine(Path.GetTempPath(), "mosaic.tif");
        #region Example:LargeImageExamples.Mosaic
        // 1 024 x 768 f32 grey, written tile by tile in any order, without the image in memory (up to 2 147 483 647 pixels a
        // side; BigTIFF when the file could exceed 4 GB).
        var options = TiffWriteOptions.Default with { Layout = TiffLayout.Tiles, TileSize = 256 };
        using (var writer = TiffImageWriter.Create(path, 1024, 768, TiffSampleType.Float32, options: options))
        {
            var tile = new float[256 * 256];
            for (int ty = 2; ty >= 0; ty--)
            {
                for (int tx = 0; tx < 4; tx++)
                {
                    Array.Fill(tile, tx + 10 * ty);                // the panel computed for this tile
                    writer.WriteTile<float>(tx, ty, tile);
                }
            }

            writer.Complete();                                     // refuses missing tiles; without it, nothing is kept
        }

        using var file = TiffFile.Open(path);
        Console.WriteLine($"{file.Primary.ReadRegion<float>(600, 300, 1, 1)[0]}");   // tile (2, 1)
        #endregion
        File.Delete(path);
    }
}

/// <summary>The examples of the page "Threads and memory".</summary>
public static class ThreadExamples
{
    /// <summary>Limiting the threads and the memory.</summary>
    public static void Limits()
    {
        #region Example:ThreadExamples.Limits
        // Reading: two decoding threads, at most 256 MB for the pixels of an image.
        var read = TiffReadOptions.Default with { MaxDegreeOfParallelism = 2, MaxPixelBytes = 256L << 20 };

        // Writing on the calling thread only.
        var write = TiffWriteOptions.Default with { MaxDegreeOfParallelism = 1 };

        Console.WriteLine($"read on {read.MaxDegreeOfParallelism} threads, write on {write.MaxDegreeOfParallelism}");
        #endregion
    }
}
