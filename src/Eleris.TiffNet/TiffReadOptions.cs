namespace Eleris.TiffNet;

/// <summary>How a TIFF file is read: tolerance of defects, and the limits that keep a damaged or hostile file from exhausting the
/// memory or the time of the application.</summary>
/// <remarks>
/// <para>
/// The defaults suit files that you trust. For files you do not control, set the limits to what your application needs: a file
/// that would exceed one fails with a <see cref="TiffException"/> whose <see cref="TiffException.Limit"/> names it, before the
/// memory is allocated. On a phone, a <see cref="MaxPixelBytes"/> of 256 MB is a reasonable start.
/// </para>
/// <para>Change a limit with a <c>with</c> expression: <c>TiffReadOptions.Default with { MaxPixelBytes = 256L &lt;&lt; 20 }</c>.</para>
/// </remarks>
public sealed record TiffReadOptions
{
    private const long ArrayLimit = 2_147_483_591L;

    private readonly long _maxInputBytes = ArrayLimit;
    private readonly long _maxPixelBytes = ArrayLimit;
    private readonly int _maxDirectories = 10_000;
    private readonly int _maxTagsPerDirectory = 4_096;
    private readonly long _maxTagBytes = 64L << 20;
    private readonly int _maxJpegScans = 100;
    private readonly int _maxDegreeOfParallelism = -1;

    /// <summary>Creates options with the default limits; set the ones to change with an object initializer.</summary>
    public TiffReadOptions()
    {
    }

    /// <summary>The default options: tolerant reading with the default limits.</summary>
    public static TiffReadOptions Default { get; } = new();

    /// <summary>
    /// Refuses the defects that are tolerated by default (directory entries out of order, duplicate tags, tiles whose size is not
    /// a multiple of 16, a tag whose values do not fit in the file) with a <see cref="TiffException"/>.
    /// </summary>
    public bool Strict { get; init; }

    /// <summary>
    /// The number of bytes that <c>TiffFile.Read</c> takes from a stream, which it holds in memory. 2 147 483 591 (the largest
    /// .NET array) by default.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not positive.</exception>
    public long MaxInputBytes
    {
        get => _maxInputBytes;
        init => _maxInputBytes = Positive(value);
    }

    /// <summary>
    /// The memory that decoding an image or a region may use for the data the file asks for: the pixels and the intermediate
    /// buffers of the codecs (for example the coefficients of a progressive JPEG). 2 147 483 591 bytes by default.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not positive.</exception>
    public long MaxPixelBytes
    {
        get => _maxPixelBytes;
        init => _maxPixelBytes = Positive(value);
    }

    /// <summary>The number of directories read from a file: pages and sub-directories together. 10 000 by default.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not positive.</exception>
    public int MaxDirectories
    {
        get => _maxDirectories;
        init => _maxDirectories = (int)Positive(value);
    }

    /// <summary>The number of entries of one directory. 4 096 by default; real files have a few dozen.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not positive.</exception>
    public int MaxTagsPerDirectory
    {
        get => _maxTagsPerDirectory;
        init => _maxTagsPerDirectory = (int)Positive(value);
    }

    /// <summary>The size of the values of one tag. 64 MB by default, which ICC profiles and XMP packets fit in.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not positive.</exception>
    public long MaxTagBytes
    {
        get => _maxTagBytes;
        init => _maxTagBytes = Positive(value);
    }

    /// <summary>The number of scans of a progressive JPEG image. 100 by default; a normal image has about 10.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not positive.</exception>
    public int MaxJpegScans
    {
        get => _maxJpegScans;
        init => _maxJpegScans = (int)Positive(value);
    }

    /// <summary>
    /// The number of threads that decode strips and tiles at once: -1 (the default) for all the cores, 1 to decode on the calling
    /// thread only.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is 0 or less than -1.</exception>
    public int MaxDegreeOfParallelism
    {
        get => _maxDegreeOfParallelism;
        init => _maxDegreeOfParallelism = value is -1 or > 0
            ? value
            : throw new ArgumentOutOfRangeException(nameof(value), value, "-1 (all the cores) or a positive number of threads is expected.");
    }

    private static long Positive(long value) =>
        value > 0 ? value : throw new ArgumentOutOfRangeException(nameof(value), value, "A positive limit is expected.");
}
