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
/// <see cref="Open(string, TiffReadOptions?)"/> (or <see cref="Open(Stream, TiffReadOptions?, bool)"/>) reads the directories of a file, not its pixels, and keeps the file open until <see cref="Dispose"/>: the
/// pixels are read when they are asked for. <see cref="Read(Stream, TiffReadOptions?)"/> holds a whole stream in memory.
/// </para>
/// <para>A damaged or hostile file fails with a <see cref="TiffException"/>, within the limits of <see cref="TiffReadOptions"/>.</para>
/// </remarks>
public sealed partial class TiffFile : IReadOnlyList<TiffImage>, IDisposable
{
    private readonly List<TiffImage> _images;
    private readonly TiffSource? _source;

    /// <summary>Creates an empty file, to build with <see cref="Add"/> and write with <c>Save</c>.</summary>
    public TiffFile()
    {
        Options = TiffReadOptions.Default;
        _images = [];
    }

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
    /// <exception cref="InvalidOperationException">The file has no page.</exception>
    public TiffImage Primary => _images.FirstOrDefault(i => i.IsFullResolution)
        ?? (_images.Count > 0 ? _images[0] : throw new InvalidOperationException("The file has no page."));

    /// <summary>The options the file was read with.</summary>
    public TiffReadOptions Options { get; }

    internal TiffSource Source => _source ?? throw new InvalidOperationException("The file was not read from a file or a stream.");

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

    /// <summary>Opens a file on a thread of the pool, as <see cref="Open(string, TiffReadOptions?)"/>.</summary>
    /// <param name="path">The path of the file.</param>
    /// <param name="options">The tolerance and the limits of the read; <see cref="TiffReadOptions.Default"/> when <see langword="null"/>.</param>
    /// <param name="cancellationToken">Stops the reading of the directories.</param>
    /// <returns>The file, to dispose when its pixels are no longer needed; every failure but of the arguments is reported by the task.</returns>
    public static Task<TiffFile> OpenAsync(string path, TiffReadOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        Platform.EnsureSupported();
        options ??= TiffReadOptions.Default;
        return Task.Run(() => Load(TiffSource.Open(path), options, cancellationToken), cancellationToken);
    }

    /// <summary>
    /// Opens a TIFF stream that seeks, from its current position, and reads its directories; the pixels are read from the stream
    /// on demand, until the file is disposed, without copying the stream into memory (a <see cref="FileStream"/> is read through
    /// its handle, from several threads at once).
    /// </summary>
    /// <param name="stream">The stream; it must seek and be read, and must not be used by the application while the file is open.</param>
    /// <param name="options">The tolerance and the limits of the read; <see cref="TiffReadOptions.Default"/> when <see langword="null"/>.</param>
    /// <param name="leaveOpen">Whether the stream stays open when the file is disposed (or when opening it fails).</param>
    /// <returns>The file, to dispose when its pixels are no longer needed.</returns>
    /// <exception cref="ArgumentException">The stream cannot seek or be read.</exception>
    /// <exception cref="TiffException">The data is not TIFF, is damaged, or exceeds a limit of <paramref name="options"/>.</exception>
    /// <exception cref="IOException">The stream cannot be read.</exception>
    public static TiffFile Open(Stream stream, TiffReadOptions? options = null, bool leaveOpen = false)
    {
        ArgumentNullException.ThrowIfNull(stream);
        Platform.EnsureSupported();
        if (!stream.CanSeek || !stream.CanRead)
        {
            throw new ArgumentException("The stream must seek and be read: use TiffFile.Read for a stream that cannot seek.", nameof(stream));
        }

        options ??= TiffReadOptions.Default;
        return Load(TiffSource.FromOpenStream(stream, leaveOpen, NameOf(stream)), options);
    }

