using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.Arm;
using System.Runtime.Intrinsics.X86;

namespace Eleris.TiffNet.Internal.Writing;

/// <summary>
/// One channel of interleaved pixels, copied into a plane: the blocks of a planar file, and the planes of a read in planar
/// order. 2 to 4 channels go through byte shuffles (SSSE3 or AdvSimd), 16 bytes of the plane at once.
/// </summary>
internal static class Channels
{
    /// <summary>
    /// Copies sample <paramref name="channel"/> of each of the <paramref name="count"/> pixels of <paramref name="pixels"/>
    /// (<paramref name="channels"/> samples of <paramref name="bytesPerSample"/> bytes each) into <paramref name="plane"/>.
    /// </summary>
    public static void Extract(ReadOnlySpan<byte> pixels, int channels, int channel, int bytesPerSample, int count, Span<byte> plane)
    {
        int done = Vectorised(pixels, channels, channel, bytesPerSample, count, plane);
        if (done == count)
        {
            return;
        }

        pixels = pixels[(done * channels * bytesPerSample)..];
        plane = plane[(done * bytesPerSample)..];
        count -= done;
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

    /// <summary>
    /// The first pixels of <see cref="Extract"/> done with shuffles, as many as whole groups allow (16 bytes of the plane from
    /// <paramref name="channels"/> × 16 bytes of pixels, within <paramref name="pixels"/>); the number done.
    /// </summary>
    public static int Vectorised(ReadOnlySpan<byte> pixels, int channels, int channel, int bytesPerSample, int count, Span<byte> plane)
    {
        if (channels is < 2 or > 4 || bytesPerSample is not (1 or 2 or 4 or 8) || !(Ssse3.IsSupported || AdvSimd.Arm64.IsSupported)
            || (long)count * bytesPerSample < 16)
        {
            return 0;
        }

        // Byte j of the plane is byte q = (j / size) × channels × size + channel × size + j % size of the group: in input vector
        // q / 16, lane q % 16; the other vectors give zero there (0x80), and the shuffles of the vectors are added together.
        Span<Vector128<byte>> indices = stackalloc Vector128<byte>[4];
        Span<byte> lanes = stackalloc byte[16];
        for (int v = 0; v < channels; v++)
        {
            for (int j = 0; j < 16; j++)
            {
                int q = j / bytesPerSample * channels * bytesPerSample + channel * bytesPerSample + j % bytesPerSample;
                lanes[j] = q / 16 == v ? (byte)(q % 16) : (byte)0x80;
            }

            indices[v] = Vector128.Create<byte>(lanes);
        }

        int perGroup = 16 / bytesPerSample, groupBytes = 16 * channels, p = 0;
        for (int from = 0; p + perGroup <= count && from + groupBytes <= pixels.Length; p += perGroup, from += groupBytes)
        {
            var result = Lookup(Vector128.Create<byte>(pixels[from..]), indices[0]) | Lookup(Vector128.Create<byte>(pixels[(from + 16)..]), indices[1]);
            if (channels > 2)
            {
                result |= Lookup(Vector128.Create<byte>(pixels[(from + 32)..]), indices[2]);
            }

            if (channels > 3)
            {
                result |= Lookup(Vector128.Create<byte>(pixels[(from + 48)..]), indices[3]);
            }

            result.CopyTo(plane[(p * bytesPerSample)..]);
        }

        return p;
    }

    /// <summary>Bytes of <paramref name="x"/> at <paramref name="indices"/>; an index of 0x80 gives 0.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector128<byte> Lookup(Vector128<byte> x, Vector128<byte> indices) =>
        Ssse3.IsSupported ? Ssse3.Shuffle(x, indices) : AdvSimd.Arm64.VectorTableLookup(x, indices);

    private static void Typed<T>(ReadOnlySpan<T> pixels, int channels, int channel, int count, Span<T> plane) where T : unmanaged
    {
        if (count == 0)
        {
            return;
        }

        // Both ends are checked (the slice, then the last sample); the loop then goes through references.
        var samples = pixels[channel..];
        _ = samples[(count - 1) * channels];
        _ = plane[count - 1];
        ref T source = ref MemoryMarshal.GetReference(samples);
        ref T target = ref MemoryMarshal.GetReference(plane);
        for (int i = 0; i < count; i++)
        {
            Unsafe.Add(ref target, i) = Unsafe.Add(ref source, i * channels);
        }
    }
}
