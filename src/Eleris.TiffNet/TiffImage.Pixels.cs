using System.Globalization;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Eleris.TiffNet.Internal;
using Eleris.TiffNet.Internal.Pixels;

namespace Eleris.TiffNet;

public sealed partial class TiffImage
{
    /// <summary>The samples as stored (orientation applied, palette expanded, MinIsWhite inverted), of exactly their type.</summary>
    /// <typeparam name="T">The type of <see cref="SampleType"/>: <see cref="byte"/> for <see cref="TiffSampleType.UInt8"/>,
    /// <see cref="Half"/> for <see cref="TiffSampleType.Float16"/>…</typeparam>
    /// <param name="order">Interleaved (<c>[y][x][c]</c>) or planar (<c>[c][y][x]</c>, the order of a FITS cube).</param>
    /// <returns>A new array of <c>Width × Height × Channels</c> samples, rows from the top.</returns>
    /// <exception cref="InvalidOperationException"><typeparamref name="T"/> is not the type of the samples, or the image holds
    /// more samples than an array can (read it by region).</exception>
    /// <exception cref="NotSupportedException">The library does not handle the format of the image (<see cref="UnsupportedReason"/>).</exception>
    /// <exception cref="TiffException">The pixel data is damaged, or exceeds <see cref="TiffReadOptions.MaxPixelBytes"/>.</exception>
    public T[] ReadRawPixels<T>(TiffPixelOrder order = TiffPixelOrder.Interleaved) where T : unmanaged, INumber<T> =>
        ReadArray<T>(Whole(order, TiffPixelScale.Stored), raw: true, CancellationToken.None);

    /// <summary>The samples, converted to <typeparamref name="T"/>, as stored or normalised.</summary>
    /// <typeparam name="T"><see cref="float"/> or <see cref="double"/> always; an integer type when every stored value fits in it
    /// (the same or a wider type of the same signedness, or an unsigned type into a wider signed one).</typeparam>
    /// <param name="scale">The stored values, or values normalised to [0, 1] (<see cref="float"/> and <see cref="double"/> only).</param>
    /// <param name="order">Interleaved (<c>[y][x][c]</c>) or planar (<c>[c][y][x]</c>).</param>
    /// <returns>A new array of <c>Width × Height × Channels</c> samples, rows from the top.</returns>
    /// <exception cref="InvalidOperationException">The conversion could lose values, or the image holds more samples than an array can.</exception>
    /// <exception cref="NotSupportedException">The library does not handle the format of the image (<see cref="UnsupportedReason"/>).</exception>
    /// <exception cref="TiffException">The pixel data is damaged, or exceeds <see cref="TiffReadOptions.MaxPixelBytes"/>.</exception>
    public T[] ReadPixels<T>(TiffPixelScale scale = TiffPixelScale.Stored, TiffPixelOrder order = TiffPixelOrder.Interleaved)
        where T : unmanaged, INumber<T> =>
        ReadArray<T>(Whole(order, scale), raw: false, CancellationToken.None);

    /// <summary>
    /// Reads the samples into a buffer of the caller, on the calling thread, without allocating an array for them; the
    /// <see cref="Memory{T}"/> overload decodes on every core.
    /// </summary>
    /// <typeparam name="T">As for <see cref="ReadPixels{T}(TiffPixelScale, TiffPixelOrder)"/>.</typeparam>
    /// <param name="destination">Exactly <c>Width × Height × Channels</c> samples.</param>
    /// <param name="scale">The stored values, or values normalised to [0, 1].</param>
    /// <param name="order">Interleaved or planar.</param>
    /// <exception cref="ArgumentException"><paramref name="destination"/> is not of the size of the image.</exception>
    /// <exception cref="InvalidOperationException">The conversion could lose values.</exception>
    /// <exception cref="NotSupportedException">The library does not handle the format of the image.</exception>
    /// <exception cref="TiffException">The pixel data is damaged, or the decoding buffers exceed <see cref="TiffReadOptions.MaxPixelBytes"/>.</exception>
    /// <exception cref="ObjectDisposedException">The file is disposed.</exception>
    public void ReadPixels<T>(Span<T> destination, TiffPixelScale scale = TiffPixelScale.Stored, TiffPixelOrder order = TiffPixelOrder.Interleaved)
        where T : unmanaged, INumber<T>
    {
        var request = Whole(order, scale);
        long count = Prepare<T>(request, raw: false);
        if (destination.Length != count)
        {
            throw new ArgumentException($"The image has {count} samples, the destination {destination.Length}.", nameof(destination));
        }

        var budget = Budget(CancellationToken.None);
        Dispatch(PixelPlan.Create(Descriptor, budget, request, SampleConversion.SizeOf(SampleType), parallel: false), destination, null, budget);
    }