    /// <summary>
    /// Reads TIFF data the application holds in memory, without copying it: the memory must not change while the file is used.
    /// </summary>
    /// <param name="data">The bytes of the TIFF data.</param>
    /// <param name="options">The tolerance and the limits of the read; <see cref="TiffReadOptions.Default"/> when <see langword="null"/>.</param>
    /// <returns>The file.</returns>
    /// <exception cref="TiffException">The data is not TIFF, is damaged, or exceeds a limit of <paramref name="options"/>.</exception>
    public static TiffFile Read(ReadOnlyMemory<byte> data, TiffReadOptions? options = null)
    {
        Platform.EnsureSupported();
        options ??= TiffReadOptions.Default;
        return Load(TiffSource.FromMemory(data), options);
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

    /// <summary>Closes the file opened by <see cref="Open(string, TiffReadOptions?)"/>; its pixels can no longer be read. The metadata stays available.</summary>
    public void Dispose() => _source?.Dispose();

    private static string NameOf(Stream stream) => stream is FileStream file ? file.Name : "stream";

    /// <summary>How deep sub-directories are read: a pyramid needs one level; deeper chains are cut, never followed recursively.</summary>
    internal const int MaxSubImageDepth = 8;

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

            // The pages first, so that a sub-directory pointing to a page cannot take it out of the chain.
            var pages = new List<RawDirectory>();
            for (var directory = reader.ReadDirectory(reader.Header.FirstDirectory); directory is not null;
                 directory = directory.Next == 0 ? null : reader.ReadDirectory(directory.Next))
            {
                pages.Add(directory);
            }

            if (pages.Count == 0)
            {
                throw budget.Invalid($"the first directory (offset {reader.Header.FirstDirectory}) is outside the file.");
            }

            var images = pages.Select(p => ReadImage(reader, p)).ToList();
            ReadSubImages(reader, pages, images);
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

    /// <summary>
    /// Reads the sub-directories (SubIFDs) of the pages, level after level with a work list: the depth of a hostile chain cannot
    /// exhaust the stack. A damaged sub-image is left out (refused with Strict): it must not hide the page it belongs to.
    /// </summary>
    private static void ReadSubImages(StructureReader reader, List<RawDirectory> pages, List<TiffImage> images)
    {
        var level = pages.Zip(images).ToList();
        for (int depth = 1; depth <= MaxSubImageDepth && level.Count > 0; depth++)
        {
            var next = new List<(RawDirectory, TiffImage)>();
            foreach (var (directory, image) in level)
            {
                if (!directory.TryGet(ImageDescriptor.SubIfdsTag, out var entry))
                {
                    continue;
                }

                if (!entry.Valid || !TagTypes.IsInteger(entry.Type))
                {
                    if (reader.Budget.Options.Strict)
                    {
                        reader.Ensure(entry);
                        throw reader.Budget.Invalid($"the SubIFDs tag holds {entry.Type} values.");
                    }

                    continue;
                }

                foreach (long offset in reader.ReadIntegers(entry))
                {
                    if (reader.ReadDirectory(offset) is not { } sub)
                    {
                        continue;
                    }

                    try
                    {
                        var subImage = ReadImage(reader, sub);
                        image.AddSubImage(subImage);
                        next.Add((sub, subImage));
                    }
                    catch (TiffException ex) when (!reader.Budget.Options.Strict && ex.Limit is null)
                    {
                        // A damaged sub-image (a thumbnail without its size, for example): left out.
                    }
                }
            }

            level = next;
        }

        if (level.Count > 0 && reader.Budget.Options.Strict && level.Any(l => l.Item1.TryGet(ImageDescriptor.SubIfdsTag, out _)))
        {
            throw reader.Budget.Invalid($"the sub-directories are nested more than {MaxSubImageDepth} levels deep.");
        }
    }

    private static TiffImage ReadImage(StructureReader reader, RawDirectory directory)
    {
        var descriptor = ImageDescriptor.Read(reader, directory);
        bool tiles = descriptor.Layout == TiffLayout.Tiles;
        var storage = new TiffStorage
        {
            Compression = descriptor.Compression,
            Predictor = descriptor.Predictor,
            Layout = descriptor.Layout,
            // Tiles larger than an int make the image unsupported; their size is reported as the largest int.
            TileWidth = tiles ? (int)Math.Min(descriptor.BlockWidth, int.MaxValue) : 0,
            TileHeight = tiles ? (int)Math.Min(descriptor.BlockHeight, int.MaxValue) : 0,
            RowsPerStrip = tiles ? 0 : (int)Math.Min(descriptor.BlockHeight, int.MaxValue),
            PlanarConfiguration = descriptor.Planar ? TiffPixelOrder.Planar : TiffPixelOrder.Interleaved,
            BigEndian = !reader.Header.LittleEndian,
            BigTiff = reader.Header.BigTiff,
            Orientation = descriptor.Orientation,
            Photometric = descriptor.Photometric,
            BitsPerSample = descriptor.BitsPerSample,
        };
        var metadata = MetadataReader.Read(reader, directory);
        if (descriptor.Photometric is TiffPhotometric.MinIsWhite or TiffPhotometric.Palette or TiffPhotometric.YCbCr)
        {
            // The values are delivered inverted (MinIsWhite), expanded to RGB (palette) or converted to RGB (YCbCr): the ranges of
            // the file describe the values it stores.
            foreach (int code in TiffMetadata.PixelTags)
            {
                metadata.Remove(code);
            }
        }

        return new TiffImage(descriptor, metadata, storage);
    }
}
