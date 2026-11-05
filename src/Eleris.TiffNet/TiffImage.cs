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
/// </remarks>
public sealed class TiffImage
{
    private readonly ImageDescriptor _descriptor;

    internal TiffImage(ImageDescriptor descriptor, TiffMetadata metadata, IReadOnlyList<TiffImage> subImages)
    {
        _descriptor = descriptor;
        Metadata = metadata;
        SubImages = subImages;
        ExtraChannels = [.. descriptor.ExtraChannels.Select(k => new TiffExtraChannel(k))];
    }

    /// <summary>The width, in pixels.</summary>
    /// <exception cref="NotSupportedException">The image is wider than an <see cref="int"/> can count.</exception>
    public int Width => _descriptor.Width <= int.MaxValue ? (int)_descriptor.Width : throw Unsupported();

    /// <summary>The height, in pixels.</summary>
    /// <exception cref="NotSupportedException">The image is taller than an <see cref="int"/> can count.</exception>
    public int Height => _descriptor.Height <= int.MaxValue ? (int)_descriptor.Height : throw Unsupported();

    /// <summary>The number of channels: the colour channels (1 for grey, 3 for RGB), then the extra channels.</summary>
    public int Channels => _descriptor.Channels;

    /// <summary>The type of the samples the image delivers.</summary>
    /// <exception cref="NotSupportedException">The library does not handle the format of the image (<see cref="UnsupportedReason"/>).</exception>
    public TiffSampleType SampleType => _descriptor.Unsupported is null ? _descriptor.SampleType : throw Unsupported();

    /// <summary>What the colour channels are, as the file says: never inferred from the number of channels.</summary>
    /// <exception cref="NotSupportedException">The library does not handle the format of the image (<see cref="UnsupportedReason"/>).</exception>
    public TiffColorModel ColorModel => _descriptor.Unsupported is null ? _descriptor.ColorModel : throw Unsupported();

    /// <summary>The channels after the colour channels, with what they hold.</summary>
    public IReadOnlyList<TiffExtraChannel> ExtraChannels { get; }

    /// <summary>Whether the image is a reduced-resolution version of another (a thumbnail, a level of a pyramid).</summary>
    public bool IsReducedResolution => _descriptor.IsReducedResolution;

    /// <summary>The images of the sub-directories (<c>SubIFDs</c>) of this one, usually reduced-resolution versions of it.</summary>
    public IReadOnlyList<TiffImage> SubImages { get; }

    /// <summary>
    /// The tags of the image other than its structure: description (the FITS header of an Astro-TIFF file), ICC profile, resolution,
    /// private tags… Read with the image and kept when it is saved again.
    /// </summary>
    public TiffMetadata Metadata { get; }

    /// <summary>Why the library cannot deliver this image, or <see langword="null"/> when it can.</summary>
    public string? UnsupportedReason => _descriptor.Unsupported;

    /// <summary>The file the image was read from.</summary>
    internal TiffFile? File { get; private set; }

    internal ImageDescriptor Descriptor => _descriptor;

    internal bool IsFullResolution => !_descriptor.IsReducedResolution && !_descriptor.IsMask;

    internal void Attach(TiffFile file)
    {
        File = file;
        foreach (var sub in SubImages)
        {
            sub.Attach(file);
        }
    }

    private NotSupportedException Unsupported() =>
        new($"{File?.Source.Name ?? "image"}: {_descriptor.Unsupported ?? $"images of {_descriptor.Width} x {_descriptor.Height} pixels are not supported."}");
}