    /// <summary>Reads the samples into an array of the caller, decoding on every core, without allocating one.</summary>
    /// <typeparam name="T">As for <see cref="ReadPixels{T}(TiffPixelScale, TiffPixelOrder)"/>.</typeparam>
    /// <param name="destination">Exactly <c>Width × Height × Channels</c> samples.</param>
    /// <param name="scale">The stored values, or values normalised to [0, 1].</param>
    /// <param name="order">Interleaved or planar.</param>
    /// <exception cref="ArgumentNullException"><paramref name="destination"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="destination"/> is not of the size of the image.</exception>
    /// <exception cref="InvalidOperationException">The conversion could lose values.</exception>
    /// <exception cref="NotSupportedException">The library does not handle the format of the image.</exception>
    /// <exception cref="TiffException">The pixel data is damaged, or the decoding buffers exceed <see cref="TiffReadOptions.MaxPixelBytes"/>.</exception>
    /// <exception cref="ObjectDisposedException">The file is disposed.</exception>
    public void ReadPixels<T>(T[] destination, TiffPixelScale scale = TiffPixelScale.Stored, TiffPixelOrder order = TiffPixelOrder.Interleaved)
        where T : unmanaged, INumber<T>
    {
        ArgumentNullException.ThrowIfNull(destination);
        ReadPixels(destination.AsMemory(), scale, order);
    }

    /// <summary>Reads the samples into a buffer of the caller, decoding on every core, without allocating an array for them.</summary>
    /// <typeparam name="T">As for <see cref="ReadPixels{T}(TiffPixelScale, TiffPixelOrder)"/>.</typeparam>
    /// <param name="destination">Exactly <c>Width × Height × Channels</c> samples; not used by the caller until the method returns.</param>
    /// <param name="scale">The stored values, or values normalised to [0, 1].</param>
    /// <param name="order">Interleaved or planar.</param>
    /// <exception cref="ArgumentException"><paramref name="destination"/> is not of the size of the image.</exception>
    /// <exception cref="InvalidOperationException">The conversion could lose values.</exception>
    /// <exception cref="NotSupportedException">The library does not handle the format of the image.</exception>
    /// <exception cref="TiffException">The pixel data is damaged, or the decoding buffers exceed <see cref="TiffReadOptions.MaxPixelBytes"/>.</exception>
    /// <exception cref="ObjectDisposedException">The file is disposed.</exception>
    public void ReadPixels<T>(Memory<T> destination, TiffPixelScale scale = TiffPixelScale.Stored, TiffPixelOrder order = TiffPixelOrder.Interleaved)
        where T : unmanaged, INumber<T>
    {
        var request = Whole(order, scale);
        long count = Prepare<T>(request, raw: false);
        if (destination.Length != count)
        {
            throw new ArgumentException($"The image has {count} samples, the destination {destination.Length}.", nameof(destination));
        }

        ReadInto(request, destination, CancellationToken.None);
    }

    /// <summary>Reads a request (checked by <see cref="Prepare{T}"/>) into a buffer of its size, decoding on every core.</summary>
    internal void ReadInto<T>(PixelRequest request, Memory<T> destination, CancellationToken cancellationToken) where T : unmanaged, INumber<T>
    {
        var budget = Budget(cancellationToken);
        var plan = PixelPlan.Create(Descriptor, budget, request, SampleConversion.SizeOf(SampleType), parallel: true);
        Dispatch(plan, destination.Span, destination, budget);
    }

