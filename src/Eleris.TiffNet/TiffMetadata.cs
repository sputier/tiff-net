using System.Collections;
using System.Globalization;

namespace Eleris.TiffNet;

/// <summary>The unit of a resolution.</summary>
public enum TiffResolutionUnit
{
    /// <summary>No absolute unit: the values only give the aspect ratio of a pixel.</summary>
    None = 1,

    /// <summary>Pixels per inch.</summary>
    Inch = 2,

    /// <summary>Pixels per centimetre.</summary>
    Centimeter = 3,
}

/// <summary>The resolution of an image: pixels per unit, horizontally and vertically.</summary>
/// <param name="X">Pixels per unit along a row.</param>
/// <param name="Y">Pixels per unit along a column.</param>
/// <param name="Unit">The unit.</param>
public sealed record TiffResolution(double X, double Y, TiffResolutionUnit Unit);

/// <summary>
/// The tags of an image other than its structure: description, software, date, resolution, ICC profile, XMP, private tags…
/// They are kept as they are when an image is read and saved again.
/// </summary>
/// <remarks>
/// <para>
/// The tags of the structure are not here: the library computes them when it writes (size, samples, photometric interpretation,
/// compression, predictor, strips and tiles, planar configuration, orientation, extra samples, sub-file type, sub-directories,
/// colour map, JPEG and YCbCr tables). Tags that hold offsets into the file (<c>Ifd</c> types, <c>FreeOffsets</c>) are not kept
/// either: they would point nowhere in another file. The EXIF and GPS sub-directories are read into <see cref="Exif"/> and
/// <see cref="Gps"/>.
/// </para>
/// <para>Text is read as UTF-8 when it is valid UTF-8 (ASCII is), as Latin-1 otherwise; it is written as UTF-8.</para>
/// <para>
/// Reading the properties of metadata from several threads is safe; changing it while another thread reads it is not, as for the
/// .NET collections. The byte array properties return a copy on each call.
/// </para>
/// </remarks>
public sealed class TiffMetadata : IEnumerable<TiffTag>
{
    // Tag codes.
    private const int DocumentNameTag = 269, DescriptionTag = 270, PageNameTag = 285, XResolutionTag = 282, YResolutionTag = 283,
        ResolutionUnitTag = 296, SoftwareTag = 305, DateTimeTag = 306, ArtistTag = 315, HostComputerTag = 316, XmpTag = 700,
        MinSampleValueTag = 340, MaxSampleValueTag = 341, CopyrightTag = 33432, IptcTag = 33723, IccProfileTag = 34675;

    private const string DateTimeFormat = "yyyy:MM:dd HH:mm:ss";

    /// <summary>The tags of the structure of an image, which the library computes, and the tags of offsets.</summary>
    internal static readonly HashSet<int> Excluded =
    [
        254, 255, 256, 257, 258, 259, 262, 266, 273, 274, 277, 278, 279, 284, 288, 289, 292, 293, 317, 320, 322, 323, 324, 325,
        330, 338, 339, 347, 512, 513, 514, 515, 517, 518, 519, 520, 521, 529, 530, 531, 532, 32997, 32998, 34665, 34853, 40965,
    ];

    private readonly SortedDictionary<int, TiffTag> _tags = [];

    /// <summary>Creates empty metadata.</summary>
    public TiffMetadata()
    {
    }

    internal TiffMetadata(IEnumerable<TiffTag> tags, IReadOnlyList<TiffTag> exif, IReadOnlyList<TiffTag> gps)
    {
        foreach (var tag in tags)
        {
            _tags[tag.Code] = tag;
        }

        Exif = exif.ToList().AsReadOnly();
        Gps = gps.ToList().AsReadOnly();
    }

    /// <summary>The text that describes the image (<c>ImageDescription</c>, 270). Astro-TIFF stores the FITS header here.</summary>
    public string? Description { get => Text(DescriptionTag); set => SetText(DescriptionTag, value); }

    /// <summary>The name of the document the image was scanned from (<c>DocumentName</c>, 269).</summary>
    public string? DocumentName { get => Text(DocumentNameTag); set => SetText(DocumentNameTag, value); }

    /// <summary>The name of the page (<c>PageName</c>, 285).</summary>
    public string? PageName { get => Text(PageNameTag); set => SetText(PageNameTag, value); }

