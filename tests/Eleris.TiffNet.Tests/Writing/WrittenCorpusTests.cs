using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;

namespace Eleris.TiffNet.Tests.Writing;

/// <summary>
/// The written corpus: every combination of sample type, channels, compression, predictor, layout, planar configuration and
/// BigTIFF, written by the library into <c>TIFFNET_WRITTEN_DIR</c> with the SHA-256 of the samples tifffile must read from each
/// (<c>expected.json</c>). tools/check_written.py reads them back with tifffile and Pillow; the interop job of the CI runs both.
/// </summary>
public class WrittenCorpusTests
{
    public const string FolderVariable = "TIFFNET_WRITTEN_DIR";

    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    private static readonly TiffCompression[] Compressions = [TiffCompression.None, TiffCompression.PackBits, TiffCompression.Lzw, TiffCompression.Deflate];

    [SkippableFact]
    public void The_written_corpus_is_written_with_its_expectations()
    {
        string? folder = Environment.GetEnvironmentVariable(FolderVariable);
        Skip.If(string.IsNullOrEmpty(folder), $"Only with {FolderVariable} set (the interop job).");

        Directory.CreateDirectory(folder);
        var expected = new SortedDictionary<string, string>(StringComparer.Ordinal);
        Write<byte>("uint8", folder, expected);
        Write<sbyte>("int8", folder, expected);
        Write<ushort>("uint16", folder, expected);
        Write<short>("int16", folder, expected);
        Write<uint>("uint32", folder, expected);
        Write<int>("int32", folder, expected);
        Write<ulong>("uint64", folder, expected);
        Write<long>("int64", folder, expected);
        Write<Half>("float16", folder, expected);
        Write<float>("float32", folder, expected);
        Write<double>("float64", folder, expected);
        WriteLarge(folder, expected);
        WriteAlphaMosaic(folder, expected);
        File.WriteAllText(Path.Combine(folder, "expected.json"), JsonSerializer.Serialize(expected, Indented));

        Assert.True(expected.Count > 1800, $"only {expected.Count} files");
    }

    /// <summary>A mosaic written piece by piece with an unassociated alpha (tools/check_written.py checks its ExtraSamples).</summary>
    private static void WriteAlphaMosaic(string folder, SortedDictionary<string, string> expected)
    {
        byte[] pixels = [.. Enumerable.Range(0, 64 * 48 * 4).Select(i => (byte)(i * 7))];
        const string name = "writer-rgba-unassociated.tif";
        using (var writer = TiffImageWriter.Create(Path.Combine(folder, name), 64, 48, TiffSampleType.UInt8, 4, TiffColorModel.Rgb,
            options: TiffWriteOptions.Default with { Layout = TiffLayout.Tiles, TileSize = 32 }))
        {
            writer.SetExtraChannels(TiffExtraChannelKind.UnassociatedAlpha);
            for (int ty = 0; ty < 2; ty++)
            {
                for (int tx = 0; tx < 2; tx++)
                {
                    int h = Math.Min(32, 48 - ty * 32);
                    byte[] tile = new byte[32 * h * 4];
                    for (int r = 0; r < h; r++)
                    {
                        pixels.AsSpan(((ty * 32 + r) * 64 + tx * 32) * 4, 32 * 4).CopyTo(tile.AsSpan(r * 32 * 4));
                    }

                    writer.WriteTile<byte>(tx, ty, tile);
                }
            }

            writer.Complete();
        }

        expected[name] = Convert.ToHexString(SHA256.HashData(pixels));
    }

    /// <summary>Images of many strips and several bands of rows (the blocks small images never reach), each layout and order.</summary>
    private static void WriteLarge(string folder, SortedDictionary<string, string> expected)
    {
        var image = BandWritingTests.Gradient(4096, 2100, 3);
        foreach (var (name, options) in new[]
        {
            ("large-u8-c3-deflate-interleaved.tif", TiffWriteOptions.Default),
            ("large-u8-c3-deflate-planar.tif", TiffWriteOptions.Default with { PlanarConfiguration = TiffPixelOrder.Planar }),
            ("large-u8-c3-lzw-planar-threads1.tif", TiffWriteOptions.Default with
                { Compression = TiffCompression.Lzw, PlanarConfiguration = TiffPixelOrder.Planar, MaxDegreeOfParallelism = 1 }),
            ("large-u8-c3-packbits-tiles.tif", TiffWriteOptions.Default with { Compression = TiffCompression.PackBits, Layout = TiffLayout.Tiles, TileSize = 256 }),
        })
        {
            new TiffFile().Add(image).Save(Path.Combine(folder, name), options);
            expected[name] = Convert.ToHexString(SHA256.HashData(image.ReadRawPixels<byte>(options.PlanarConfiguration)));
        }
    }

    private static void Write<T>(string type, string folder, SortedDictionary<string, string> expected) where T : unmanaged, System.Numerics.INumber<T>
    {
        foreach (int channels in new[] { 1, 2, 3, 4 })
        {
            var image = DirectoryWriterTests.Sample<T>(53, 37, channels, channels * 100 + type.Length);
            foreach (var compression in Compressions)
            {
                foreach (var predictor in new[] { TiffPredictor.Auto, TiffPredictor.None })
                {
                    if (predictor == TiffPredictor.Auto && compression is not (TiffCompression.Lzw or TiffCompression.Deflate))
                    {
                        continue;
                    }

                    foreach (var layout in new[] { TiffLayout.Strips, TiffLayout.Tiles })
                    {
                        foreach (var order in channels == 1 ? new[] { TiffPixelOrder.Interleaved } : [TiffPixelOrder.Interleaved, TiffPixelOrder.Planar])
                        {
                            foreach (var bigTiff in new[] { TiffBigTiff.Auto, TiffBigTiff.Always })
                            {
                                var options = TiffWriteOptions.Default with
                                {
                                    Compression = compression, Predictor = predictor, Layout = layout, PlanarConfiguration = order,
                                    BigTiff = bigTiff, TileSize = 16,
                                };
                                string name = $"{type}-c{channels}-{compression.ToString().ToLowerInvariant()}-p{(predictor == TiffPredictor.Auto ? 1 : 0)}"
                                    + $"-t{(layout == TiffLayout.Tiles ? 1 : 0)}-pl{(order == TiffPixelOrder.Planar ? 1 : 0)}-b{(bigTiff == TiffBigTiff.Always ? 1 : 0)}.tif";
                                new TiffFile().Add(image).Save(Path.Combine(folder, name), options);

                                // tifffile gives planar files as [c][y][x], the others as [y][x][c]: the samples in that order.
                                T[] samples = image.ReadRawPixels<T>(order);
                                expected[name] = Convert.ToHexString(SHA256.HashData(MemoryMarshal.AsBytes(samples.AsSpan())));
                            }
                        }
                    }
                }
            }
        }
    }
}
