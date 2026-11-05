using System.Collections;
using Eleris.TiffNet.Internal;
using Eleris.TiffNet.Internal.Structure;

namespace Eleris.TiffNet;

/// <summary>
/// A TIFF file: its pages, in the order of the file. A page is a <see cref="TiffImage"/>; reduced-resolution versions stored as
/// sub-directories are reached through <see cref="TiffImage.SubImages"/>.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Open"/> reads the directories of a file, not its pixels, and keeps the file open until <see cref="Dispose"/>: the
/// pixels are read when they are asked for. <see cref="Read"/> holds a whole stream in memory.
/// </para>
/// <para>A damaged or hostile file fails with a <see cref="TiffException"/>, within the limits of <see cref="TiffReadOptions"/>.</para>
/// </remarks>
public sealed class TiffFile : IReadOnlyList<TiffImage>, IDisposable
{
    private readonly List<TiffImage> _images;
    private readonly TiffSource? _source;

    private TiffFile(TiffSource source, TiffReadOptions options, List<TiffImage> images)
    {
        _source = source;
        Options = options;
        _images = images;
    }

    /// <summary>The number of pages.</summary>
    public int Count => _images.Count;

    /// <summary>A page, by its zero-based index in the file.</summary>
    /// <param name="index">The index of the page.</param>
    public TiffImage this[int index] => _images[index];

    /// <summary>
    /// The main image: the first page that is neither a reduced-resolution version of another (a thumbnail) nor a transparency
    /// mask, as <c>NewSubfileType</c> says; the first page when every page is one of those.
    /// </summary>
    public TiffImage Primary => _images.FirstOrDefault(i => i.IsFullResolution) ?? _images[0];

    /// <summary>The options the file was read with.</summary>
    public TiffReadOptions Options { get; }

    internal TiffSource Source => _source ?? throw new InvalidOperationException("The file has no source.");

    /// <summary>Opens a file and reads its directories; the pixels are read on demand, until the file is disposed.</summary>
    /// <param name="path">The path of the file.</param>
    /// <param name="options">The tolerance and the limits of the read; <see cref="TiffReadOptions.Default"/> when <see langword="null"/>.</param>
    /// <returns>The file, to dispose when its pixels are no longer needed.</returns>
    /// <exception cref="TiffException">The file is not TIFF, is damaged, or exceeds a limit of <paramref name="options"/>.</exception>
    /// <exception cref="IOException">The file cannot be read.</exception>
    public static TiffFile Open(string path, TiffReadOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        Platform.EnsureSupported();
        options ??= TiffReadOptions.Default;
        var source = TiffSource.Open(path);
        return Load(source, options);
    }

    /// <summary>
    /// Reads a TIFF stream into memory, from its current position: the stream can be closed afterwards, and need not be able to
    /// seek.
    /// </summary>
    /// <param name="stream">The stream; at most <see cref="TiffReadOptions.MaxInputBytes"/> are taken from it.</param>
    /// <param name="options">The tolerance and the limits of the read; <see cref="TiffReadOptions.Default"/> when <see langword="null"/>.</param>
    /// <returns>The file.</returns>
    /// <exception cref="TiffException">The data is not TIFF, is damaged, or exceeds a limit of <paramref name="options"/>.</exception>
    public static TiffFile Read(Stream stream, TiffReadOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(stream);
        Platform.EnsureSupported();
        options ??= TiffReadOptions.Default;
        return Load(TiffSource.FromStream(stream, options, NameOf(stream)), options);
    }

    /// <summary>Reads a TIFF stream into memory asynchronously, from its current position.</summary>
    /// <param name="stream">The stream; at most <see cref="TiffReadOptions.MaxInputBytes"/> are taken from it.</param>
    /// <param name="options">The tolerance and the limits of the read; <see cref="TiffReadOptions.Default"/> when <see langword="null"/>.</param>
    /// <param name="cancellationToken">Stops the read.</param>
    /// <returns>The file.</returns>
    /// <exception cref="TiffException">The data is not TIFF, is damaged, or exceeds a limit of <paramref name="options"/>.</exception>
    public static async Task<TiffFile> ReadAsync(Stream stream, TiffReadOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        Platform.EnsureSupported();
        options ??= TiffReadOptions.Default;
        var source = await TiffSource.FromStreamAsync(stream, options, NameOf(stream), cancellationToken).ConfigureAwait(false);
        return Load(source, options, cancellationToken);
    }

    /// <summary>Returns an enumerator over the pages.</summary>
    public IEnumerator<TiffImage> GetEnumerator() => _images.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>Closes the file opened by <see cref="Open"/>; its pixels can no longer be read. The metadata stays available.</summary>
    public void Dispose() => _source?.Dispose();

    private static string NameOf(Stream stream) => stream is FileStream file ? file.Name : "stream";

    private static TiffFile Load(TiffSource source, TiffReadOptions options, CancellationToken cancellationToken = default)
    {
        try
        {
            var budget = new ResourceBudget(options, source.Name, cancellationToken);
            var reader = new StructureReader(source, budget);
            if (reader.Header.FirstDirectory == 0)
            {
                throw budget.Invalid("the file has no image.");
            }

            var images = new List<TiffImage>();
            for (var directory = reader.ReadDirectory(reader.Header.FirstDirectory); directory is not null;
                 directory = directory.Next == 0 ? null : reader.ReadDirectory(directory.Next))
            {
                images.Add(ReadImage(reader, directory));
            }

            if (images.Count == 0)
            {
                throw budget.Invalid($"the first directory (offset {reader.Header.FirstDirectory}) is outside the file.");
            }

            var file = new TiffFile(source, options, images);
            foreach (var image in images)
            {
                image.Attach(file);
            }

            return file;
        }
        catch
        {
            source.Dispose();
            throw;
        }
    }

    private static TiffImage ReadImage(StructureReader reader, RawDirectory directory)
    {
        var descriptor = ImageDescriptor.Read(reader, directory);
        var subImages = new List<TiffImage>();
        if (directory.TryGet(ImageDescriptor.SubIfdsTag, out var entry) && entry.Valid)
        {
            foreach (long offset in reader.ReadIntegers(entry))
            {
                if (reader.ReadDirectory(offset) is { } sub)
                {
                    subImages.Add(ReadImage(reader, sub));
                }
            }
        }

        return new TiffImage(descriptor, subImages);
    }
}
