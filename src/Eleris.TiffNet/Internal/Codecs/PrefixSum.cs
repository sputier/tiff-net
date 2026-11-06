using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.Arm;
using System.Runtime.Intrinsics.X86;

namespace Eleris.TiffNet.Internal.Codecs;

/// <summary>
/// The running sums that undo the predictors, <c>v[i] += v[i - stride]</c> from the start of a row, with 128-bit vectors: 16
/// bytes, 8 words or 4 double words at once. Within a vector the sum takes log2(lanes / stride) shifted additions; the last
/// values of the vector before carry into the next one. A stride of a vector or more needs no shift: each vector adds the one
/// a stride before, already final.
/// </summary>
/// <remarks>
/// Integer additions wrap, so the order of the additions does not change the result: the vectors give the samples of the loop
/// one by one, bit for bit, whatever the stride (tested against it). Where 128-bit vectors are not accelerated, or for a stride
/// that does not divide into the lanes usefully, the loop runs.
/// </remarks>
internal static class PrefixSum
{
    /// <summary>Whether <see cref="Run{T}"/> uses vectors for this element and stride.</summary>
    public static bool Vectorised<T>(int stride) where T : unmanaged =>
        Vector128.IsHardwareAccelerated && (Ssse3.IsSupported || AdvSimd.Arm64.IsSupported) && stride >= 1
        && (typeof(T) == typeof(byte) || typeof(T) == typeof(ushort) || typeof(T) == typeof(uint));

    /// <summary>Accumulates <paramref name="values"/> with a stride of <paramref name="stride"/> elements, in place.</summary>
    public static void Run<T>(Span<T> values, int stride) where T : unmanaged, IBinaryInteger<T>
    {
        int lanes = Vector128<T>.Count;
        if (!Vectorised<T>(stride) || values.Length < lanes + stride)
        {
            Scalar(values, stride, 0);
            return;
        }

        if (stride >= lanes)
        {
            // The values a vector adds are stride elements before it: final already, since the vectors go in order.
            Scalar(values[..stride], stride, 0);
            int i = stride;
            for (; i + lanes <= values.Length; i += lanes)
            {
                var sum = Vector128.Create<T>(values[i..]) + Vector128.Create<T>(values[(i - stride)..]);
                sum.CopyTo(values[i..]);
            }

            Scalar(values, stride, i);
            return;
        }

        int size = Unsafe.SizeOf<T>();
        int byteStride = stride * size;
        var bytes = MemoryMarshal.AsBytes(values);

        // The shifts of the vector by stride, 2 × stride, 4 × stride… bytes (towards the higher lanes, zeros in), and the carry:
        // lane j takes the last value of the vector before that is in the same chain (j + 16 − byteStride × k, the largest below
        // 16), repeated for every byte of the element.
        Span<Vector128<byte>> shifts = stackalloc Vector128<byte>[4];
        int levels = 0;
        for (int shift = byteStride; shift < 16; shift *= 2)
        {
            shifts[levels++] = ShiftIndices(shift);
        }

        var carryIndices = CarryIndices(byteStride, size);
        var carry = Vector128<byte>.Zero;
        int at = 0;
        for (; at + 16 <= bytes.Length; at += 16)
        {
            var x = Vector128.Create<byte>(bytes[at..]);
            for (int l = 0; l < levels; l++)
            {
                x = Add<T>(x, Lookup(x, shifts[l]));
            }

            x = Add<T>(x, carry);
            x.CopyTo(bytes[at..]);
            carry = Lookup(x, carryIndices);
        }

        Scalar(values, stride, at / size);
    }

    /// <summary>The loop, from element <paramref name="start"/> (the elements before it final).</summary>
    private static void Scalar<T>(Span<T> values, int stride, int start) where T : unmanaged, IBinaryInteger<T>
    {
        for (int i = Math.Max(start, stride); i < values.Length; i++)
        {
            values[i] += values[i - stride];
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector128<byte> Add<T>(Vector128<byte> a, Vector128<byte> b) where T : unmanaged =>
        (a.As<byte, T>() + b.As<byte, T>()).AsByte();

    /// <summary>Bytes of <paramref name="x"/> at <paramref name="indices"/>; an index of 0x80 or more gives 0.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector128<byte> Lookup(Vector128<byte> x, Vector128<byte> indices)
    {
        if (Ssse3.IsSupported)
        {
            return Ssse3.Shuffle(x, indices);
        }

        if (AdvSimd.Arm64.IsSupported)
        {
            return AdvSimd.Arm64.VectorTableLookup(x, indices);
        }

        return Vector128.Shuffle(x, indices);
    }

    /// <summary>Lane j takes byte j − <paramref name="shift"/>, or 0 below the vector.</summary>
    private static Vector128<byte> ShiftIndices(int shift)
    {
        Span<byte> indices = stackalloc byte[16];
        for (int j = 0; j < 16; j++)
        {
            indices[j] = j >= shift ? (byte)(j - shift) : (byte)0x80;
        }

        return Vector128.Create<byte>(indices);
    }

    /// <summary>
    /// Lane j takes the byte of the last element of the vector before that precedes it in its chain: element e of lane j is
    /// (16 + j) / size; its last predecessor in the vector before is the largest element below 16 / size that differs from it by a
    /// multiple of the stride.
    /// </summary>
    private static Vector128<byte> CarryIndices(int byteStride, int size)
    {
        Span<byte> indices = stackalloc byte[16];
        for (int j = 0; j < 16; j++)
        {
            int position = 16 + j;
            int last = 15;
            while ((position - last) % byteStride != 0)
            {
                last--;
            }

            indices[j] = (byte)last;
        }

        return Vector128.Create<byte>(indices);
    }
}
