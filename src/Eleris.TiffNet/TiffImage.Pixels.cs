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

    /// <summary>Reads the samples into a buffer of the caller, on the calling thread, without allocating an array for them.</summary>
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
    /// <param name="cancellationToken">Stops the read between strips or tiles.</param>
    /// <returns>A new array of <c>Width × Height × Channels</c> samples. Every failure, including a type that cannot be read,
    /// is reported by the task, never thrown by the call.</returns>
    /// <exception cref="InvalidOperationException">The conversion could lose values, or the image holds more samples than an array can.</exception>
    /// <exception cref="NotSupportedException">The library does not handle the format of the image (<see cref="UnsupportedReason"/>).</exception>
    /// <exception cref="TiffException">The pixel data is damaged, or exceeds <see cref="TiffReadOptions.MaxPixelBytes"/>.</exception>
    /// <exception cref="ObjectDisposedException">The file is disposed.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    public Task<T[]> ReadPixelsAsync<T>(TiffPixelScale scale = TiffPixelScale.Stored, TiffPixelOrder order = TiffPixelOrder.Interleaved,
        CancellationToken cancellationToken = default) where T : unmanaged, INumber<T>
    {
        var request = Whole(order, scale);
        return Task.Run(() => ReadArray<T>(request, raw: false, cancellationToken), cancellationToken);
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

    private T[] ReadArray<T>(PixelRequest request, bool raw, CancellationToken cancellationToken) where T : unmanaged, INumber<T>
    {
        long count = Prepare<T>(request, raw);
        var budget = Budget(cancellationToken);

        // Everything is checked before the destination is allocated: its size (the limit of the application first, then what an
        // array can hold), the blocks under it, the buffers of the workers.
        budget.ReservePixels(count > long.MaxValue / Unsafe.SizeOf<T>() ? long.MaxValue : count * Unsafe.SizeOf<T>(), "the pixels");
        ThrowIfLargerThanAnArray(count);
        var plan = PixelPlan.Create(Descriptor, budget, request, SampleConversion.SizeOf(SampleType), parallel: true);
        var result = new T[count];
        Dispatch(plan, result, result, budget);
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
    private ResourceBudget Budget(CancellationToken cancellationToken) => _pixels is not null
        ? new(TiffReadOptions.Default, "image", cancellationToken)
        : new(File!.Options, File.Source.Name, cancellationToken);

    /// <summary>
    /// Reads the raw samples of a region (one channel, or all of them interleaved) into bytes, in the order of the machine: what
    /// the writer encodes. On the calling thread, within the limits of the file.
    /// </summary>
    internal void ReadBlock(int x, int y, int width, int height, int? channel, Span<byte> destination)
    {
        ThrowIfUnsupported();
        var request = new PixelRequest(x, y, width, height, channel, TiffPixelOrder.Interleaved, TiffPixelScale.Stored);
        var budget = Budget(CancellationToken.None);
        var plan = PixelPlan.Create(Descriptor, budget, request, SampleConversion.SizeOf(SampleType), parallel: false);
        switch (SampleType)
        {
            case TiffSampleType.UInt8: Dispatch(plan, destination, null, budget); break;
            case TiffSampleType.Int8: Dispatch(plan, MemoryMarshal.Cast<byte, sbyte>(destination), null, budget); break;
            case TiffSampleType.UInt16: Dispatch(plan, MemoryMarshal.Cast<byte, ushort>(destination), null, budget); break;
            case TiffSampleType.Int16: Dispatch(plan, MemoryMarshal.Cast<byte, short>(destination), null, budget); break;
            case TiffSampleType.UInt32: Dispatch(plan, MemoryMarshal.Cast<byte, uint>(destination), null, budget); break;
            case TiffSampleType.Int32: Dispatch(plan, MemoryMarshal.Cast<byte, int>(destination), null, budget); break;
            case TiffSampleType.UInt64: Dispatch(plan, MemoryMarshal.Cast<byte, ulong>(destination), null, budget); break;
            case TiffSampleType.Int64: Dispatch(plan, MemoryMarshal.Cast<byte, long>(destination), null, budget); break;
            case TiffSampleType.Float16: Dispatch(plan, MemoryMarshal.Cast<byte, Half>(destination), null, budget); break;
            case TiffSampleType.Float32: Dispatch(plan, MemoryMarshal.Cast<byte, float>(destination), null, budget); break;
            default: Dispatch(plan, MemoryMarshal.Cast<byte, double>(destination), null, budget); break;
        }
    }

    private void Dispatch<T>(PixelPlan plan, Span<T> destination, T[]? array, ResourceBudget budget) where T : unmanaged, INumber<T>
    {
        var source = _pixels ?? File!.Source;
        bool little = _pixels is not null ? BitConverter.IsLittleEndian : !Storage!.BigEndian;
        int c = Channels;
        switch (SampleType)
        {
            case TiffSampleType.UInt8: PixelReader.Read<byte, T>(plan, source, budget, c, little, destination, array); break;
            case TiffSampleType.Int8: PixelReader.Read<sbyte, T>(plan, source, budget, c, little, destination, array); break;
            case TiffSampleType.UInt16: PixelReader.Read<ushort, T>(plan, source, budget, c, little, destination, array); break;
            case TiffSampleType.Int16: PixelReader.Read<short, T>(plan, source, budget, c, little, destination, array); break;
            case TiffSampleType.UInt32: PixelReader.Read<uint, T>(plan, source, budget, c, little, destination, array); break;
            case TiffSampleType.Int32: PixelReader.Read<int, T>(plan, source, budget, c, little, destination, array); break;
            case TiffSampleType.UInt64: PixelReader.Read<ulong, T>(plan, source, budget, c, little, destination, array); break;
            case TiffSampleType.Int64: PixelReader.Read<long, T>(plan, source, budget, c, little, destination, array); break;
            case TiffSampleType.Float16: PixelReader.Read<Half, T>(plan, source, budget, c, little, destination, array); break;
            case TiffSampleType.Float32: PixelReader.Read<float, T>(plan, source, budget, c, little, destination, array); break;
            default: PixelReader.Read<double, T>(plan, source, budget, c, little, destination, array); break;
        }
    }
}
