namespace Eleris.TiffNet.Internal;

/// <summary>Checks that the machine is one the library gives correct results on.</summary>
internal static class Platform
{
    /// <summary>
    /// Refuses big-endian machines: samples are converted to the machine's byte order with spans and
    /// <see cref="System.Runtime.InteropServices.MemoryMarshal"/>, which assumes little endian. Every .NET 10 platform the
    /// library targets (x64 and Arm64 on Windows, Linux, Android, iOS) is little endian; refusing is better than wrong pixels.
    /// </summary>
    /// <remarks>Called by the entry points of the library (opening, reading, creating and writing images).</remarks>
    /// <exception cref="PlatformNotSupportedException">The machine is big endian.</exception>
    public static void EnsureSupported() => EnsureSupported(BitConverter.IsLittleEndian);

    /// <summary>The check, with the byte order as a parameter so that the refusal can be tested.</summary>
    internal static void EnsureSupported(bool isLittleEndian)
    {
        if (!isLittleEndian)
        {
            throw new PlatformNotSupportedException("Eleris.TiffNet needs a little-endian machine.");
        }
    }
}
