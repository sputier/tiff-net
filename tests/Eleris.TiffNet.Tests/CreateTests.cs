using Eleris.TiffNet.Tests.Fixtures;

namespace Eleris.TiffNet.Tests;

public class CreateTests
{
    [Fact]
    public void A_created_image_gives_back_its_pixels_through_every_read()
    {
        ushort[] pixels = [.. Enumerable.Range(0, 5 * 4 * 3).Select(i => (ushort)(i * 1000))];

        var image = TiffImage.Create<ushort>(pixels, 5, 4, channels: 3, TiffColorModel.Rgb);

        Assert.Equal((5, 4, 3, TiffSampleType.UInt16, TiffColorModel.Rgb), (image.Width, image.Height, image.Channels, image.SampleType, image.ColorModel));
        Assert.Null(image.Storage);
        Assert.Empty(image.ExtraChannels);
        Assert.Equal(pixels, image.ReadRawPixels<ushort>());
        Assert.Equal(pixels.Select(v => v / 65535.0), image.ReadPixels<double>(TiffPixelScale.Normalized));
        Assert.Equal(pixels.Where((_, i) => i % 3 == 1), image.ReadChannel<ushort>(1));
        Assert.Equal(new ushort[] { pixels[(1 * 5 + 2) * 3], pixels[(1 * 5 + 2) * 3 + 1], pixels[(1 * 5 + 2) * 3 + 2] },
            image.ReadRawRegion<ushort>(2, 1, 1, 1));
    }

    [Theory]
    [InlineData(TiffPixelOrder.Interleaved)]
    [InlineData(TiffPixelOrder.Planar)]
    public void Planar_and_interleaved_input_give_the_same_image(TiffPixelOrder order)
    {
        float[] interleaved = [.. Enumerable.Range(0, 3 * 2 * 4).Select(i => i * 0.5f)];
        float[] planar = [.. Enumerable.Range(0, 4).SelectMany(c => Enumerable.Range(0, 6).Select(p => interleaved[p * 4 + c]))];

        var image = TiffImage.Create<float>(order == TiffPixelOrder.Planar ? planar : interleaved, 3, 2, channels: 4, TiffColorModel.Grey, order);

        Assert.Equal(interleaved, image.ReadRawPixels<float>());
        Assert.Equal(planar, image.ReadRawPixels<float>(TiffPixelOrder.Planar));
        Assert.Equal(3, image.ExtraChannels.Count);
    }

    [Fact]
    public void Three_channels_or_more_need_a_colour_model()
    {
        // An L / H-alpha / OIII stack must not become RGB by accident.
        var stack = new float[2 * 2 * 3];

        Assert.Contains("colour model", Assert.Throws<ArgumentException>(() => TiffImage.Create<float>(stack, 2, 2, channels: 3)).Message,
            StringComparison.Ordinal);
        var grey = TiffImage.Create<float>(stack, 2, 2, channels: 3, TiffColorModel.Grey);
        Assert.Equal((TiffColorModel.Grey, 2), (grey.ColorModel, grey.ExtraChannels.Count));
        Assert.Equal(TiffColorModel.Grey, TiffImage.Create<float>(new float[8], 2, 2, channels: 2).ColorModel);
        Assert.Throws<ArgumentException>(() => TiffImage.Create<float>(new float[8], 2, 2, channels: 2, TiffColorModel.Rgb));
    }

    [Fact]
    public void Arguments_are_checked()
    {
        Assert.Throws<ArgumentException>(() => TiffImage.Create<byte>(new byte[5], 2, 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => TiffImage.Create<byte>([], 0, 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => TiffImage.Create<byte>(new byte[4], 2, 2, order: (TiffPixelOrder)7));
        Assert.Throws<ArgumentException>(() => TiffImage.Create<Int128>(new Int128[4], 2, 2));   // not a sample type of TIFF
    }

    [Fact]
    public void Every_sample_type_is_created_and_read_back()
    {
        static void RoundTrip<T>(T[] values) where T : unmanaged, System.Numerics.INumber<T> =>
            Assert.Equal(values, TiffImage.Create<T>(values, values.Length, 1).ReadRawPixels<T>());

        RoundTrip<byte>([0, 255]);
        RoundTrip<sbyte>([-128, 127]);
        RoundTrip<short>([short.MinValue, short.MaxValue]);
        RoundTrip<uint>([0, uint.MaxValue]);
        RoundTrip<int>([int.MinValue, int.MaxValue]);
        RoundTrip<ulong>([0, ulong.MaxValue]);
        RoundTrip<long>([long.MinValue, long.MaxValue]);
        RoundTrip<Half>([Half.NegativeZero, Half.MaxValue]);
        RoundTrip<double>([double.NaN, double.NegativeInfinity, -0.0]);
    }

    [Fact]
    public void SetPixels_replaces_the_pixels_of_a_read_image_and_keeps_its_metadata()
    {
        using var file = TiffFile.Open(DomainFixtures.PathOf("siril_m42_grey16_astro.tif"));
        var image = file.Primary;
        string? description = image.Metadata.Description;

        float[] pixels = [1f, 2f, 3f, 4f, 5f, 6f];
        image.SetPixels<float>(pixels, 3, 2);

        Assert.Equal((3, 2, TiffSampleType.Float32), (image.Width, image.Height, image.SampleType));
        Assert.Equal(pixels, image.ReadRawPixels<float>());
        Assert.Equal(description, image.Metadata.Description);
        Assert.NotNull(image.Storage);   // how it was stored in its file
    }

    [Fact]
    public void The_kinds_of_the_extra_channels_can_be_set()
    {
        var image = TiffImage.Create<byte>(new byte[2 * 5], 2, 1, channels: 5, TiffColorModel.Rgb);

        image.SetExtraChannels(TiffExtraChannelKind.UnassociatedAlpha, TiffExtraChannelKind.Data);

        Assert.Equal([TiffExtraChannelKind.UnassociatedAlpha, TiffExtraChannelKind.Data], image.ExtraChannels.Select(e => e.Kind));
        Assert.Throws<ArgumentException>(() => image.SetExtraChannels(TiffExtraChannelKind.Data));
        Assert.Throws<ArgumentException>(() => image.SetExtraChannels(TiffExtraChannelKind.Data, (TiffExtraChannelKind)9));
    }
}

/// <summary>Allocation measured alone: the measurement counts every thread.</summary>
[Collection(nameof(Measurement))]
public class CreateMemoryTests
{
    [Fact]
    public void The_pixels_are_copied_once_and_the_image_does_not_follow_the_buffer()
    {
        byte[] pixels = new byte[1000 * 1000];
        pixels[0] = 7;

        GC.Collect();
        long before = GC.GetTotalAllocatedBytes(precise: true);
        var image = TiffImage.Create<byte>(pixels, 1000, 1000);
        long allocated = GC.GetTotalAllocatedBytes(precise: true) - before;
        pixels[0] = 9;

        Assert.InRange(allocated, 1_000_000, 1_100_000);   // one copy, and the image itself
        Assert.Equal(7, image.ReadRawRegion<byte>(0, 0, 1, 1)[0]);
    }
}
