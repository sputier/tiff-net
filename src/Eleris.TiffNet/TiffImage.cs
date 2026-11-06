using Eleris.TiffNet.Internal.Structure;

namespace Eleris.TiffNet;

/// <summary>An extra channel of an image, after its colour channels.</summary>
/// <param name="Kind">What the channel holds.</param>
public sealed record TiffExtraChannel(TiffExtraChannelKind Kind);

/// <summary>
/// One image of a TIFF file: its geometry, its samples and, on demand, its pixels.
/// </summary>
/// <remarks>
/// An image whose format the library does not handle (CMYK, Lab, a compression it does not read…) is still listed with the pages
/// of its file, with its size and the reason in <see cref="UnsupportedReason"/>; its <see cref="SampleType"/>,
/// <see cref="ColorModel"/> and pixels throw a <see cref="NotSupportedException"/> that gives the reason.
/// <para>
/// An image can be read from several threads at once. Changing it (<see cref="SetPixels{T}"/>, <see cref="SetExtraChannels"/>,
/// its <see cref="Metadata"/>) must not happen while it is read.
/// </para>
/// </remarks>
public sealed partial class TiffImage
{
    private ImageDescriptor _descriptor;

    /// <summary>The pixels given by the application (<see cref="Create{T}"/>, <see cref="SetPixels{T}"/>), or null.</summary>
    private Internal.TiffSource? _pixels;

    private readonly List<TiffImage> _subImages = [];

    internal TiffImage(ImageDescriptor descriptor, TiffMetadata metadata, TiffStorage? storage)
    {
        _descriptor = descriptor;
        Metadata = metadata;
        Storage = storage;
        SubImages = _subImages.AsReadOnly();
        ExtraChannels = descriptor.ExtraChannels.Select(k => new TiffExtraChannel(k)).ToList().AsReadOnly();
    }

    /// <summary>The width of the delivered image, in pixels: with its orientation applied (an image stored on its side, Orientation 5
    /// to 8, has the stored height as its width).</summary>
    /// <exception cref="NotSupportedException">The image is wider than an <see cref="int"/> can count.</exception>
    public int Width => Dimension(Transposed ? _descriptor.Height : _descriptor.Width);

    /// <summary>The height of the delivered image, in pixels, with its orientation applied.</summary>
    /// <exception cref="NotSupportedException">The image is taller than an <see cref="int"/> can count.</exception>
    public int Height => Dimension(Transposed ? _descriptor.Width : _descriptor.Height);

    /// <summary>The number of channels: the colour channels (1 for grey, 3 for RGB), then the extra channels.</summary>
    public int Channels => _descriptor.Channels;

    /// <summary>The type of the samples the image delivers.</summary>
    /// <exception cref="NotSupportedException">The library does not handle the format of the image (<see cref="UnsupportedReason"/>).</exception>
    public TiffSampleType SampleType => _descriptor.Unsupported is null ? _descriptor.SampleType : throw Unsupported();

    /// <summary>What the colour channels are, as the file says: never inferred from the number of channels.</summary>
    /// <exception cref="NotSupportedException">The library does not handle the format of the image (<see cref="UnsupportedReason"/>).</exception>
    public TiffColorModel ColorModel => _descriptor.Unsupported is null ? _descriptor.ColorModel : throw Unsupported();

    /// <summary>The channels after the colour channels, with what they hold (see <see cref="SetExtraChannels"/>).</summary>
    public IReadOnlyList<TiffExtraChannel> ExtraChannels { get; private set; }

    /// <summary>Whether the image is a reduced-resolution version of another (a thumbnail, a level of a pyramid).</summary>
    public bool IsReducedResolution => _descriptor.IsReducedResolution;

    /// <summary>The images of the sub-directories (<c>SubIFDs</c>) of this one, usually reduced-resolution versions of it.</summary>
    public IReadOnlyList<TiffImage> SubImages { get; }

    /// <summary>
    /// The tags of the image other than its structure: description (the FITS header of an Astro-TIFF file), ICC profile, resolution,
    /// private tags… Read with the image and kept when it is saved again.
    /// </summary>
    public TiffMetadata Metadata { get; }

    /// <summary>How the image is stored in the file it was read from; <see langword="null"/> for an image created in memory.</summary>
    public TiffStorage? Storage { get; }

    /// <summary>Why the library cannot deliver this image, or <see langword="null"/> when it can.</summary>
    public string? UnsupportedReason => _descriptor.Unsupported;

    /// <summary>The file the image was read from.</summary>
    internal TiffFile? File { get; private set; }

    internal ImageDescriptor Descriptor => _descriptor;

    /// <summary>Whether the pixels are those the application gave (in memory), not those of a file.</summary>
    internal bool InMemory => _pixels is not null;

    /// <summary>
    /// Where the pixels are read from, resolved once: the pixels of the application (in the order of the machine, the default
    /// limits), or the file of the image (its byte order, the limits it was opened with).
    /// </summary>
    /// <exception cref="InvalidOperationException">The image has neither (an image of a file whose file is not attached yet).</exception>
    private PixelOrigin Origin => _pixels is { } pixels
        ? new PixelOrigin(pixels, BitConverter.IsLittleEndian, TiffReadOptions.Default, "image")
        : File is { } file && Storage is { } storage
            ? new PixelOrigin(file.Source, !storage.BigEndian, file.Options, file.Source.Name)
            : throw new InvalidOperationException("The image has no pixels to read.");

    /// <summary>The source of the pixels of an image, the byte order of its samples and the limits of its reads.</summary>
    private readonly record struct PixelOrigin(Internal.TiffSource Source, bool LittleEndian, TiffReadOptions Options, string Name);

    internal bool IsFullResolution => !_descriptor.IsReducedResolution && !_descriptor.IsMask;

    /// <summary>Whether the orientation swaps rows and columns (Orientation 5 to 8).</summary>
    private bool Transposed => _descriptor.Orientation >= 5;

    internal void AddSubImage(TiffImage image) => _subImages.Add(image);

    /// <summary>Links the image and its sub-images, at any depth, to their file, without recursion.</summary>
    internal void Attach(TiffFile file)
    {
        var pending = new Stack<TiffImage>([this]);
        while (pending.TryPop(out var image))
        {
            image.File = file;
            foreach (var sub in image._subImages)
            {
                pending.Push(sub);
            }
        }
    }

    private int Dimension(long value) => value <= int.MaxValue ? (int)value : throw Unsupported();

    private NotSupportedException Unsupported() =>
        new($"{File?.Source.Name ?? "image"}: {_descriptor.Unsupported ?? $"images of {_descriptor.Width} x {_descriptor.Height} pixels are not supported."}");
}
