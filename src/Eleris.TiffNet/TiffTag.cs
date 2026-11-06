using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;
using Eleris.TiffNet.Internal.Structure;

namespace Eleris.TiffNet;

/// <summary>One tag of a TIFF directory: its code, the type of its values and the values, independent of the byte order of the file.</summary>
/// <remarks>Two tags are equal when their code, type and values are.</remarks>
public sealed class TiffTag : IEquatable<TiffTag>
{
    // The values, little endian.
    private readonly byte[] _values;

    private TiffTag(int code, TiffTagType type, byte[] values)
    {
        if (code is < 0 or > ushort.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(code), code, "A tag code is between 0 and 65535.");
        }

        Code = code;
        Type = type;
        _values = values;
    }

    /// <summary>The code of the tag, for example 270 for <c>ImageDescription</c>.</summary>
    public int Code { get; }

    /// <summary>How the values are stored.</summary>
    public TiffTagType Type { get; }

    /// <summary>The number of values (characters, NUL bytes included, for <see cref="TiffTagType.Ascii"/>).</summary>
    public int Count => _values.Length / TagTypes.Size(Type);

    /// <summary>A text tag (<see cref="TiffTagType.Ascii"/>), in ASCII when the text is, in UTF-8 otherwise, ended by a NUL byte.</summary>
    /// <param name="code">The code of the tag.</param>
    /// <param name="value">The text.</param>
    public static TiffTag FromString(int code, string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new TiffTag(code, TiffTagType.Ascii, [.. Encoding.UTF8.GetBytes(value), 0]);
    }

    /// <summary>A tag of bytes, as they are.</summary>
    /// <param name="code">The code of the tag.</param>
    /// <param name="value">The bytes.</param>
    /// <param name="type">A type of one-byte values: <see cref="TiffTagType.Undefined"/>, <see cref="TiffTagType.Byte"/>,
    /// <see cref="TiffTagType.SByte"/> or <see cref="TiffTagType.Ascii"/>.</param>
    public static TiffTag FromBytes(int code, ReadOnlySpan<byte> value, TiffTagType type = TiffTagType.Undefined)
    {
        if (TagTypes.Size(type) != 1)
        {
            throw new ArgumentException($"{type} values are not single bytes; use FromNumbers.", nameof(type));
        }

        return new TiffTag(code, type, value.ToArray());
    }

    /// <summary>A tag of numbers, whose type follows from <typeparamref name="T"/> (for example <see cref="TiffTagType.Short"/> for
    /// <see cref="ushort"/>, <see cref="TiffTagType.Double"/> for <see cref="double"/>).</summary>
    /// <typeparam name="T"><see cref="byte"/>, <see cref="sbyte"/>, <see cref="ushort"/>, <see cref="short"/>, <see cref="uint"/>,
    /// <see cref="int"/>, <see cref="ulong"/>, <see cref="long"/>, <see cref="float"/> or <see cref="double"/>.</typeparam>
    /// <param name="code">The code of the tag.</param>
    /// <param name="values">The values.</param>
    public static TiffTag FromNumbers<T>(int code, ReadOnlySpan<T> values) where T : unmanaged
    {
        var type = typeof(T) switch
        {
            var t when t == typeof(byte) => TiffTagType.Byte,
            var t when t == typeof(sbyte) => TiffTagType.SByte,
            var t when t == typeof(ushort) => TiffTagType.Short,
            var t when t == typeof(short) => TiffTagType.SShort,
            var t when t == typeof(uint) => TiffTagType.Long,
            var t when t == typeof(int) => TiffTagType.SLong,
            var t when t == typeof(ulong) => TiffTagType.Long8,
            var t when t == typeof(long) => TiffTagType.SLong8,
            var t when t == typeof(float) => TiffTagType.Float,
            var t when t == typeof(double) => TiffTagType.Double,
            _ => throw new ArgumentException($"{typeof(T).Name} is not a type of TIFF values.", nameof(values)),
        };
        // The machine is little endian (checked at start-up): the values are already in the order the tag keeps.
        return new TiffTag(code, type, MemoryMarshal.AsBytes(values).ToArray());
    }

    /// <summary>A tag of rationals (<see cref="TiffTagType.Rational"/>), each a numerator and a denominator.</summary>
    internal static TiffTag FromRationals(int code, ReadOnlySpan<(uint Numerator, uint Denominator)> values)
    {
        var bytes = new byte[values.Length * 8];
        for (int i = 0; i < values.Length; i++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(i * 8), values[i].Numerator);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(i * 8 + 4), values[i].Denominator);
        }

        return new TiffTag(code, TiffTagType.Rational, bytes);
    }

    /// <summary>A tag read from a file: the values are put in little-endian order, value by value.</summary>
    internal static TiffTag FromFile(int code, TiffTagType type, byte[] values, bool littleEndian)
    {
        int size = TagTypes.Size(type);
        int part = type is TiffTagType.Rational or TiffTagType.SRational ? 4 : size;
        if (!littleEndian && part > 1)
        {
            for (int i = 0; i + part <= values.Length; i += part)
            {
                values.AsSpan(i, part).Reverse();
            }
        }

        return new TiffTag(code, type, values);
    }

    /// <summary>The text of the tag: up to the first NUL byte, decoded as UTF-8 when it is valid UTF-8 (ASCII is), as Latin-1 otherwise.</summary>
    /// <exception cref="InvalidOperationException">The tag holds numbers.</exception>
    public string AsString()
    {
        EnsureText();
        int end = _values.AsSpan().IndexOf((byte)0);
        return Decode(end < 0 ? _values : _values.AsSpan(0, end));
    }

    /// <summary>The texts of a tag that holds several, separated by NUL bytes.</summary>
    /// <remarks>A string per text: a tag of a file from anywhere can hold millions (up to <see cref="TiffReadOptions.MaxTagBytes"/>).</remarks>
    /// <exception cref="InvalidOperationException">The tag holds numbers.</exception>
    public IReadOnlyList<string> AsStrings()
    {
        EnsureText();

        var texts = new List<string>();
        ReadOnlySpan<byte> rest = _values;
        while (rest.Length > 0)
        {
            int end = rest.IndexOf((byte)0);
            ReadOnlySpan<byte> text = end < 0 ? rest : rest[..end];
            texts.Add(Decode(text));
            rest = end < 0 ? [] : rest[(end + 1)..];
        }

        return texts;
    }

    /// <summary>The values as numbers of type <typeparamref name="T"/>.</summary>
    /// <remarks>An array of <see cref="Count"/> values: check it first for a tag of a file from anywhere, which can hold millions.</remarks>
    /// <typeparam name="T">A numeric type that can hold every value: any for integers that fit, <see cref="float"/> or
    /// <see cref="double"/> for floating point values and rationals (numerator divided by denominator).</typeparam>
    /// <returns>The values.</returns>
    /// <exception cref="InvalidOperationException">A value cannot be represented as <typeparamref name="T"/>, or the tag holds text.</exception>
    public T[] AsNumbers<T>() where T : unmanaged, System.Numerics.INumber<T>
    {
        EnsureNumbers();
        int size = TagTypes.Size(Type);
        var result = new T[Count];
        for (int i = 0; i < result.Length; i++)
        {
            result[i] = NumberAt<T>(i, size);
        }

        return result;
    }

    /// <summary>The first value, without the others (a resolution of a hostile file may hold millions).</summary>
    internal T FirstNumber<T>() where T : unmanaged, System.Numerics.INumber<T>
    {
        EnsureNumbers();
        return NumberAt<T>(0, TagTypes.Size(Type));
    }

    private void EnsureNumbers()
    {
        if (Type is TiffTagType.Ascii or TiffTagType.Undefined)
        {
            throw new InvalidOperationException($"Tag {Code} holds {Type} values, not numbers.");
        }
    }

    private T NumberAt<T>(int i, int size) where T : unmanaged, System.Numerics.INumber<T>
    {
        ReadOnlySpan<byte> v = _values.AsSpan(i * size, size);
        try
        {
        return Type switch
            {
                TiffTagType.Byte => T.CreateChecked(v[0]),
                TiffTagType.SByte => T.CreateChecked((sbyte)v[0]),
                TiffTagType.Short => T.CreateChecked(BinaryPrimitives.ReadUInt16LittleEndian(v)),
                TiffTagType.SShort => T.CreateChecked(BinaryPrimitives.ReadInt16LittleEndian(v)),
                TiffTagType.Long or TiffTagType.Ifd => T.CreateChecked(BinaryPrimitives.ReadUInt32LittleEndian(v)),
                TiffTagType.SLong => T.CreateChecked(BinaryPrimitives.ReadInt32LittleEndian(v)),
                TiffTagType.Long8 or TiffTagType.Ifd8 => T.CreateChecked(BinaryPrimitives.ReadUInt64LittleEndian(v)),
                TiffTagType.SLong8 => T.CreateChecked(BinaryPrimitives.ReadInt64LittleEndian(v)),
                TiffTagType.Float => Real(BinaryPrimitives.ReadSingleLittleEndian(v)),
                TiffTagType.Double => Real(BinaryPrimitives.ReadDoubleLittleEndian(v)),
                TiffTagType.Rational => Real((double)BinaryPrimitives.ReadUInt32LittleEndian(v) / BinaryPrimitives.ReadUInt32LittleEndian(v[4..])),
                _ => Real((double)BinaryPrimitives.ReadInt32LittleEndian(v) / BinaryPrimitives.ReadInt32LittleEndian(v[4..])),
            };
        }
        catch (OverflowException)
        {
            throw new InvalidOperationException($"Value {i} of tag {Code} ({Type}) does not fit in {typeof(T).Name}.");
        }

        T Real(double value) => typeof(T) == typeof(double) || typeof(T) == typeof(float) || typeof(T) == typeof(Half)
            ? T.CreateChecked(value)
            : throw new InvalidOperationException($"Tag {Code} holds {Type} values: read them as float or double.");
    }

    /// <summary>The values as bytes, little endian whatever the byte order of the file.</summary>
    public ReadOnlyMemory<byte> AsBytes() => _values;

    /// <summary>Whether two tags have the same code, type and values.</summary>
    /// <param name="other">The other tag.</param>
    public bool Equals(TiffTag? other) =>
        other is not null && Code == other.Code && Type == other.Type && _values.AsSpan().SequenceEqual(other._values);

    /// <summary>Whether <paramref name="obj"/> is a tag with the same code, type and values.</summary>
    /// <param name="obj">The other object.</param>
    public override bool Equals(object? obj) => Equals(obj as TiffTag);

    /// <summary>A hash of the code, type and number of values.</summary>
    public override int GetHashCode() => HashCode.Combine(Code, Type, _values.Length);

    /// <summary>The code, type and count of the tag.</summary>
    public override string ToString() => $"Tag {Code}: {Count} x {Type}";

    private void EnsureText()
    {
        if (TagTypes.Size(Type) != 1)
        {
            throw new InvalidOperationException($"Tag {Code} holds {Type} values, not text.");
        }
    }

    private static string Decode(ReadOnlySpan<byte> text) =>
        System.Text.Unicode.Utf8.IsValid(text) ? Encoding.UTF8.GetString(text) : Encoding.Latin1.GetString(text);
}
