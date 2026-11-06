namespace Eleris.TiffNet.Internal.Writing;

/// <summary>Where a written file goes: a temporary file that replaces the target, or a stream (through a temporary file if it cannot seek).</summary>
internal static class SafeFile
{
    /// <summary>The bytes copied at once from a temporary file into a stream that cannot seek.</summary>
    private const int CopyBuffer = 1 << 20;

    /// <summary>
    /// Writes a file through a temporary file in the same folder, which then replaces the target: a failure leaves the target as it
    /// was, and the temporary file is removed without hiding the failure.
    /// </summary>
    public static void Replace(string path, Action<Stream> write)
    {
        using var file = TemporaryFile.Next(path);
        write(file.Stream);
        file.Commit();
    }

    /// <summary>
    /// Puts the temporary file in place of the target. A replaced file gives its permissions (Unix) and its attributes and access
    /// rights (Windows, through File.Replace) to the new one. A target this process holds open (the file being saved over) is
    /// first moved aside, which Windows allows for a file opened with FileShare.Delete, then removed.
    /// </summary>
    internal static void Move(string temporary, string target)
    {
        if (!File.Exists(target))
        {
            File.Move(temporary, target);
            return;
        }

        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(temporary, File.GetUnixFileMode(target));
            File.Move(temporary, target, overwrite: true);
            return;
        }

        try
        {
            File.Replace(temporary, target, destinationBackupFileName: null, ignoreMetadataErrors: true);
        }
        catch (IOException ex) when (IsSharingViolation(ex))
        {
            // The target is open (FileShare.Delete): move it aside, put the new file in place, remove the old one. If the new
            // file cannot be put in place, the old one goes back: the target is never lost.
            string aside = $"{temporary}.old";
            File.Move(target, aside);
            try
            {
                File.Move(temporary, target);
            }
            catch
            {
                File.Move(aside, target);
                throw;
            }

            TryDelete(aside);
        }
    }

    /// <summary>A file open elsewhere (Windows ERROR_SHARING_VIOLATION or ERROR_LOCK_VIOLATION), not another failure.</summary>
    private static bool IsSharingViolation(IOException ex) => (ex.HResult & 0xFFFF) is 32 or 33;

    /// <summary>The file a path designates: the end of its symbolic links, so that a link is written through, not replaced.</summary>
    internal static string Resolve(string path)
    {
        string full = Path.GetFullPath(path);
        var info = new FileInfo(full);
        return info.Exists && info.LinkTarget is not null && info.ResolveLinkTarget(returnFinalTarget: true) is { } target
            ? target.FullName
            : full;
    }

    /// <summary>Writes into a stream: straight when it seeks, otherwise through a temporary file copied in a bounded buffer.</summary>
    public static void ToStream(Stream stream, Action<Stream> write)
    {
        if (!stream.CanWrite)
        {
            throw new ArgumentException("The stream cannot be written.", nameof(stream));
        }

        if (stream.CanSeek)
        {
            write(stream);
            stream.Flush();
            return;
        }

        using var buffer = Buffer();
        write(buffer);
        buffer.Position = 0;
        buffer.CopyTo(stream, CopyBuffer);
        stream.Flush();
    }

    /// <summary>As <see cref="ToStream"/>, the copy of a stream that cannot seek done asynchronously (a response body of a web server).</summary>
    public static async Task ToStreamAsync(Stream stream, Action<Stream> write, CancellationToken cancellationToken)
    {
        if (!stream.CanWrite)
        {
            throw new ArgumentException("The stream cannot be written.", nameof(stream));
        }

        if (stream.CanSeek)
        {
            // Encoding is the work; the writes go to a stream that seeks, which does not block for long.
            await Task.Run(() => write(stream), cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        await using var buffer = Buffer();
        await Task.Run(() => write(buffer), cancellationToken).ConfigureAwait(false);
        buffer.Position = 0;
        await buffer.CopyToAsync(stream, CopyBuffer, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The temporary file of a stream that cannot seek, removed when it is closed.</summary>
    private static FileStream Buffer() => new(Path.Combine(Path.GetTempPath(), $"eleris-tiff-{Guid.NewGuid():N}.tmp"),
        FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 1 << 16, FileOptions.DeleteOnClose);

    internal static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // Left behind: the original failure matters more.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}

/// <summary>A temporary file next to a target, which replaces it on <see cref="Commit"/> and is removed otherwise.</summary>
internal sealed class TemporaryFile : IDisposable
{
    private readonly string _target;
    private readonly string _path;
    private bool _committed;

    private TemporaryFile(string target)
    {
        _target = SafeFile.Resolve(target);
        string folder = Path.GetDirectoryName(_target) ?? ".";
        _path = Path.Combine(folder, $".tiff-{Guid.NewGuid():N}.tmp");   // short: the name of the target may be long
        Stream = new FileStream(_path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 1 << 16);
    }

    public FileStream Stream { get; }

    /// <summary>Creates the temporary file of a target.</summary>
    public static TemporaryFile Next(string target) => new(target);

    /// <summary>Flushes the file to the disk and puts it in place of the target.</summary>
    public void Commit()
    {
        Stream.Flush(flushToDisk: true);
        Stream.Dispose();
        SafeFile.Move(_path, _target);
        _committed = true;
    }

    /// <summary>Removes the temporary file unless it was committed; a failure to remove it hides nothing.</summary>
    public void Dispose()
    {
        if (!_committed)
        {
            Stream.Dispose();
            SafeFile.TryDelete(_path);
        }
    }
}
