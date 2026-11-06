using Eleris.TiffNet.Internal;
using Eleris.TiffNet.Internal.Writing;

namespace Eleris.TiffNet;

public sealed partial class TiffFile
{
    /// <summary>
    /// Writes the pages into a file. A temporary file is written next to it, then replaces it: a failure leaves an existing file as
    /// it was; the attributes and permissions of a replaced file are kept where the platform allows. The file this one was read
    /// from can be the target.
    /// </summary>
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
        SafeFile.Replace(path, stream => TiffFileWriter.Write(_images, stream, options, CancellationToken.None));
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
        SafeFile.ToStream(stream, output => TiffFileWriter.Write(_images, output, options, CancellationToken.None));
    }

    /// <summary>
    /// Writes the pages into a stream on a thread of the pool (encoding and writing), as <see cref="Save(Stream, TiffWriteOptions?)"/>.
    /// Every failure is reported by the task.
    /// </summary>
    /// <param name="stream">The stream, left open.</param>
    /// <param name="options">How to write; <see cref="TiffWriteOptions.Default"/> when <see langword="null"/>.</param>
    /// <param name="cancellationToken">Stops the write between strips or tiles; what is written of the stream is then incomplete.</param>
    /// <returns>The task of the write.</returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    public Task SaveAsync(Stream stream, TiffWriteOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        options ??= TiffWriteOptions.Default;
        return Task.Run(() =>
        {
            Platform.EnsureSupported();
            SafeFile.ToStream(stream, output => TiffFileWriter.Write(_images, output, options, cancellationToken));
        }, cancellationToken);
    }
}
