using System.Globalization;

namespace Eleris.TiffNet.Internal;

/// <summary>
/// The limits of one read, checked before any allocation or loop whose size comes from the file. Created from
/// <see cref="TiffReadOptions"/> for each read (opening a file, decoding an image or a region); usable from the threads that decode
/// in parallel.
/// </summary>
/// <remarks>
/// A refusal is a <see cref="TiffException"/> naming the limit, so that an application can tell "too large for this device" from
/// "damaged". Allocations that do not depend on the file (the pooled state of the codecs) are not counted.
/// </remarks>
internal sealed class ResourceBudget
{
    private readonly TiffReadOptions _options;
    private readonly CancellationToken _cancellation;
    private long _pixelBytes;
    private int _directories;

    public ResourceBudget(TiffReadOptions options, string sourceName, CancellationToken cancellation = default)
    {
        _options = options;
        SourceName = sourceName;
        _cancellation = cancellation;
    }

    /// <summary>The name of the file in messages: its file name, never its path.</summary>
    public string SourceName { get; }

    public TiffReadOptions Options => _options;

    /// <summary>Counts memory that decoding needs because of the file (pixels, codec buffers), before it is allocated.</summary>
    /// <exception cref="TiffException">The total would exceed <see cref="TiffReadOptions.MaxPixelBytes"/>.</exception>
    public void ReservePixels(long bytes, string what)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(bytes);
        long total = Interlocked.Add(ref _pixelBytes, bytes);
        if (total < 0 || total > _options.MaxPixelBytes)
        {
            Interlocked.Add(ref _pixelBytes, -bytes);
            throw Limit(nameof(TiffReadOptions.MaxPixelBytes),
                $"{what} needs {Describe(bytes)}, beyond the {Describe(_options.MaxPixelBytes)} allowed by MaxPixelBytes.");
        }
    }

    /// <summary>Counts one more directory (page or sub-directory).</summary>
    /// <exception cref="TiffException">The file has more than <see cref="TiffReadOptions.MaxDirectories"/>.</exception>
    public void CountDirectory()
    {
        if (Interlocked.Increment(ref _directories) > _options.MaxDirectories)
        {
            throw Limit(nameof(TiffReadOptions.MaxDirectories), $"the file has more than {_options.MaxDirectories} directories.");
        }
    }

    /// <exception cref="TiffException">A directory has more than <see cref="TiffReadOptions.MaxTagsPerDirectory"/> entries.</exception>
    public void CheckTagsPerDirectory(long entries)
    {
        if (entries > _options.MaxTagsPerDirectory)
        {
            throw Limit(nameof(TiffReadOptions.MaxTagsPerDirectory),
                $"a directory has {entries} entries, more than the {_options.MaxTagsPerDirectory} allowed by MaxTagsPerDirectory.");
        }
    }

    /// <exception cref="TiffException">The values of a tag are larger than <see cref="TiffReadOptions.MaxTagBytes"/>.</exception>
    public void CheckTagBytes(int tag, long bytes)
    {
        if (bytes > _options.MaxTagBytes)
        {
            throw Limit(nameof(TiffReadOptions.MaxTagBytes),
                $"the values of tag {tag} take {Describe(bytes)}, beyond the {Describe(_options.MaxTagBytes)} allowed by MaxTagBytes.");
        }
    }

    /// <exception cref="TiffException">A progressive JPEG image has more than <see cref="TiffReadOptions.MaxJpegScans"/> scans.</exception>
    public void CheckJpegScans(int scans)
    {
        if (scans > _options.MaxJpegScans)
        {
            throw Limit(nameof(TiffReadOptions.MaxJpegScans),
                $"a JPEG image has more than {_options.MaxJpegScans} scans, the limit of MaxJpegScans.");
        }
    }

    /// <summary>Stops the read when it is cancelled; called between strips, tiles and JPEG scans.</summary>
    public void ThrowIfCancelled() => _cancellation.ThrowIfCancellationRequested();

    /// <summary>A defect of the data, in a message that names the file.</summary>
    public TiffException Invalid(string message) => new($"{SourceName}: {message}");

    /// <summary>The name of a file in messages: its file name only, so that paths (and user names) do not leak into logs.</summary>
    public static string NameOf(string? path) => string.IsNullOrEmpty(path) ? "stream" : Path.GetFileName(path);

    private TiffException Limit(string limit, string message) => new($"{SourceName}: {message}", limit);

    // Messages are in English and must not depend on the culture of the machine ("1.5 GB", never "1,5 GB").
    private static string Describe(long bytes) => bytes switch
    {
        >= 1L << 30 => string.Create(CultureInfo.InvariantCulture, $"{bytes / (double)(1L << 30):0.#} GB"),
        >= 1L << 20 => string.Create(CultureInfo.InvariantCulture, $"{bytes / (double)(1L << 20):0.#} MB"),
        >= 1L << 10 => string.Create(CultureInfo.InvariantCulture, $"{bytes / 1024.0:0.#} KB"),
        _ => string.Create(CultureInfo.InvariantCulture, $"{bytes} bytes"),
    };
}
