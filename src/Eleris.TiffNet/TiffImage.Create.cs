using System.Numerics;
using System.Runtime.InteropServices;
using Eleris.TiffNet.Internal;
using Eleris.TiffNet.Internal.Pixels;
using Eleris.TiffNet.Internal.Structure;

namespace Eleris.TiffNet;

public sealed partial class TiffImage
{
    /// <summary>Creates an image from pixels of the application, which are copied once.</summary>
    /// <typeparam name="T">The type of the samples: <see cref="byte"/>, <see cref="sbyte"/>, <see cref="ushort"/>, <see cref="short"/>,
    /// <see cref="uint"/>, <see cref="int"/>, <see cref="ulong"/>, <see cref="long"/>, <see cref="Half"/>, <see cref="float"/>
    /// or <see cref="double"/>.</typeparam>
    /// <param name="pixels">Exactly <c>width × height × channels</c> samples, rows from the top.</param>
    /// <param name="width">The width, in pixels.</param>
    /// <param name="height">The height, in pixels.</param>
    /// <param name="channels">The channels per pixel: the colour channels (1 for grey, 3 for RGB), then extra channels.</param>
    /// <param name="colorModel">What the colour channels are. Required with 3 channels or more, so that a stack of filters (L,
    /// Hα, OIII) never becomes RGB by accident; 1 or 2 channels are grey when it is omitted.</param>
    /// <param name="order">How <paramref name="pixels"/> is laid out: interleaved (<c>[y][x][c]</c>) or planar (<c>[c][y][x]</c>,
    /// the order of a FITS cube). The image keeps that layout: no reordering copy.</param>
    /// <returns>An image in memory: no <see cref="Storage"/>, empty <see cref="Metadata"/>, extra channels of kind
    /// <see cref="TiffExtraChannelKind.Data"/> (see <see cref="SetExtraChannels"/>).</returns>
    /// <exception cref="ArgumentException">The sizes do not match the pixels, the colour model is missing or does not fit the
    /// channels, <typeparamref name="T"/> is not a sample type of TIFF, or the pixels are more bytes than an array holds (write
    /// them with TiffImageWriter).</exception>
    /// <exception cref="ArgumentOutOfRangeException">A size is not positive.</exception>
    public static TiffImage Create<T>(ReadOnlySpan<T> pixels, int width, int height, int channels = 1, TiffColorModel? colorModel = null,
        TiffPixelOrder order = TiffPixelOrder.Interleaved) where T : unmanaged, INumber<T>
    {
        var (descriptor, source) = Describe(pixels, width, height, channels, colorModel, order);
        var image = new TiffImage(descriptor, new TiffMetadata(), storage: null);
        image._pixels = source;
        return image;
    }

    /// <summary>Replaces the pixels of the image (and so its size, channels and sample type) by pixels of the application, copied once.</summary>
    /// <typeparam name="T">As for <see cref="Create{T}"/>.</typeparam>
    /// <param name="pixels">Exactly <c>width × height × channels</c> samples, rows from the top.</param>
    /// <param name="width">The width, in pixels.</param>
    /// <param name="height">The height, in pixels.</param>
    /// <param name="channels">The channels per pixel.</param>
    /// <param name="colorModel">What the colour channels are; required with 3 channels or more.</param>
    /// <param name="order">How <paramref name="pixels"/> is laid out.</param>
    /// <remarks>
    /// The metadata and the sub-images are kept; <see cref="Storage"/> still tells how the image was stored in its file. The
    /// extra channels become <see cref="TiffExtraChannelKind.Data"/>; a reason the library could not deliver the former pixels
    /// (<see cref="UnsupportedReason"/>) no longer applies. Like the other changes of an image, it must not run while the image
    /// is read on another thread.
    /// </remarks>
    /// <exception cref="ArgumentException">As for <see cref="Create{T}"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A size is not positive.</exception>
    public void SetPixels<T>(ReadOnlySpan<T> pixels, int width, int height, int channels = 1, TiffColorModel? colorModel = null,
        TiffPixelOrder order = TiffPixelOrder.Interleaved) where T : unmanaged, INumber<T>
    {
        var (descriptor, source) = Describe(pixels, width, height, channels, colorModel, order);
        _descriptor = descriptor.WithSubfileType(_descriptor.SubfileType);
        _pixels = source;
        ExtraChannels = Kinds(_descriptor.ExtraChannels);
    }

