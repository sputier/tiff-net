namespace Eleris.TiffNet.Tests;

/// <summary>Colour models and extra channels, built byte by byte.</summary>
public class ChannelTests
{
    private static TiffImage Open(TiffBuilder builder) => TiffFile.Read(new MemoryStream(builder.Build())).Primary;

    [Fact]
    public void Extra_channels_come_after_the_colours_with_their_kinds_and_unchanged()
    {
        // RGB, then associated alpha and a data channel: delivered as stored (alpha is not divided out).
        var builder = new TiffBuilder { Data = [10, 20, 30, 128, 7, 1, 2, 3, 0, 9] };
        builder.AddDirectory().Image(2, 1, samples: 5, photometric: 2).Add(338, TiffTagType.Short, 1, 0);
        var image = Open(builder);

        Assert.Equal((TiffColorModel.Rgb, 5), (image.ColorModel, image.Channels));
        Assert.Equal([TiffExtraChannelKind.AssociatedAlpha, TiffExtraChannelKind.Data], image.ExtraChannels.Select(e => e.Kind));
        Assert.Equal(new byte[] { 10, 20, 30, 128, 7, 1, 2, 3, 0, 9 }, image.ReadRawPixels<byte>());
        Assert.Equal(new byte[] { 128, 0 }, image.ReadChannel<byte>(3));
    }

    [Fact]
    public void Grey_with_three_samples_stays_grey_with_two_extra_channels()
    {
        var builder = new TiffBuilder { Data = [1, 2, 3, 4, 5, 6] };
        builder.AddDirectory().Image(2, 1, samples: 3, photometric: 1);
        var image = Open(builder);

        Assert.Equal((TiffColorModel.Grey, 3), (image.ColorModel, image.Channels));
        Assert.Equal(2, image.ExtraChannels.Count);
        Assert.Equal(new byte[] { 1, 4, 2, 5, 3, 6 }, image.ReadRawPixels<byte>(TiffPixelOrder.Planar));
    }
}
