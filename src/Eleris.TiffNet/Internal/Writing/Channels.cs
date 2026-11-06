using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Eleris.TiffNet.Internal.Writing;

/// <summary>One channel of interleaved pixels, copied into a plane (the blocks of a planar file).</summary>
internal static class Channels
{
    /// <summary>
    /// Copies sample <paramref name="channel"/> of each of the <paramref name="count"/> pixels of <paramref name="pixels"/>
    /// (<paramref name="channels"/> samples of <paramref name="bytesPerSample"/> bytes each) into <paramref name="plane"/>.
    /// </summary>
    public static void Extract(ReadOnlySpan<byte> pixels, int channels, int channel, int bytesPerSample, int count, Span<byte> plane)
    {
        switch (bytesPerSample)
        {
            case 1:
                Typed(pixels, channels, channel, count, plane);
                break;
            case 2:
                Typed(MemoryMarshal.Cast<byte, ushort>(pixels[..(count * channels * 2)]), channels, channel, count, MemoryMarshal.Cast<byte, ushort>(plane));
                break;
            case 4:
                Typed(MemoryMarshal.Cast<byte, uint>(pixels[..(count * channels * 4)]), channels, channel, count, MemoryMarshal.Cast<byte, uint>(plane));
                break;
            default:
                Typed(MemoryMarshal.Cast<byte, ulong>(pixels[..(count * channels * 8)]), channels, channel, count, MemoryMarshal.Cast<byte, ulong>(plane));
                break;
        }
    }

    private static void Typed<T>(ReadOnlySpan<T> pixels, int channels, int channel, int count, Span<T> plane) where T : unmanaged
    {
        if (count == 0)
        {
            return;
        }

        // The last sample read and written are checked once; the loop then goes through references.
        _ = pixels[(count - 1) * channels + channel];
        _ = plane[count - 1];
        ref T source = ref Unsafe.Add(ref MemoryMarshal.GetReference(pixels), channel);
        ref T target = ref MemoryMarshal.GetReference(plane);
        for (int i = 0; i < count; i++)
        {
            Unsafe.Add(ref target, i) = Unsafe.Add(ref source, i * channels);
        }
    }
}
