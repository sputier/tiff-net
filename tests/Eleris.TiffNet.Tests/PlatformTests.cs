using Eleris.TiffNet.Internal;

namespace Eleris.TiffNet.Tests;

public class PlatformTests
{
    [Fact]
    public void A_little_endian_machine_is_supported() => Platform.EnsureSupported(isLittleEndian: true);

    [Fact]
    public void A_big_endian_machine_is_refused()
    {
        var ex = Assert.Throws<PlatformNotSupportedException>(() => Platform.EnsureSupported(isLittleEndian: false));
        Assert.Contains("little-endian", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_machine_running_the_tests_is_supported() => Platform.EnsureSupported();
}