    /// <summary>The samples of a region of the image (in the delivered orientation), converted as by <see cref="ReadPixels{T}(TiffPixelScale, TiffPixelOrder)"/>.</summary>
    /// <typeparam name="T">As for <see cref="ReadPixels{T}(TiffPixelScale, TiffPixelOrder)"/>.</typeparam>
    /// <param name="x">The first column.</param>
    /// <param name="y">The first row, from the top.</param>
    /// <param name="width">The number of columns.</param>
    /// <param name="height">The number of rows.</param>
    /// <param name="scale">The stored values, or values normalised to [0, 1].</param>
    /// <param name="order">Interleaved or planar.</param>
    /// <returns>A new array of <c>width × height × Channels</c> samples. Only the strips or tiles under the region are read.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The region is empty or not within the image.</exception>
    /// <exception cref="InvalidOperationException">The conversion could lose values.</exception>
    /// <exception cref="NotSupportedException">The library does not handle the format of the image.</exception>
    /// <exception cref="TiffException">The pixel data is damaged.</exception>
    public T[] ReadRegion<T>(int x, int y, int width, int height, TiffPixelScale scale = TiffPixelScale.Stored,
        TiffPixelOrder order = TiffPixelOrder.Interleaved) where T : unmanaged, INumber<T> =>
        ReadArray<T>(Region(x, y, width, height, null, order, scale), raw: false, CancellationToken.None);

    /// <summary>The samples of a region of the image, of exactly their stored type.</summary>
    /// <typeparam name="T">The type of <see cref="SampleType"/>.</typeparam>
    /// <param name="x">The first column.</param>
    /// <param name="y">The first row, from the top.</param>
    /// <param name="width">The number of columns.</param>
    /// <param name="height">The number of rows.</param>
    /// <param name="order">Interleaved or planar.</param>
    /// <returns>A new array of <c>width × height × Channels</c> samples.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The region is empty or not within the image.</exception>
    /// <exception cref="InvalidOperationException"><typeparamref name="T"/> is not the type of the samples.</exception>
    /// <exception cref="NotSupportedException">The library does not handle the format of the image.</exception>
    /// <exception cref="TiffException">The pixel data is damaged.</exception>
    public T[] ReadRawRegion<T>(int x, int y, int width, int height, TiffPixelOrder order = TiffPixelOrder.Interleaved)
        where T : unmanaged, INumber<T> =>
        ReadArray<T>(Region(x, y, width, height, null, order, TiffPixelScale.Stored), raw: true, CancellationToken.None);

    /// <summary>One channel of the image, converted as by <see cref="ReadPixels{T}(TiffPixelScale, TiffPixelOrder)"/>.</summary>
    /// <typeparam name="T">As for <see cref="ReadPixels{T}(TiffPixelScale, TiffPixelOrder)"/>.</typeparam>
    /// <param name="channel">The channel: 0 for grey or red, 1 for green…, then the extra channels.</param>
    /// <param name="scale">The stored values, or values normalised to [0, 1].</param>
    /// <returns>A new array of <c>Width × Height</c> samples. For an image stored in planes, only the plane of the channel is read.</returns>
    /// <exception cref="ArgumentOutOfRangeException">There is no such channel.</exception>
    /// <exception cref="InvalidOperationException">The conversion could lose values.</exception>
    /// <exception cref="NotSupportedException">The library does not handle the format of the image.</exception>
    /// <exception cref="TiffException">The pixel data is damaged.</exception>
    public T[] ReadChannel<T>(int channel, TiffPixelScale scale = TiffPixelScale.Stored) where T : unmanaged, INumber<T>
    {
        ThrowIfUnsupported();
        ArgumentOutOfRangeException.ThrowIfNegative(channel);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(channel, Channels);
        return ReadArray<T>(Region(0, 0, Width, Height, channel, TiffPixelOrder.Interleaved, scale), raw: false, CancellationToken.None);
    }

