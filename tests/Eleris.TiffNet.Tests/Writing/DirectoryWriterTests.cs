using System.Buffers.Binary;
using Eleris.TiffNet.Internal.Writing;

namespace Eleris.TiffNet.Tests.Writing;

/// <summary>The files the writer makes: structure, sample types, BigTIFF, pages.</summary>
public class DirectoryWriterTests
{
    private static byte[] Write(TiffFile file, TiffWriteOptions? options = null)
    {
        using var stream = new MemoryStream();
        TiffFileWriter.Write([.. file], stream, options ?? TiffWriteOptions.Default, CancellationToken.None);
        return stream.ToArray();
    }

    private static TiffFile Reload(byte[] bytes) => TiffFile.Read(new MemoryStream(bytes));

    internal static TiffImage Sample<T>(int width, int height, int channels, int seed) where T : unmanaged, System.Numerics.INumber<T>
    {
        var random = new Random(seed);
        var values = new T[width * height * channels];
        for (int i = 0; i < values.Length; i++)
        {
            values[i] = typeof(T) == typeof(float) || typeof(T) == typeof(double) || typeof(T) == typeof(Half)
                ? T.CreateTruncating(random.NextDouble() * 1000 - 500)
                : T.CreateTruncating(random.NextInt64());
        }

        return TiffImage.Create<T>(values, width, height, channels, channels >= 3 ? TiffColorModel.Rgb : null);
    }

    public static TheoryData<TiffCompression, TiffPredictor, TiffLayout, TiffPixelOrder> Options()
    {
        var data = new TheoryData<TiffCompression, TiffPredictor, TiffLayout, TiffPixelOrder>();
        foreach (var compression in new[] { TiffCompression.None, TiffCompression.PackBits, TiffCompression.Lzw, TiffCompression.Deflate })
        {
            foreach (var predictor in new[] { TiffPredictor.Auto, TiffPredictor.None })
            {
                data.Add(compression, predictor, TiffLayout.Strips, TiffPixelOrder.Interleaved);
            }
        }

        return data;
    }

    [Fact]
    public void A_file_is_classic_little_endian_with_sorted_tags_and_even_offsets()
    {
        byte[] bytes = Write(new TiffFile().Add(Sample<ushort>(20, 10, 3, 1)));

        Assert.Equal("II*\0"u8.ToArray(), bytes[..4]);
        uint ifd = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(4));
        Assert.Equal(0u, ifd % 2);
        int count = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan((int)ifd));
        var codes = Enumerable.Range(0, count).Select(i => BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan((int)ifd + 2 + 12 * i))).ToList();
        Assert.Equal(codes.Order(), codes);
        Assert.Contains((ushort)274, codes);   // Orientation = 1, as the pixels are delivered
        using var back = Reload(bytes);
        Assert.False(back.Primary.Storage!.BigTiff);
    }

    [Fact]
    public void Pages_are_chained_in_order()
    {
        var file = new TiffFile().Add(Sample<byte>(10, 10, 1, 1)).Add(Sample<ushort>(7, 3, 3, 2)).Add(Sample<float>(5, 5, 1, 3));

        using var back = Reload(Write(file));

        Assert.Equal(3, back.Count);
        Assert.Equal([TiffSampleType.UInt8, TiffSampleType.UInt16, TiffSampleType.Float32], back.Select(i => i.SampleType));
        Assert.Equal(file[1].ReadRawPixels<ushort>(), back[1].ReadRawPixels<ushort>());
    }

    [Theory]
    [MemberData(nameof(Options))]
    public void Every_sample_type_is_read_back_as_written(TiffCompression compression, TiffPredictor predictor, TiffLayout layout, TiffPixelOrder order)
    {
        var options = TiffWriteOptions.Default with { Compression = compression, Predictor = predictor, Layout = layout, PlanarConfiguration = order };
        void Check<T>(int channels) where T : unmanaged, System.Numerics.INumber<T>
        {
            var image = Sample<T>(53, 37, channels, channels * 31 + typeof(T).Name.Length);
            var file = new TiffFile().Add(image);
            using var back = Reload(Write(file, options));
            Assert.Equal(image.ReadRawPixels<T>(), back.Primary.ReadRawPixels<T>());
            Assert.Equal((image.Channels, image.ColorModel, image.SampleType), (back.Primary.Channels, back.Primary.ColorModel, back.Primary.SampleType));
        }

        Check<byte>(1);
        Check<sbyte>(3);
        Check<ushort>(4);
        Check<short>(1);
        Check<uint>(2);
        Check<int>(3);
        Check<ulong>(1);
        Check<long>(2);
        Check<Half>(1);
        Check<float>(3);
        Check<double>(1);
    }

    [Theory]
    [InlineData(TiffBigTiff.Always, true)]
    [InlineData(TiffBigTiff.Never, false)]
    [InlineData(TiffBigTiff.Auto, false)]
    public void BigTiff_is_written_when_asked_or_needed(TiffBigTiff choice, bool expected)
    {
        var image = Sample<float>(31, 17, 1, 2);
        byte[] bytes = Write(new TiffFile().Add(image), TiffWriteOptions.Default with { BigTiff = choice });

        using var back = Reload(bytes);
        Assert.Equal(expected, back.Primary.Storage!.BigTiff);
        Assert.Equal(image.ReadRawPixels<float>(), back.Primary.ReadRawPixels<float>());
    }
}
