using Eleris.TiffNet.Internal.Writing;
using Eleris.TiffNet.Tests.Fixtures;

namespace Eleris.TiffNet.Tests.Writing;

public class WriteOptionsTests
{
    [Fact]
    public void The_defaults_are_Deflate_with_a_predictor_in_strips()
    {
        var d = TiffWriteOptions.Default;

        Assert.Equal((TiffCompression.Deflate, TiffCompressionLevel.Optimal, TiffPredictor.Auto, TiffLayout.Strips, 256, TiffPixelOrder.Interleaved,
            TiffBigTiff.Auto, -1), (d.Compression, d.Level, d.Predictor, d.Layout, d.TileSize, d.PlanarConfiguration, d.BigTiff, d.MaxDegreeOfParallelism));
    }

    [Theory]
    [InlineData(TiffSampleType.UInt8, TiffPredictor.Horizontal)]
    [InlineData(TiffSampleType.Int32, TiffPredictor.Horizontal)]
    [InlineData(TiffSampleType.UInt64, TiffPredictor.None)]
    [InlineData(TiffSampleType.Int64, TiffPredictor.None)]
    [InlineData(TiffSampleType.Float16, TiffPredictor.FloatingPoint)]
    [InlineData(TiffSampleType.Float64, TiffPredictor.FloatingPoint)]
    public void Auto_chooses_the_predictor_that_suits_the_samples(TiffSampleType type, TiffPredictor expected)
    {
        Assert.Equal(expected, ImageEncoder.ChoosePredictor(TiffPredictor.Auto, TiffCompression.Deflate, type));
        Assert.Equal(expected, ImageEncoder.ChoosePredictor(TiffPredictor.Auto, TiffCompression.Lzw, type));
        Assert.Equal(TiffPredictor.None, ImageEncoder.ChoosePredictor(TiffPredictor.Auto, TiffCompression.PackBits, type));
        Assert.Equal(TiffPredictor.None, ImageEncoder.ChoosePredictor(TiffPredictor.Horizontal, TiffCompression.None, type));
    }

    [Fact]
    public void The_floating_point_predictor_is_refused_for_integers() =>
        Assert.Throws<ArgumentException>(() => ImageEncoder.ChoosePredictor(TiffPredictor.FloatingPoint, TiffCompression.Deflate, TiffSampleType.UInt16));

    [Fact]
    public void Invalid_values_and_JPEG_are_refused()
    {
        Assert.Throws<NotSupportedException>(() => TiffWriteOptions.Default with { Compression = TiffCompression.Jpeg });
        Assert.Throws<ArgumentOutOfRangeException>(() => TiffWriteOptions.Default with { Compression = (TiffCompression)50000 });
        Assert.Throws<ArgumentOutOfRangeException>(() => TiffWriteOptions.Default with { TileSize = 100 });
        Assert.Throws<ArgumentOutOfRangeException>(() => TiffWriteOptions.Default with { TileSize = 0 });
        Assert.Throws<ArgumentOutOfRangeException>(() => TiffWriteOptions.Default with { MaxDegreeOfParallelism = 0 });
        Assert.Throws<ArgumentOutOfRangeException>(() => TiffWriteOptions.Default with { Level = (TiffCompressionLevel)7 });
        Assert.Throws<ArgumentOutOfRangeException>(() => TiffWriteOptions.Default with { Layout = (TiffLayout)3 });
        Assert.Throws<ArgumentOutOfRangeException>(() => TiffWriteOptions.Default with { BigTiff = (TiffBigTiff)9 });
    }

    [Theory]
    [InlineData("siril_m42_grey16_deflate.tif")]
    [InlineData("pillow_rgb8_jpeg.tif")]
    [InlineData("multipage.tif")]
    public void From_writes_an_image_as_its_file_stored_it(string name)
    {
        using var file = TiffFile.Open(DomainFixtures.PathOf(name));
        var storage = file.Primary.Storage!;

        var options = TiffWriteOptions.From(storage);

        var expected = storage.Compression == TiffCompression.Jpeg ? TiffCompression.Deflate : storage.Compression;
        Assert.Equal((expected, storage.Layout, storage.PlanarConfiguration), (options.Compression, options.Layout, options.PlanarConfiguration));
        using var stream = new MemoryStream();
        new TiffFile().Add(file.Primary).Save(stream, options);
        using var back = TiffFile.Read(new MemoryStream(stream.ToArray()));
        Assert.Equal(expected, back.Primary.Storage!.Compression);
    }

    [Fact]
    public void From_keeps_square_tiles_and_BigTIFF()
    {
        var storage = new TiffStorage
        {
            Compression = TiffCompression.Lzw, Predictor = TiffPredictor.Horizontal, Layout = TiffLayout.Tiles, TileWidth = 128, TileHeight = 128,
            RowsPerStrip = 0, PlanarConfiguration = TiffPixelOrder.Planar, BigEndian = true, BigTiff = true, Orientation = 1,
            Photometric = TiffPhotometric.Rgb, BitsPerSample = 16,
        };

        var options = TiffWriteOptions.From(storage);

        Assert.Equal((TiffCompression.Lzw, TiffPredictor.Horizontal, TiffLayout.Tiles, 128, TiffPixelOrder.Planar, TiffBigTiff.Always),
            (options.Compression, options.Predictor, options.Layout, options.TileSize, options.PlanarConfiguration, options.BigTiff));
        Assert.Equal(256, TiffWriteOptions.From(storage with { TileWidth = 100, TileHeight = 100 }).TileSize);
        Assert.Throws<ArgumentNullException>(() => TiffWriteOptions.From(null!));
    }
}