    /// <summary>Reads the samples on a thread of the pool, as <see cref="ReadPixels{T}(TiffPixelScale, TiffPixelOrder)"/>.</summary>
    /// <typeparam name="T">As for <see cref="ReadPixels{T}(TiffPixelScale, TiffPixelOrder)"/>.</typeparam>
    /// <param name="scale">The stored values, or values normalised to [0, 1].</param>
    /// <param name="order">Interleaved or planar.</param>
    /// <param name="progress">Receives the fraction read, 0 to 1, at most once a percent and in increasing order, from the threads that
    /// decode; 1 once the array is complete. A <see cref="Progress{T}"/> posts its reports: its last may run after the task ends.</param>
    /// <param name="cancellationToken">Stops the read between strips or tiles.</param>
    /// <returns>A new array of <c>Width × Height × Channels</c> samples. Every failure, including a type that cannot be read,
    /// is reported by the task, never thrown by the call.</returns>
    /// <exception cref="InvalidOperationException">The conversion could lose values, or the image holds more samples than an array can.</exception>
    /// <exception cref="NotSupportedException">The library does not handle the format of the image (<see cref="UnsupportedReason"/>).</exception>
    /// <exception cref="TiffException">The pixel data is damaged, or exceeds <see cref="TiffReadOptions.MaxPixelBytes"/>.</exception>
    /// <exception cref="ObjectDisposedException">The file is disposed.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    public Task<T[]> ReadPixelsAsync<T>(TiffPixelScale scale = TiffPixelScale.Stored, TiffPixelOrder order = TiffPixelOrder.Interleaved,
        IProgress<double>? progress = null, CancellationToken cancellationToken = default) where T : unmanaged, INumber<T>
    {
        var request = Whole(order, scale);
        return Task.Run(() => ReadArray<T>(request, raw: false, cancellationToken, progress), cancellationToken);
    }

    /// <summary>
    /// The indices of a palette image, before the colour map expands them to RGB: a classification map or a label image keeps its
    /// classes. Then the extra samples, as stored, for each pixel (interleaved).
    /// </summary>
    /// <returns>A new array of <c>Width × Height × samples per pixel</c> values, each below 2^BitsPerSample.</returns>
    /// <exception cref="InvalidOperationException">The image is not a palette image.</exception>
    /// <exception cref="NotSupportedException">The library does not handle the format of the image.</exception>
    /// <exception cref="TiffException">The pixel data is damaged, or exceeds <see cref="TiffReadOptions.MaxPixelBytes"/>.</exception>
    /// <exception cref="ObjectDisposedException">The file is disposed.</exception>
    public ushort[] ReadIndices()
    {
        ThrowIfUnsupported();
        if (Descriptor.Photometric != TiffPhotometric.Palette || _pixels is not null)
        {
            throw new InvalidOperationException("The image is not a palette image: its samples are its values (ReadRawPixels).");
        }

        var d = Descriptor.AsIndices();
        var budget = Budget(CancellationToken.None);
        var request = new PixelRequest(0, 0, Width, Height, null, TiffPixelOrder.Interleaved, TiffPixelScale.Stored);
        long count = request.Count(d.Channels);
        budget.ReservePixels(count * sizeof(ushort), "the indices");
        ThrowIfLargerThanAnArray(count);
        var plan = PixelPlan.Create(d, budget, request, sizeof(ushort), parallel: true);
        var result = GC.AllocateUninitializedArray<ushort>((int)count);
        var origin = Origin;
        PixelReader.Read<ushort, ushort>(plan, origin.Source, budget, d.Channels, origin.LittleEndian, result, result);
        return result;
    }

    /// <summary>Reads a region on a thread of the pool, as <see cref="ReadRegion{T}"/>.</summary>
    /// <typeparam name="T">As for <see cref="ReadPixels{T}(TiffPixelScale, TiffPixelOrder)"/>.</typeparam>
    /// <param name="x">The first column.</param>
    /// <param name="y">The first row, from the top.</param>
    /// <param name="width">The number of columns.</param>
    /// <param name="height">The number of rows.</param>
    /// <param name="scale">The stored values, or values normalised to [0, 1].</param>
    /// <param name="order">Interleaved or planar.</param>
    /// <param name="progress">Receives the fraction read, 0 to 1, at most once a percent and in increasing order; 1 once the array is complete.</param>
    /// <param name="cancellationToken">Stops the read between strips or tiles.</param>
    /// <returns>A new array of <c>width × height × Channels</c> samples; a region outside the image is thrown, every other failure
    /// is reported by the task.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The region is empty or not within the image.</exception>
    /// <exception cref="InvalidOperationException">The conversion could lose values.</exception>
    /// <exception cref="NotSupportedException">The library does not handle the format of the image.</exception>
    /// <exception cref="TiffException">The pixel data is damaged.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    public Task<T[]> ReadRegionAsync<T>(int x, int y, int width, int height, TiffPixelScale scale = TiffPixelScale.Stored,
        TiffPixelOrder order = TiffPixelOrder.Interleaved, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
        where T : unmanaged, INumber<T>
    {
        var request = Region(x, y, width, height, null, order, scale);
        return Task.Run(() => ReadArray<T>(request, raw: false, cancellationToken, progress), cancellationToken);
    }

    /// <summary>Reads one channel on a thread of the pool, as <see cref="ReadChannel{T}"/>.</summary>
    /// <typeparam name="T">As for <see cref="ReadPixels{T}(TiffPixelScale, TiffPixelOrder)"/>.</typeparam>
    /// <param name="channel">The channel: 0 for grey or red, 1 for green…, then the extra channels.</param>
    /// <param name="scale">The stored values, or values normalised to [0, 1].</param>
    /// <param name="progress">Receives the fraction read, 0 to 1, at most once a percent and in increasing order; 1 once the array is complete.</param>
    /// <param name="cancellationToken">Stops the read between strips or tiles.</param>
    /// <returns>A new array of <c>Width × Height</c> samples; every failure is reported by the task.</returns>
    /// <exception cref="ArgumentOutOfRangeException">There is no such channel.</exception>
    /// <exception cref="InvalidOperationException">The conversion could lose values.</exception>
    /// <exception cref="NotSupportedException">The library does not handle the format of the image.</exception>
    /// <exception cref="TiffException">The pixel data is damaged.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    public Task<T[]> ReadChannelAsync<T>(int channel, TiffPixelScale scale = TiffPixelScale.Stored, IProgress<double>? progress = null,
        CancellationToken cancellationToken = default) where T : unmanaged, INumber<T>
    {
        ArgumentOutOfRangeException.ThrowIfNegative(channel);
        return Task.Run(() =>
        {
            ThrowIfUnsupported();
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(channel, Channels);
            return ReadArray<T>(Region(0, 0, Width, Height, channel, TiffPixelOrder.Interleaved, scale), raw: false, cancellationToken, progress);
        }, cancellationToken);
    }

    private PixelRequest Whole(TiffPixelOrder order, TiffPixelScale scale) => new(0, 0, Width, Height, null, order, scale);

    private PixelRequest Region(int x, int y, int width, int height, int? channel, TiffPixelOrder order, TiffPixelScale scale)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(x);
        ArgumentOutOfRangeException.ThrowIfNegative(y);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        if ((long)x + width > Width)
        {
            throw new ArgumentOutOfRangeException(nameof(width), $"The region {x}, {y}, {width} x {height} is not within the image of {Width} x {Height}.");
        }

        if ((long)y + height > Height)
        {
            throw new ArgumentOutOfRangeException(nameof(height), $"The region {x}, {y}, {width} x {height} is not within the image of {Width} x {Height}.");
        }

        return new PixelRequest(x, y, width, height, channel, order, scale);
    }

    private T[] ReadArray<T>(PixelRequest request, bool raw, CancellationToken cancellationToken, IProgress<double>? progress = null)
        where T : unmanaged, INumber<T>
    {
        long count = Prepare<T>(request, raw);
        var budget = Budget(cancellationToken);

        // Everything is checked before the destination is allocated: its size (the limit of the application first, then what an
        // array can hold), the blocks under it, the buffers of the workers.
        budget.ReservePixels(count > long.MaxValue / Unsafe.SizeOf<T>() ? long.MaxValue : count * Unsafe.SizeOf<T>(), "the pixels");
        ThrowIfLargerThanAnArray(count);
        var plan = PixelPlan.Create(Descriptor, budget, request, SampleConversion.SizeOf(SampleType), parallel: true);
        plan.Progress = progress is null ? null : ProgressCounter.Of(progress, plan.Units().LongCount());

        // Not cleared: every sample of a request is written by the read (EverySampleWrittenTests checks it on every image of the
        // corpora), and a read that fails returns no array.
        var result = GC.AllocateUninitializedArray<T>((int)count);
        Dispatch(plan, result, result, budget);
        plan.Progress?.Done();
        return result;
    }

    /// <summary>Checks what can be checked before reading: the image, the type, the size; returns the number of samples.</summary>
    private long Prepare<T>(PixelRequest request, bool raw) where T : unmanaged, INumber<T>
    {
        ThrowIfUnsupported();

        SampleConversion.Check<T>(SampleType, request.Scale, raw);
        return request.Count(Channels);
    }

    private static void ThrowIfLargerThanAnArray(long count)
    {
        if (count > Array.MaxLength)
        {
            throw new InvalidOperationException(
                $"The read covers {(count == long.MaxValue ? "more than 2^63" : count.ToString(CultureInfo.InvariantCulture))} samples, "
                + "more than an array can hold: read the image by region (ReadRegion).");
        }
    }

    private void ThrowIfUnsupported()
    {
        if (UnsupportedReason is not null)
        {
            throw Unsupported();
        }
    }

    /// <summary>The limits of a read: those of the file, or the defaults for pixels the application gave (they are trusted).</summary>
    private ResourceBudget Budget(CancellationToken cancellationToken)
    {
        var origin = Origin;
        return new(origin.Options, origin.Name, cancellationToken);
    }

    /// <summary>
    /// Checks, before anything is allocated to write the image, that it can be read whole: an image of a file must hold the pixels
    /// it declares (a small file declaring a huge image, strips sharing their data, a decompression bomb), and the pixels a save
    /// decodes count in <see cref="TiffReadOptions.MaxPixelBytes"/> as a read of the whole image does, although they are decoded
    /// band after band.
    /// </summary>
    internal void ValidateForWrite()
    {
        ThrowIfUnsupported();
        if (_pixels is not null)
        {
            return;
        }

        int size = SampleConversion.SizeOf(SampleType);
        var budget = Budget(CancellationToken.None);
        Int128 bytes = (Int128)Width * Height * Channels * size;
        try
        {
            budget.ReservePixels(bytes > long.MaxValue ? long.MaxValue : (long)bytes, "saving the image (its pixels are decoded)");
        }
        catch (TiffException ex) when (ex.Limit is { } limit)
        {
            throw new TiffException($"{ex.Message} Open the file with a larger MaxPixelBytes to save it, or copy it by region into a TiffImageWriter.", limit);
        }
        var request = new PixelRequest(0, 0, Width, Height, null, TiffPixelOrder.Interleaved, TiffPixelScale.Stored);
        _ = PixelPlan.Create(Descriptor, budget, request, size, parallel: false);
    }

    /// <summary>Checks that rows of the image can be read (support, blocks, buffers) before the band that holds them is allocated.</summary>
    internal void ValidateBand(int y, int rows, int degree = 1)
    {
        ThrowIfUnsupported();
        var request = new PixelRequest(0, y, Width, rows, null, TiffPixelOrder.Interleaved, TiffPixelScale.Stored);
        var budget = Budget(CancellationToken.None);
        budget.ReservePixels((long)rows * Width * Channels * SampleConversion.SizeOf(SampleType), "the rows read to be written");
        bool parallel = degree > 1 && Descriptor.Compression != TiffCompression.None;
        _ = PixelPlan.Create(Descriptor, budget, request, SampleConversion.SizeOf(SampleType), parallel, maxWorkers: degree);
    }

    /// <summary>
    /// The samples of an image of the application, interleaved, in the order of the machine: what the writer encodes, without
    /// reading them into bands. Null for an image of a file, or held planar.
    /// </summary>
    /// <remarks>Null is given as such: a <c>null</c> in the conditional would become an empty memory (its conversion from an array).</remarks>
    internal ReadOnlyMemory<byte>? InterleavedPixels => _pixels is { Memory: { } memory } && !Descriptor.Planar ? memory : default(ReadOnlyMemory<byte>?);

    /// <summary>
    /// Reads the raw samples of whole rows, interleaved, into bytes in the order of the machine: what the writer encodes. In
    /// parallel when <paramref name="degree"/> allows, within the limits of the file.
    /// </summary>
    internal void ReadBand(int y, int rows, Memory<byte> destination, int degree, ScratchCache cache, CancellationToken cancellationToken)
    {
        ThrowIfUnsupported();
        if (_pixels is not null && !Descriptor.Planar)
        {
            // Pixels of the application, interleaved, in the order of the machine: the rows are a copy, nothing to decode.
            long rowBytes = (long)Width * Channels * SampleConversion.SizeOf(SampleType);
            _pixels.ReadExactly(y * rowBytes, destination.Span);
            return;
        }

        var request = new PixelRequest(0, y, Width, rows, null, TiffPixelOrder.Interleaved, TiffPixelScale.Stored);
        var budget = Budget(cancellationToken);

        // Several threads pay for decoding; rows stored as they are only need to be copied.
        bool parallel = degree > 1 && Descriptor.Compression != TiffCompression.None;
        var plan = PixelPlan.Create(Descriptor, budget, request, SampleConversion.SizeOf(SampleType), parallel, maxWorkers: degree);
        var visitor = new BandVisitor(this, plan, destination, budget, cache);
        SampleTypes.Visit(SampleType, ref visitor);
    }

    /// <summary>Reads into the bytes of a band, seen as samples of their type (the workers of a parallel read share it as memory).</summary>
    private void ReadBandAs<T>(PixelPlan plan, Memory<byte> destination, ResourceBudget budget, ScratchCache cache) where T : unmanaged, INumber<T>
    {
        var samples = new CastMemory<T>(destination).Memory;
        Dispatch(plan, samples.Span, samples, budget, cache);
    }

    private void Dispatch<T>(PixelPlan plan, Span<T> destination, Memory<T>? array, ResourceBudget budget, ScratchCache? cache = null)
        where T : unmanaged, INumber<T>
    {
        var origin = Origin;
        var visitor = new ReadVisitor<T>(plan, origin.Source, budget, Channels, origin.LittleEndian, destination, array, cache);
        SampleTypes.Visit(SampleType, ref visitor);
    }

    /// <summary>Reads the rows of a band as the type of the samples (a writer reads its source band after band).</summary>
    private readonly struct BandVisitor(TiffImage image, PixelPlan plan, Memory<byte> destination, ResourceBudget budget, ScratchCache cache)
        : ISampleVisitor
    {
        public void Visit<TD>() where TD : unmanaged, INumber<TD> => image.ReadBandAs<TD>(plan, destination, budget, cache);
    }

    /// <summary>Reads a plan into a destination of <typeparamref name="T"/>, the stored samples being of the visited type.</summary>
    private readonly ref struct ReadVisitor<T>(PixelPlan plan, TiffSource source, ResourceBudget budget, int channels, bool little,
        Span<T> destination, Memory<T>? array, ScratchCache? cache) : ISampleVisitor
        where T : unmanaged, INumber<T>
    {
        private readonly Span<T> _destination = destination;

        public void Visit<TD>() where TD : unmanaged, INumber<TD> =>
            PixelReader.Read<TD, T>(plan, source, budget, channels, little, _destination, array, cache);
    }
}
