using BenchmarkDotNet.Attributes;
using ImageMagick;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Tiff;
using SixLabors.ImageSharp.Formats.Tiff.Constants;
using SixLabors.ImageSharp.PixelFormats;

namespace Bench;

/// <summary>ImageSharp 3.1 and Magick.NET (Q16-HDRI) on the same files.</summary>
[Config(typeof(BigConfig))]
public class ThirdPartyReadBenchmarks
{
    [Params("u16_24_none.tif", "u16_24_lzw_p2.tif", "u16_24_zip_p2.tif", "f32rgb_24_none.tif", "f32rgb_24_zip_p3.tif")]
    public string File_ { get; set; } = "";

    [Benchmark]
    public object ImageSharp_Load()
    {
        // L16 for grey 16-bit, RgbaVector (float) for float RGB: the closest lossless pixel types.
        if (File_.StartsWith("u16")) return SixLabors.ImageSharp.Image.Load<L16>(Data.P(File_));
        return SixLabors.ImageSharp.Image.Load<RgbaVector>(Data.P(File_));
    }

    [Benchmark]
    public object MagickNet_Read() => new MagickImage(Data.P(File_));

    [Benchmark]
    public object TiffNet_Read() => Eleris.TiffNet.TiffImage.Read(Data.P(File_));
}

[Config(typeof(BigConfig))]
public class ThirdPartyWriteBenchmarks
{
    [Params("none", "lzw", "deflate")]
    public string Compression { get; set; } = "";

    SixLabors.ImageSharp.Image<L16> _is = null!;
    MagickImage _mk = null!;
    string _out = "";

    [GlobalSetup]
    public void Setup()
    {
        _out = Data.P("tp_out.tif");
        _is = SixLabors.ImageSharp.Image.Load<L16>(Data.P("u16_24_none.tif"));
        _mk = new MagickImage(Data.P("u16_24_none.tif"));
    }

    [GlobalCleanup]
    public void Cleanup() => File.Delete(_out);

    [Benchmark]
    public void ImageSharp_Save_u16()
    {
        var enc = new TiffEncoder
        {
            BitsPerPixel = TiffBitsPerPixel.Bit16,
            Compression = Compression switch { "lzw" => TiffCompression.Lzw, "deflate" => TiffCompression.Deflate, _ => TiffCompression.None },
            HorizontalPredictor = Compression == "none" ? TiffPredictor.None : TiffPredictor.Horizontal,
        };
        _is.Save(_out, enc);
    }

    [Benchmark]
    public void MagickNet_Write_u16()
    {
        _mk.Settings.Compression = Compression switch { "lzw" => ImageMagick.CompressionMethod.LZW, "deflate" => ImageMagick.CompressionMethod.Zip, _ => ImageMagick.CompressionMethod.NoCompression };
        if (Compression != "none") _mk.Settings.SetDefine(MagickFormat.Tiff, "predictor", "2");
        _mk.Write(_out, MagickFormat.Tiff);
    }

    [Benchmark]
    public void TiffNet_Save_u16() => Data.U16_24.Save(_out, new Eleris.TiffNet.TiffWriteOptions
    {
        Compression = Compression switch { "lzw" => Eleris.TiffNet.TiffCompression.Lzw, "deflate" => Eleris.TiffNet.TiffCompression.Deflate, _ => Eleris.TiffNet.TiffCompression.None },
    });
}
