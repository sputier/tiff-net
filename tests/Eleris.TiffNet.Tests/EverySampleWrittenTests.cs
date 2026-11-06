using System.Numerics;
using System.Runtime.InteropServices;
using Eleris.TiffNet.Internal.Pixels;
using Eleris.TiffNet.Tests.Interop;

namespace Eleris.TiffNet.Tests;

/// <summary>
/// Every sample of a read is written by the read: the arrays of results are allocated without being cleared, so a sample left
/// out would show what the memory held before. Each image of the corpora is read twice into buffers filled with different
/// bytes; a sample that differs between the two was not written.
/// </summary>
public class EverySampleWrittenTests
{
    [Fact]
    public void Every_read_of_every_image_of_the_corpora_writes_every_sample()
    {
        var folders = new List<string> { Path.Combine(AppContext.BaseDirectory, "Fixtures", "domain") };
        if (!InteropCorpus.Skipped && Directory.Exists(InteropCorpus.Folder))
        {
            folders.Add(InteropCorpus.Folder);
        }

        int images = 0;
        reads = 0;
        foreach (string path in folders.SelectMany(f => Directory.EnumerateFiles(f, "*.tif")))
        {
            TiffFile file;
            try
            {
                file = TiffFile.Open(path);
            }
            catch (Exception ex) when (ex is TiffException or NotSupportedException)
            {
                continue;
            }

            using (file)
            {
                foreach (var image in file.Where(i => i.UnsupportedReason is null && (long)i.Width * i.Height * i.Channels <= 1 << 22))
                {
                    images++;
                    foreach (var order in new[] { TiffPixelOrder.Interleaved, TiffPixelOrder.Planar })
                    {
                        Check<double>(image, TiffPixelScale.Normalized, order, path);
                        Check<float>(image, TiffPixelScale.Stored, order, path);
                        CheckRaw(image, order, path);
                    }

                    // Regions cutting blocks at their edges, and each channel: the requests the arrays of results serve.
                    var random = new Random(image.Width * 31 + image.Height);
                    for (int k = 0; k < 3; k++)
                    {
                        int x = random.Next(image.Width), y = random.Next(image.Height);
                        int w = random.Next(1, image.Width - x + 1), h = random.Next(1, image.Height - y + 1);
                        foreach (var order in new[] { TiffPixelOrder.Interleaved, TiffPixelOrder.Planar })
                        {
                            CheckRequest<double>(image, new PixelRequest(x, y, w, h, null, order, TiffPixelScale.Normalized), path);
                        }
                    }

                    for (int c = 0; c < image.Channels; c++)
                    {
                        CheckRequest<double>(image, new PixelRequest(0, 0, image.Width, image.Height, c, TiffPixelOrder.Interleaved, TiffPixelScale.Stored), path);
                    }
                }
            }
        }

        Assert.True(images > 20, $"only {images} images read");
        Assert.True(reads > images * 4, $"only {reads} reads succeeded for {images} images");
    }

    /// <summary>The samples of their own type: the copies and the direct paths.</summary>
    private static void CheckRaw(TiffImage image, TiffPixelOrder order, string path)
    {
        switch (image.SampleType)
        {
            case TiffSampleType.UInt8: Check<byte>(image, TiffPixelScale.Stored, order, path); break;
            case TiffSampleType.Int8: Check<sbyte>(image, TiffPixelScale.Stored, order, path); break;
            case TiffSampleType.UInt16: Check<ushort>(image, TiffPixelScale.Stored, order, path); break;
            case TiffSampleType.Int16: Check<short>(image, TiffPixelScale.Stored, order, path); break;
            case TiffSampleType.UInt32: Check<uint>(image, TiffPixelScale.Stored, order, path); break;
            case TiffSampleType.Int32: Check<int>(image, TiffPixelScale.Stored, order, path); break;
            case TiffSampleType.UInt64: Check<ulong>(image, TiffPixelScale.Stored, order, path); break;
            case TiffSampleType.Int64: Check<long>(image, TiffPixelScale.Stored, order, path); break;
            case TiffSampleType.Float16: Check<Half>(image, TiffPixelScale.Stored, order, path); break;
            case TiffSampleType.Float32: Check<float>(image, TiffPixelScale.Stored, order, path); break;
            default: Check<double>(image, TiffPixelScale.Stored, order, path); break;
        }
    }

    private static int reads;

    /// <summary>A request of any region or channel, through the path that the arrays of results take.</summary>
    private static void CheckRequest<T>(TiffImage image, PixelRequest request, string path) where T : unmanaged, INumber<T>
    {
        int count = (int)request.Count(image.Channels);
        var first = new T[count];
        var second = new T[count];
        MemoryMarshal.AsBytes(first.AsSpan()).Fill(0x55);
        MemoryMarshal.AsBytes(second.AsSpan()).Fill(0xAA);
        try
        {
            image.ReadInto<T>(request, first, CancellationToken.None);
            image.ReadInto<T>(request, second, CancellationToken.None);
        }
        catch (Exception ex) when (ex is TiffException)
        {
            return;
        }

        reads++;
        if (!MemoryMarshal.AsBytes(first.AsSpan()).SequenceEqual(MemoryMarshal.AsBytes(second.AsSpan())))
        {
            Assert.Fail($"{Path.GetFileName(path)} ({request}): a sample of {count} is not written");
        }
    }

    private static void Check<T>(TiffImage image, TiffPixelScale scale, TiffPixelOrder order, string path) where T : unmanaged, INumber<T>
    {
        int count = image.Width * image.Height * image.Channels;
        var first = new T[count];
        var second = new T[count];
        MemoryMarshal.AsBytes(first.AsSpan()).Fill(0x55);
        MemoryMarshal.AsBytes(second.AsSpan()).Fill(0xAA);
        try
        {
            image.ReadPixels<T>(first.AsMemory(), scale, order);
            image.ReadPixels<T>(second.AsSpan(), scale, order);
        }
        catch (Exception ex) when (ex is TiffException or InvalidOperationException)
        {
            return;   // a damaged image, or a conversion that would lose values: nothing is returned
        }

        reads++;

        if (!MemoryMarshal.AsBytes(first.AsSpan()).SequenceEqual(MemoryMarshal.AsBytes(second.AsSpan())))
        {
            int at = Enumerable.Range(0, count).First(i => !first[i].Equals(second[i]) && !(T.IsNaN(first[i]) && T.IsNaN(second[i])));
            Assert.Fail($"{Path.GetFileName(path)} ({typeof(T).Name}, {scale}, {order}): sample {at} of {count} is not written");
        }
    }
}
