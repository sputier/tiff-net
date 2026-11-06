using Eleris.TiffNet.Internal;
using Eleris.TiffNet.Internal.Writing;

namespace Eleris.TiffNet;

public sealed partial class TiffFile
{
    /// <summary>
    /// Writes the pages into a file. A temporary file is written next to it (flushed to the disk), then replaces it: a failure
    /// leaves an existing file as it was; the attributes and permissions of a replaced file are kept where the platform allows,
    /// and a symbolic link is written through. The file this one was read from can be the target.
    /// </summary>
    /// <remarks>
    /// A file holds its pages: the sub-images of a page (<see cref="TiffImage.SubImages"/>), read only in this version, are not
    /// written, nor are the EXIF and GPS sub-directories (see the coverage of the standard). The pages must not change during the save.
    /// </remarks>
    /// <param name="path">The path of the file.</param>
    /// <param name="options">How to write; <see cref="TiffWriteOptions.Default"/> when <see langword="null"/>.</param>
    /// <exception cref="InvalidOperationException">The file has no page, or a classic file (<see cref="TiffBigTiff.Never"/>)
    /// would exceed 4 GB.</exception>
    /// <exception cref="NotSupportedException">A page cannot be read (<see cref="TiffImage.UnsupportedReason"/>).</exception>
    /// <exception cref="TiffException">The pixels of a page read from a file are damaged, or exceed the
    /// <see cref="TiffReadOptions.MaxPixelBytes"/> it was opened with: saving decodes every pixel, as reading the whole image does
    /// (<see cref="TiffException.Limit"/> says so).</exception>
    /// <exception cref="IOException">The file cannot be written.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled; the file is left as it was.</exception>
    /// <param name="cancellationToken">Stops the write between strips or tiles.</param>
    public void Save(string path, TiffWriteOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        Platform.EnsureSupported();
        options ??= TiffWriteOptions.Default;
        TiffImage[] pages = Pages(options);
        var encoders = TiffFileWriter.Prepare(pages, options, cancellationToken);
        SafeFile.Replace(path, stream => TiffFileWriter.Write(pages, encoders, stream, options, cancellationToken));
    }

    /// <summary>
    /// Writes the pages into a stream, from its current position; the stream is left open. A stream that cannot seek is written
    /// through a temporary file, copied in a bounded buffer.
    /// </summary>
    /// <param name="stream">The stream.</param>
    /// <param name="options">How to write; <see cref="TiffWriteOptions.Default"/> when <see langword="null"/>.</param>
    /// <exception cref="InvalidOperationException">The file has no page, or a classic file would exceed 4 GB.</exception>
    /// <exception cref="NotSupportedException">A page cannot be read.</exception>
    /// <exception cref="TiffException">The pixels of a page read from a file are damaged, or exceed the
    /// <see cref="TiffReadOptions.MaxPixelBytes"/> it was opened with: saving decodes every pixel, as reading the whole image does
    /// (<see cref="TiffException.Limit"/> says so).</exception>
    /// <exception cref="IOException">The stream cannot be written.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled; what is written of the
    /// stream is incomplete.</exception>
    /// <param name="cancellationToken">Stops the write between strips or tiles.</param>
    public void Save(Stream stream, TiffWriteOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        Platform.EnsureSupported();
        options ??= TiffWriteOptions.Default;
        TiffImage[] pages = Pages(options);
        var encoders = TiffFileWriter.Prepare(pages, options, cancellationToken);
        SafeFile.ToStream(stream, output => TiffFileWriter.Write(pages, encoders, output, options, cancellationToken));
    }

    /// <summary>
    /// Writes the pages into a stream as <see cref="Save(Stream, TiffWriteOptions?, CancellationToken)"/>, encoding on the threads of the pool; a
    /// stream that cannot seek (a response body) is written asynchronously. The pages are those of the file when the method is
    /// called. Failures of the arguments are thrown, every other failure is reported by the task.
    /// </summary>
    /// <param name="stream">The stream, left open.</param>
    /// <param name="options">How to write; <see cref="TiffWriteOptions.Default"/> when <see langword="null"/>.</param>
    /// <param name="progress">Receives the fraction written, 0 to 1, at most once a percent.</param>
    /// <param name="cancellationToken">Stops the write between strips or tiles; what is written of the stream is then incomplete.</param>
    /// <returns>The task of the write.</returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    public Task SaveAsync(Stream stream, TiffWriteOptions? options = null, IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        Platform.EnsureSupported();
        options ??= TiffWriteOptions.Default;
        TiffImage[] pages = Pages(options);
        return SafeFile.ToStreamAsync(stream,
            output => TiffFileWriter.Write(pages, TiffFileWriter.Prepare(pages, options, cancellationToken), output, options, cancellationToken, progress),
            cancellationToken);
    }

    /// <summary>
    /// Writes the pages into a file as <see cref="Save(string, TiffWriteOptions?, CancellationToken)"/> does, on a thread of the
    /// pool: a temporary file next to the target replaces it, a failure or a cancellation leaves an existing file as it was. The
    /// pages are those of the file when the method is called. Failures of the arguments are thrown, every other failure is
    /// reported by the task.
    /// </summary>
    /// <param name="path">The path of the file.</param>
    /// <param name="options">How to write; <see cref="TiffWriteOptions.Default"/> when <see langword="null"/>.</param>
    /// <param name="progress">Receives the fraction written, 0 to 1, at most once a percent.</param>
    /// <param name="cancellationToken">Stops the write between strips or tiles.</param>
    /// <returns>The task of the write.</returns>
    public Task SaveAsync(string path, TiffWriteOptions? options = null, IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        Platform.EnsureSupported();
        options ??= TiffWriteOptions.Default;
        TiffImage[] pages = Pages(options);
        return Task.Run(() =>
        {
            var encoders = TiffFileWriter.Prepare(pages, options, cancellationToken);
            SafeFile.Replace(path, stream => TiffFileWriter.Write(pages, encoders, stream, options, cancellationToken, progress));
        }, cancellationToken);
    }

    /// <summary>The pages a save writes: all, or those the library can read when <see cref="TiffWriteOptions.SkipUnsupportedPages"/>.</summary>
    private TiffImage[] Pages(TiffWriteOptions options) =>
        options.SkipUnsupportedPages ? [.. _images.Where(i => i.UnsupportedReason is null)] : [.. _images];
}
