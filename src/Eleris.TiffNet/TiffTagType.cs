namespace Eleris.TiffNet;

/// <summary>The field types of TIFF 6.0 and BigTIFF: how the values of a tag are stored.</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1720:Identifier contains type name",
    Justification = "The names are those of the field types in the TIFF 6.0 and BigTIFF specifications.")]
public enum TiffTagType
{
    /// <summary>8-bit unsigned integers.</summary>
    Byte = 1,

    /// <summary>8-bit characters; a string ends with a NUL byte, and a tag may hold several strings.</summary>
    Ascii = 2,

    /// <summary>16-bit unsigned integers.</summary>
    Short = 3,

    /// <summary>32-bit unsigned integers.</summary>
    Long = 4,

    /// <summary>Two 32-bit unsigned integers: a numerator and a denominator.</summary>
    Rational = 5,

    /// <summary>8-bit signed integers.</summary>
    SByte = 6,

    /// <summary>Bytes whose meaning depends on the tag.</summary>
    Undefined = 7,

    /// <summary>16-bit signed integers.</summary>
    SShort = 8,

    /// <summary>32-bit signed integers.</summary>
    SLong = 9,

    /// <summary>Two 32-bit signed integers: a numerator and a denominator.</summary>
    SRational = 10,

    /// <summary>IEEE single precision floating point numbers.</summary>
    Float = 11,

    /// <summary>IEEE double precision floating point numbers.</summary>
    Double = 12,

    /// <summary>32-bit offsets of sub-directories.</summary>
    Ifd = 13,

    /// <summary>64-bit unsigned integers (BigTIFF).</summary>
    Long8 = 16,

    /// <summary>64-bit signed integers (BigTIFF).</summary>
    SLong8 = 17,

    /// <summary>64-bit offsets of sub-directories (BigTIFF).</summary>
    Ifd8 = 18,
}