    /// <summary>The software that wrote the image (<c>Software</c>, 305).</summary>
    public string? Software { get => Text(SoftwareTag); set => SetText(SoftwareTag, value); }

    /// <summary>The person who made the image (<c>Artist</c>, 315).</summary>
    public string? Artist { get => Text(ArtistTag); set => SetText(ArtistTag, value); }

    /// <summary>The computer the image was made on (<c>HostComputer</c>, 316).</summary>
    public string? HostComputer { get => Text(HostComputerTag); set => SetText(HostComputerTag, value); }

    /// <summary>The copyright notice (<c>Copyright</c>, 33432).</summary>
    public string? Copyright { get => Text(CopyrightTag); set => SetText(CopyrightTag, value); }

    /// <summary>
    /// When the image was made (<c>DateTime</c>, 306), a local time without zone as TIFF defines it; <see langword="null"/> when
    /// absent or not in the format of the specification (the tag itself is still there).
    /// </summary>
    public DateTime? DateTime
    {
        get => Text(DateTimeTag) is { } text
            && System.DateTime.TryParseExact(text.Trim(), DateTimeFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var value)
                ? value
                : null;
        set => SetText(DateTimeTag, value?.ToString(DateTimeFormat, CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// The resolution (<c>XResolution</c>, <c>YResolution</c>, <c>ResolutionUnit</c>; 282, 283, 296); the unit is inches when
    /// the file does not say, as TIFF specifies.
    /// </summary>
    public TiffResolution? Resolution
    {
        get
        {
            if (!_tags.TryGetValue(XResolutionTag, out var x) || !_tags.TryGetValue(YResolutionTag, out var y))
            {
                return null;
            }

            // An unknown unit is read as inches, as libtiff does; a value that is not a finite positive number is no resolution.
            var unit = _tags.TryGetValue(ResolutionUnitTag, out var u) && Number(u) is { } code && code is 1 or 2 or 3
                ? (TiffResolutionUnit)(int)code
                : TiffResolutionUnit.Inch;
            return Number(x) is { } xs && Number(y) is { } ys && double.IsFinite(xs) && double.IsFinite(ys) && xs > 0 && ys > 0
                ? new TiffResolution(xs, ys, unit)
                : null;
        }

        set
        {
            if (value is null)
            {
                Remove(XResolutionTag);
                Remove(YResolutionTag);
                Remove(ResolutionUnitTag);
                return;
            }

            if (!Enum.IsDefined(value.Unit))
            {
                throw new ArgumentOutOfRangeException(nameof(value), value.Unit, "The unit is not a TIFF resolution unit.");
            }

            Put(TiffTag.FromRationals(XResolutionTag, [Rational(value.X)]));
            Put(TiffTag.FromRationals(YResolutionTag, [Rational(value.Y)]));
            Put(TiffTag.FromNumbers<ushort>(ResolutionUnitTag, [(ushort)value.Unit]));
        }
    }

    /// <summary>
    /// The smallest and largest sample values the image holds (<c>SMinSampleValue</c>, <c>SMaxSampleValue</c>; 340, 341), when the
    /// writer recorded them: for example 0 and 1 for normalised floating point data. When the tags give one value per channel, those
    /// of the first channel; the tags themselves are in <c>this[340]</c> and <c>this[341]</c>.
    /// </summary>
    public (double Min, double Max)? SampleRange
    {
        get => _tags.TryGetValue(MinSampleValueTag, out var min) && _tags.TryGetValue(MaxSampleValueTag, out var max)
            && Number(min) is { } a && Number(max) is { } b
                ? (a, b)
                : null;
        set
        {
            if (value is not { } range)
            {
                Remove(MinSampleValueTag);
                Remove(MaxSampleValueTag);
                return;
            }

            Put(TiffTag.FromNumbers<double>(MinSampleValueTag, [range.Min]));
            Put(TiffTag.FromNumbers<double>(MaxSampleValueTag, [range.Max]));
        }
    }

    /// <summary>The ICC colour profile of the image (34675), as it is.</summary>
    public byte[]? IccProfile { get => Bytes(IccProfileTag); set => SetBytes(IccProfileTag, value); }

    /// <summary>The XMP packet of the image (700), as it is.</summary>
    public byte[]? Xmp { get => Bytes(XmpTag); set => SetBytes(XmpTag, value); }

    /// <summary>The IPTC metadata of the image (33723), as it is.</summary>
    public byte[]? Iptc { get => Bytes(IptcTag); set => SetBytes(IptcTag, value); }

    /// <summary>The tags of the EXIF sub-directory (34665), read only.</summary>
    public IReadOnlyList<TiffTag> Exif { get; } = [];

    /// <summary>The tags of the GPS sub-directory (34853), read only.</summary>
    public IReadOnlyList<TiffTag> Gps { get; } = [];

    /// <summary>The number of tags.</summary>
    public int Count => _tags.Count;

    /// <summary>A tag by its code, or <see langword="null"/>.</summary>
    /// <param name="code">The code of the tag.</param>
    public TiffTag? this[int code] => _tags.GetValueOrDefault(code);

    /// <summary>Adds a tag, or replaces the tag of the same code.</summary>
    /// <param name="tag">The tag.</param>
    /// <exception cref="ArgumentException">The tag is one the library computes, or holds offsets (see the remarks of the class).</exception>
    public void Set(TiffTag tag)
    {
        ArgumentNullException.ThrowIfNull(tag);
        if (Excluded.Contains(tag.Code) || tag.Type is TiffTagType.Ifd or TiffTagType.Ifd8)
        {
            throw new ArgumentException($"Tag {tag.Code} describes the structure of the image or holds offsets: the library writes it.", nameof(tag));
        }

        _tags[tag.Code] = tag;
    }

    /// <summary>Removes a tag.</summary>
    /// <param name="code">The code of the tag.</param>
    /// <returns>Whether there was such a tag.</returns>
    public bool Remove(int code) => _tags.Remove(code);

    /// <summary>The tags, by code.</summary>
    public IEnumerator<TiffTag> GetEnumerator() => _tags.Values.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    private void Put(TiffTag tag) => _tags[tag.Code] = tag;

    private string? Text(int code) => _tags.TryGetValue(code, out var tag) && tag.Type is TiffTagType.Ascii or TiffTagType.Byte or TiffTagType.Undefined
        ? tag.AsString()
        : null;

    private void SetText(int code, string? value)
    {
        if (value is null)
        {
            Remove(code);
        }
        else
        {
            Put(TiffTag.FromString(code, value));
        }
    }

    private byte[]? Bytes(int code) => _tags.TryGetValue(code, out var tag) ? tag.AsBytes().ToArray() : null;

    private void SetBytes(int code, byte[]? value)
    {
        if (value is null)
        {
            Remove(code);
        }
        else
        {
            Put(TiffTag.FromBytes(code, value, code == XmpTag ? TiffTagType.Byte : TiffTagType.Undefined));
        }
    }

    private static double? Number(TiffTag tag)
    {
        if (tag.Count == 0 || tag.Type is TiffTagType.Ascii or TiffTagType.Undefined)
        {
            return null;
        }

        return tag.FirstNumber<double>();
    }

    /// <summary>The closest fraction with a numerator and a denominator of 32 bits (continued fractions).</summary>
    internal static (uint Numerator, uint Denominator) Rational(double value)
    {
        if (!double.IsFinite(value) || value <= 0 || value > uint.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, "A resolution is a positive number below 2^32.");
        }

        // Convergents h/k of the continued fraction of the value, while they fit in 32 bits and are not exact yet.
        (double h0, double h1, double k0, double k1) = (0, 1, 1, 0);
        double x = value;
        for (int i = 0; i < 64; i++)
        {
            double a = Math.Floor(x);
            (double h2, double k2) = (a * h1 + h0, a * k1 + k0);
            if (h2 > uint.MaxValue || k2 > uint.MaxValue)
            {
                break;
            }

            (h0, h1, k0, k1) = (h1, h2, k1, k2);
            if (Math.Abs(h1 / k1 - value) <= value * 1e-15 || x - a < 1e-12)
            {
                break;
            }

            x = 1 / (x - a);
        }

        return k1 == 0 ? ((uint)Math.Round(value), 1u) : ((uint)h1, (uint)k1);
    }
}
