using System.Numerics;

namespace Eleris.TiffNet.Internal.Pixels;

/// <summary>What the library knows of a sample type: its .NET type, its size, and its <c>SampleFormat</c> in TIFF.</summary>
internal readonly record struct SampleInfo(TiffSampleType Type, Type ClrType, int Size, int Format);

/// <summary>Code that runs for a sample type as the .NET type of its samples (no reflection: AOT-compatible).</summary>
internal interface ISampleVisitor
{
    void Visit<TD>() where TD : unmanaged, INumber<TD>;
}

/// <summary>
/// The one place that lists the sample types of TIFF the library handles: a table of what each is, and a dispatch that runs code
/// with the .NET type of the samples. A new sample type is added here, and nowhere else.
/// </summary>
internal static class SampleTypes
{
    // In the order of the values of TiffSampleType (1 to 11).
    private static readonly SampleInfo[] Table =
    [
        new(TiffSampleType.UInt8, typeof(byte), 1, 1),
        new(TiffSampleType.Int8, typeof(sbyte), 1, 2),
        new(TiffSampleType.UInt16, typeof(ushort), 2, 1),
        new(TiffSampleType.Int16, typeof(short), 2, 2),
        new(TiffSampleType.UInt32, typeof(uint), 4, 1),
        new(TiffSampleType.Int32, typeof(int), 4, 2),
        new(TiffSampleType.UInt64, typeof(ulong), 8, 1),
        new(TiffSampleType.Int64, typeof(long), 8, 2),
        new(TiffSampleType.Float16, typeof(Half), 2, 3),
        new(TiffSampleType.Float32, typeof(float), 4, 3),
        new(TiffSampleType.Float64, typeof(double), 8, 3),
    ];

    /// <summary>What the library knows of <paramref name="type"/>.</summary>
    public static SampleInfo Of(TiffSampleType type) => (uint)(type - 1) < (uint)Table.Length
        ? Table[(int)type - 1]
        : throw new ArgumentOutOfRangeException(nameof(type), type, "Not a sample type.");

    /// <summary>The sample type of <typeparamref name="T"/>, or none (a constant for each instantiation).</summary>
    public static TiffSampleType? Of<T>() where T : unmanaged => Cache<T>.Type;

    /// <summary>Runs <paramref name="visitor"/> with the .NET type of the samples of <paramref name="type"/>.</summary>
    public static void Visit<TVisitor>(TiffSampleType type, ref TVisitor visitor) where TVisitor : ISampleVisitor, allows ref struct
    {
        switch (type)
        {
            case TiffSampleType.UInt8: visitor.Visit<byte>(); break;
            case TiffSampleType.Int8: visitor.Visit<sbyte>(); break;
            case TiffSampleType.UInt16: visitor.Visit<ushort>(); break;
            case TiffSampleType.Int16: visitor.Visit<short>(); break;
            case TiffSampleType.UInt32: visitor.Visit<uint>(); break;
            case TiffSampleType.Int32: visitor.Visit<int>(); break;
            case TiffSampleType.UInt64: visitor.Visit<ulong>(); break;
            case TiffSampleType.Int64: visitor.Visit<long>(); break;
            case TiffSampleType.Float16: visitor.Visit<Half>(); break;
            case TiffSampleType.Float32: visitor.Visit<float>(); break;
            case TiffSampleType.Float64: visitor.Visit<double>(); break;
            default: throw new ArgumentOutOfRangeException(nameof(type), type, "Not a sample type.");
        }
    }

    private static class Cache<T> where T : unmanaged
    {
        public static readonly TiffSampleType? Type = Array.FindIndex(Table, i => i.ClrType == typeof(T)) is var index and >= 0 ? Table[index].Type : null;
    }
}
