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
        catch (IOException)
        {
            // The target is open (FileShare.Delete): move it aside, put the new file in place, remove the old one.
            string aside = $"{temporary}.old";
            File.Move(target, aside);
            File.Move(temporary, target);
            TryDelete(aside);
        }
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

        string temporary = Path.Combine(Path.GetTempPath(), $"eleris-tiff-{Guid.NewGuid():N}.tmp");
        using var buffer = new FileStream(temporary, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 1 << 16,
            FileOptions.DeleteOnClose);
        write(buffer);
        buffer.Position = 0;
        buffer.CopyTo(stream, CopyBuffer);
        stream.Flush();
    }

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
        _target = Path.GetFullPath(target);
        string folder = Path.GetDirectoryName(_target) ?? ".";
        _path = Path.Combine(folder, $".{Path.GetFileName(_target)}.{Guid.NewGuid():N}.tmp");
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
