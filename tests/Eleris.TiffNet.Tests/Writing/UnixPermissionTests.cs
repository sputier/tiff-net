using Eleris.TiffNet.Internal.Writing;

namespace Eleris.TiffNet.Tests.Writing;

/// <summary>On Unix, the files a save writes before they are in place are never more readable than the file they become.</summary>
public class UnixPermissionTests
{
    private const UnixFileMode Private = UnixFileMode.UserRead | UnixFileMode.UserWrite;

    [Fact]
    public void The_temporary_file_of_a_private_target_is_private_while_it_is_written()
    {
        if (OperatingSystem.IsWindows())
        {
            return;   // permissions of Unix (the job on Linux runs it)
        }

        string path = SaveTests.Temporary();
        File.WriteAllBytes(path, [1, 2, 3]);
        File.SetUnixFileMode(path, Private);
        try
        {
            using (var temporary = TemporaryFile.Next(path))
            {
                Assert.Equal(Private, File.GetUnixFileMode(temporary.TemporaryPath));
            }

            new TiffFile().Add(BandWritingTests.Gradient(8, 8, 1)).Save(path);
            Assert.Equal(Private, File.GetUnixFileMode(path));
        }
        finally
        {
            SaveTests.Delete(path);
        }
    }

    [Fact]
    public void The_buffer_of_a_stream_that_cannot_seek_is_readable_by_its_owner_only()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var buffer = SafeFile.Buffer();

        Assert.Equal(Private, File.GetUnixFileMode(buffer.Name));
    }
}
