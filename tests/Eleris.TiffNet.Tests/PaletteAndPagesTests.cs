namespace Eleris.TiffNet.Tests;

/// <summary>The indices of palette images, and saves that leave out the pages the library cannot read.</summary>
public class PaletteAndPagesTests
{
    [Theory]
    [InlineData(8)]
    [InlineData(4)]
    public void The_indices_of_a_palette_image_are_its_classes(int bits)
    {
        // 5 x 2 pixels of classes 0 to 9, and a colour map that gives class k the red k × 1000.
        byte[] classes = [0, 1, 2, 3, 4, 5, 6, 7, 8, 9];
        int rowBytes = (5 * bits + 7) / 8;
        byte[] data = new byte[rowBytes * 2];
        for (int i = 0; i < classes.Length; i++)
        {
            int x = i % 5, y = i / 5, bit = x * bits;
            data[y * rowBytes + bit / 8] |= (byte)(classes[i] << (8 - bits - bit % 8));
        }

        int entries = 1 << bits;
        long[] map = new long[3 * entries];
        for (int k = 0; k < entries; k++)
        {
            map[k] = k * 1000;
        }

        var builder = new TiffBuilder { Data = data };
        builder.AddDirectory().Image(5, 2, bits: (ushort)bits, photometric: 3).Remove(279).Add(279, TiffTagType.Long, data.Length)
            .Add(320, TiffTagType.Short, map).Sorted();
        using var file = TiffFile.Read(new MemoryStream(builder.Build()));

        Assert.Equal(classes.Select(c => (ushort)c), file.Primary.ReadIndices());
        Assert.Equal(classes.Select(c => (ushort)(c * 1000)), file.Primary.ReadRawPixels<ushort>().Where((_, i) => i % 3 == 0));
    }

    [Fact]
    public void An_image_without_a_palette_has_no_indices()
    {
        Assert.Throws<InvalidOperationException>(() => TiffImage.Create<byte>(new byte[4], 2, 2).ReadIndices());
    }

    [Fact]
    public void A_save_can_leave_out_the_pages_the_library_cannot_read()
    {
        // A grey page, then a CMYK one (not read by the library).
        var builder = new TiffBuilder { Data = new byte[16 + 64] };
        builder.AddDirectory().Image(4, 4).Sorted();
        builder.AddDirectory().Image(4, 4, samples: 4, photometric: 5).Remove(273).Add(273, TiffTagType.Long, builder.DataOffset + 16).Sorted();
        using var file = TiffFile.Read(new MemoryStream(builder.Build()));
        Assert.NotNull(file[1].UnsupportedReason);

        Assert.Throws<NotSupportedException>(() => file.Save(Stream.Null));
        using var output = new MemoryStream();
        file.Save(output, TiffWriteOptions.Default with { SkipUnsupportedPages = true });

        using var back = TiffFile.Read(new MemoryStream(output.ToArray()));
        Assert.Single(back);
        Assert.Equal(new byte[16], back.Primary.ReadRawPixels<byte>());
    }
}
