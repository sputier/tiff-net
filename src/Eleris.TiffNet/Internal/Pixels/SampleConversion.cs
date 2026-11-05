using System.Numerics;

namespace Eleris.TiffNet.Internal.Pixels;

/// <summary>The conversions the pixel API allows from a sample type to a requested type.</summary>
internal static class SampleConversion
{
    /// <summary>The .NET type of a sample type.</summary>
    public static Type ClrType(TiffSampleType type) => type switch
    {
        TiffSampleType.UInt8 => typeof(byte),
        TiffSampleType.Int8 => typeof(sbyte),
        TiffSampleType.UInt16 => typeof(ushort),
        TiffSampleType.Int16 => typeof(short),
        TiffSampleType.UInt32 => typeof(uint),
        TiffSampleType.Int32 => typeof(int),
        TiffSampleType.UInt64 => typeof(ulong),
        TiffSampleType.Int64 => typeof(long),
        TiffSampleType.Float16 => typeof(Half),
        TiffSampleType.Float32 => typeof(float),
        _ => typeof(double),
    };

    /// <exception cref="InvalidOperationException">The samples cannot be read as <typeparamref name="T"/> that way.</exception>
    public static void Check<T>(TiffSampleType stored, TiffPixelScale scale, bool raw) where T : unmanaged, INumber<T>
    {
        var target = typeof(T);
        var source = ClrType(stored);
        if (raw)
        {
            if (target != source)
            {
                throw new InvalidOperationException($"The samples are {stored}: read them raw as {source.Name}, not {target.Name}.");
            }

            return;
        }

        bool floatingTarget = target == typeof(float) || target == typeof(double);
        if (scale == TiffPixelScale.Normalized)
        {
            if (!floatingTarget)
            {
                throw new InvalidOperationException($"Normalized samples are read as float or double, not {target.Name}.");
            }

            return;
        }

        if (floatingTarget || target == source)
        {
            return;
        }

        if (!Lossless(source, target))
        {
            throw new InvalidOperationException(
                $"The samples are {stored}: reading them as {target.Name} could lose values; read them as float, double or a wider type.");
        }
    }

    private static bool Lossless(Type source, Type target)
    {
        var (sourceBits, sourceSigned, sourceInteger) = Describe(source);
        var (targetBits, targetSigned, targetInteger) = Describe(target);
        if (!sourceInteger || !targetInteger)
        {
            return false;
        }

        return sourceSigned
            ? targetSigned && targetBits >= sourceBits
            : targetSigned ? targetBits > sourceBits : targetBits >= sourceBits;
    }

    private static (int Bits, bool Signed, bool Integer) Describe(Type t) => t switch
    {
        _ when t == typeof(byte) => (8, false, true),
        _ when t == typeof(sbyte) => (8, true, true),
        _ when t == typeof(ushort) => (16, false, true),
        _ when t == typeof(short) => (16, true, true),
        _ when t == typeof(uint) => (32, false, true),
        _ when t == typeof(int) => (32, true, true),
        _ when t == typeof(ulong) => (64, false, true),
        _ when t == typeof(long) => (64, true, true),
        _ when t == typeof(UInt128) => (128, false, true),
        _ when t == typeof(Int128) => (128, true, true),
        _ => (0, true, false),
    };
}