    /// <summary>Sets what each extra channel holds: alpha, associated (premultiplied) or not, or other data.</summary>
    /// <param name="kinds">One kind per extra channel, in order.</param>
    /// <exception cref="ArgumentException">There is not one kind per extra channel.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="kinds"/> is null.</exception>
    public void SetExtraChannels(params TiffExtraChannelKind[] kinds)
    {
        ArgumentNullException.ThrowIfNull(kinds);
        if (kinds.Length != _descriptor.ExtraChannels.Count)
        {
            throw new ArgumentException($"The image has {_descriptor.ExtraChannels.Count} extra channels, not {kinds.Length}.", nameof(kinds));
        }

        foreach (var kind in kinds)
        {
            if (!Enum.IsDefined(kind))
            {
                throw new ArgumentException($"{kind} is not a kind of extra channel.", nameof(kinds));
            }
        }

        _descriptor = _descriptor.WithExtraChannels([.. kinds]);
        ExtraChannels = Kinds(_descriptor.ExtraChannels);
    }

    private static System.Collections.ObjectModel.ReadOnlyCollection<TiffExtraChannel> Kinds(IReadOnlyList<TiffExtraChannelKind> kinds) =>
        kinds.Select(k => new TiffExtraChannel(k)).ToList().AsReadOnly();

    /// <summary>Checks the pixels of the application and copies them once, as bytes, in the order of the machine.</summary>
    private static (ImageDescriptor Descriptor, TiffSource Source) Describe<T>(ReadOnlySpan<T> pixels, int width, int height, int channels,
        TiffColorModel? colorModel, TiffPixelOrder order) where T : unmanaged, INumber<T>
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(channels);
        if (!Enum.IsDefined(order))
        {
            throw new ArgumentOutOfRangeException(nameof(order), order, "The order is interleaved or planar.");
        }

        var type = SampleTypeOf<T>();
        var model = colorModel ?? (channels <= 2
            ? TiffColorModel.Grey
            : throw new ArgumentException(
                $"{channels} channels need a colour model: Rgb, or Grey with extra channels (a stack of filters is not RGB).", nameof(colorModel)));
        if (!Enum.IsDefined(model) || (model == TiffColorModel.Rgb && channels < 3))
        {
            throw new ArgumentException($"The colour model {model} does not fit {channels} channels.", nameof(colorModel));
        }

        long count = (long)width * height * channels;
        if (pixels.Length != count)
        {
            throw new ArgumentException($"{width} x {height} pixels of {channels} channels are {count} samples, not {pixels.Length}.", nameof(pixels));
        }

        if (count * System.Runtime.CompilerServices.Unsafe.SizeOf<T>() > Array.MaxLength)
        {
            throw new ArgumentException(
                $"{count} samples of {typeof(T).Name} are more bytes than an array holds: write such an image with TiffImageWriter.", nameof(pixels));
        }

        int colours = model == TiffColorModel.Rgb ? 3 : 1;
        var extras = Enumerable.Repeat(TiffExtraChannelKind.Data, channels - colours).ToList().AsReadOnly();
        var descriptor = ImageDescriptor.ForPixels(width, height, channels, type, model, extras, order == TiffPixelOrder.Planar);
        if (descriptor.Unsupported is { } reason)
        {
            throw new ArgumentException($"These pixels cannot be held: {reason}", nameof(pixels));
        }

        // The one copy: the image does not change when the caller's buffer does.
        byte[] bytes = MemoryMarshal.AsBytes(pixels).ToArray();
        return (descriptor, TiffSource.FromMemory(bytes, "image"));
    }

    /// <summary>The sample type of <typeparamref name="T"/> (a constant for each instantiation, without reflection).</summary>
    private static TiffSampleType SampleTypeOf<T>() where T : unmanaged, INumber<T>
    {
        if (typeof(T) == typeof(byte)) return TiffSampleType.UInt8;
        if (typeof(T) == typeof(sbyte)) return TiffSampleType.Int8;
        if (typeof(T) == typeof(ushort)) return TiffSampleType.UInt16;
        if (typeof(T) == typeof(short)) return TiffSampleType.Int16;
        if (typeof(T) == typeof(uint)) return TiffSampleType.UInt32;
        if (typeof(T) == typeof(int)) return TiffSampleType.Int32;
        if (typeof(T) == typeof(ulong)) return TiffSampleType.UInt64;
        if (typeof(T) == typeof(long)) return TiffSampleType.Int64;
        if (typeof(T) == typeof(Half)) return TiffSampleType.Float16;
        if (typeof(T) == typeof(float)) return TiffSampleType.Float32;
        if (typeof(T) == typeof(double)) return TiffSampleType.Float64;
        throw new ArgumentException($"{typeof(T).Name} is not a sample type of TIFF: use byte, sbyte, ushort, short, uint, int, ulong, long, Half, float or double.");
    }
}
