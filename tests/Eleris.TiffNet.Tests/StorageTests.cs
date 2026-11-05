using Eleris.TiffNet.Tests.Fixtures;
using Eleris.TiffNet.Tests.Interop;

namespace Eleris.TiffNet.Tests;

public class StorageTests
{
    [Fact]
    public void The_storage_of_every_domain_fixture_is_what_tifffile_reads()
    {
        foreach (var (name, entry) in DomainFixtures.Files)
        {
            using var file = TiffFile.Open(DomainFixtures.PathOf(name));
            for (int i = 0; i < file.Count; i++)
            {
                var page = entry.GetProperty("pages")[i];
                var s = file[i].Storage!;
                string what = $"{name}, page {i}";
                Assert.True(page.GetProperty("compression").GetInt32() == (int)s.Compression, what);
                Assert.True(page.GetProperty("photometric").GetInt32() == (int)s.Photometric, what);
                Assert.True(page.GetProperty("bitsPerSample").GetInt32() == s.BitsPerSample, what);
                Assert.True(page.GetProperty("orientation").GetInt32() == s.Orientation, what);
                Assert.True(page.GetProperty("planar").GetBoolean() == (s.PlanarConfiguration == TiffPixelOrder.Planar), what);
                Assert.True(entry.GetProperty("bigEndian").GetBoolean() == s.BigEndian, what);
                Assert.False(s.BigTiff, what);
            }
        }
    }

    [Fact]
    public void What_the_delivered_samples_hide_is_in_the_storage()
    {
        TiffStorage Of(string name)
        {
            using var file = TiffFile.Open(DomainFixtures.PathOf(name));
            return file.Primary.Storage!;
        }

        Assert.Equal(TiffPhotometric.Palette, Of("palette_8bit.tif").Photometric);
        Assert.Equal(TiffPhotometric.MinIsWhite, Of("miniswhite_16bit.tif").Photometric);
        Assert.Equal(12, Of("grey_12bit_packed.tif").BitsPerSample);
        Assert.Equal(6, Of("orientation_6.tif").Orientation);
        Assert.Equal(TiffCompression.Jpeg, Of("pillow_rgb8_jpeg.tif").Compression);
        Assert.Equal(TiffCompression.Deflate, Of("siril_m42_grey16_deflate.tif").Compression);
        Assert.Equal((TiffLayout.Strips, 0, 0), (Of("multipage.tif").Layout, Of("multipage.tif").TileWidth, Of("multipage.tif").TileHeight));
    }

    [Fact]
    public void The_storage_of_every_interop_file_matches_its_name()
    {
        if (!Directory.Exists(InteropCorpus.Folder))
        {
            Assert.True(InteropCorpus.Skipped, $"The interop corpus is not generated; set {InteropCorpus.SkipVariable}=1 to skip.");
            return;
        }

        foreach (var f in InteropCorpus.Files)
        {
            using var file = TiffFile.Open(Path.Combine(InteropCorpus.Folder, f.Name));
            var s = file.Primary.Storage!;
            int code = f.Compression switch { "none" => 1, "lzw" => 5, "zlib" => 8, "packbits" => 32773, "zstd" => 50000, _ => 34925 };
            string what = f.Name;
            Assert.True((int)s.Compression == code, what);
            Assert.True((s.Predictor != TiffPredictor.None) == f.Predictor, what);
            Assert.True((s.Layout == TiffLayout.Tiles) == f.Tiled, what);
            Assert.True((s.PlanarConfiguration == TiffPixelOrder.Planar) == f.Planar, what);
            Assert.True(s.BigTiff == f.BigTiff, what);
            Assert.True(s.BigEndian == f.BigEndian, what);
            if (f.Tiled)
            {
                Assert.True((s.TileWidth, s.TileHeight, s.RowsPerStrip) == (16, 16, 0), what);
            }
            else if (f.Predictor && f.SampleType is "int64" or "uint64")
            {
                Assert.True(s.RowsPerStrip == 8, what);   // encoded by the generator, 8 rows per strip
            }
        }
    }
}
