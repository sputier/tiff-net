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
        string full = Path.GetFullPath(path);
        string folder = Path.GetDirectoryName(full) ?? ".";
        string temporary = Path.Combine(folder, $".{Path.GetFileName(full)}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 1 << 16))
            {
                write(stream);
                stream.Flush(flushToDisk: true);
            }

            Move(temporary, full);
        }
        catch
        {
            TryDelete(temporary);
            throw;
        }
    }

    /// <summary>
    /// Puts the temporary file in place of the target. A replaced file gives its permissions (Unix) and its attributes and access
    /// rights (Windows, through File.Replace) to the new one. A target this process holds open (the file being saved over) is
    /// first moved aside, which Windows allows for a file opened with FileShare.Delete, then removed.
    /// </summary>
    private static void Move(string temporary, string target)
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

    private static void TryDelete(string path)
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
