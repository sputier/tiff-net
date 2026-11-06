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
    /// <exception cref="TiffException">The pixels of a page read from a file are damaged.</exception>
    /// <exception cref="IOException">The file cannot be written.</exception>
    public void Save(string path, TiffWriteOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        Platform.EnsureSupported();
        options ??= TiffWriteOptions.Default;
        TiffImage[] pages = [.. _images];
        SafeFile.Replace(path, stream => TiffFileWriter.Write(pages, stream, options, CancellationToken.None));
    }

    /// <summary>
    /// Writes the pages into a stream, from its current position; the stream is left open. A stream that cannot seek is written
    /// through a temporary file, copied in a bounded buffer.
    /// </summary>
    /// <param name="stream">The stream.</param>
    /// <param name="options">How to write; <see cref="TiffWriteOptions.Default"/> when <see langword="null"/>.</param>
    /// <exception cref="InvalidOperationException">The file has no page, or a classic file would exceed 4 GB.</exception>
    /// <exception cref="NotSupportedException">A page cannot be read.</exception>
    /// <exception cref="TiffException">The pixels of a page read from a file are damaged.</exception>
    /// <exception cref="IOException">The stream cannot be written.</exception>
    public void Save(Stream stream, TiffWriteOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(stream);
        Platform.EnsureSupported();
        options ??= TiffWriteOptions.Default;
        TiffImage[] pages = [.. _images];
        SafeFile.ToStream(stream, output => TiffFileWriter.Write(pages, output, options, CancellationToken.None));
    }

    /// <summary>
    /// Writes the pages into a stream as <see cref="Save(Stream, TiffWriteOptions?)"/>, encoding on the threads of the pool; a
    /// stream that cannot seek (a response body) is written asynchronously. The pages are those of the file when the method is
    /// called. Failures of the arguments are thrown, every other failure is reported by the task.
    /// </summary>
    /// <param name="stream">The stream, left open.</param>
    /// <param name="options">How to write; <see cref="TiffWriteOptions.Default"/> when <see langword="null"/>.</param>
    /// <param name="cancellationToken">Stops the write between strips or tiles; what is written of the stream is then incomplete.</param>
    /// <returns>The task of the write.</returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    public Task SaveAsync(Stream stream, TiffWriteOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        Platform.EnsureSupported();
        options ??= TiffWriteOptions.Default;
        TiffImage[] pages = [.. _images];
        return SafeFile.ToStreamAsync(stream, output => TiffFileWriter.Write(pages, output, options, cancellationToken), cancellationToken);
    }
}
