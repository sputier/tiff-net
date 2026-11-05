using System.Diagnostics;
using System.Security.Cryptography;
using System.Runtime.InteropServices;
using Eleris.TiffNet;

string inDir = args[0], outDir = args[1];
if (args.Length > 2 && args[2] == "read")
{
    foreach (var f in Directory.GetFiles(inDir, "*.tif").Order())
    {
        try
        {
            var sw = Stopwatch.StartNew();
            var img = TiffImage.Read(f);
            var ms = sw.ElapsedMilliseconds;
            string first = img.SampleType switch
            {
                TiffSampleType.Float32 => string.Join(",", img.GetPixels<float>()[..Math.Min(6, img.Width * img.Channels)].ToArray().Select(v => $"{v}/{BitConverter.SingleToUInt32Bits(v):X8}")),
                TiffSampleType.UInt16 => string.Join(",", img.GetPixels<ushort>()[..6].ToArray()),
                TiffSampleType.Int8 => string.Join(",", img.GetPixels<sbyte>()[..6].ToArray()),
                TiffSampleType.Int16 => string.Join(",", img.GetPixels<short>()[..6].ToArray()),
                TiffSampleType.UInt8 => string.Join(",", img.GetPixels<byte>()[..6].ToArray()),
                _ => "",
            };
            Console.WriteLine($"OK   {Path.GetFileName(f)}: {img.Width}x{img.Height}x{img.Channels} {img.SampleType} {ms}ms first=[{first}]");
        }
        catch (Exception e) { Console.WriteLine($"FAIL {Path.GetFileName(f)}: {e.GetType().Name}: {e.Message}"); }
    }
    return;
}

// Write probes
var rnd = new Random(1);
int w = 40, h = 30;
void W<T>(string name, int ch, TiffCompression c, Func<int, T> gen) where T : unmanaged
{
    var px = Enumerable.Range(0, w * h * ch).Select(gen).ToArray();
    try { TiffImage.Create(w, h, ch, px).Save(Path.Combine(outDir, name), new TiffWriteOptions { Compression = c }); Console.WriteLine($"wrote {name}"); }
    catch (Exception e) { Console.WriteLine($"FAIL write {name}: {e.Message}"); }
}
W<ushort>("w_u16_deflate.tif", 1, TiffCompression.Deflate, i => (ushort)(i * 37));
W<ushort>("w_u16_lzw.tif", 1, TiffCompression.Lzw, i => (ushort)(i * 37));
W<ushort>("w_u16_packbits.tif", 1, TiffCompression.PackBits, i => (ushort)(i * 37));
W<float>("w_f32_deflate.tif", 1, TiffCompression.Deflate, i => i == 0 ? float.NaN : i / 1000f);
W<float>("w_f32_lzw.tif", 1, TiffCompression.Lzw, i => i / 1000f);
W<float>("w_rgb_f32.tif", 3, TiffCompression.Deflate, i => i / 3600f);
W<ushort>("w_rgb_u16.tif", 3, TiffCompression.Deflate, i => (ushort)(i * 17));
W<byte>("w_rgb_u8.tif", 3, TiffCompression.Lzw, i => (byte)i);
W<byte>("w_rgba_u8.tif", 4, TiffCompression.None, i => (byte)i);
W<byte>("w_grey_alpha_u8.tif", 2, TiffCompression.None, i => (byte)i);
W<sbyte>("w_i8.tif", 1, TiffCompression.Deflate, i => (sbyte)(i - 128));
W<short>("w_i16.tif", 1, TiffCompression.Deflate, i => (short)(i * 50 - 30000));
W<int>("w_i32.tif", 1, TiffCompression.Deflate, i => i * 1000 - 500000);
W<ulong>("w_u64.tif", 1, TiffCompression.Deflate, i => (ulong)i << 40);
W<double>("w_f64.tif", 1, TiffCompression.Deflate, i => i / 7.0);
W<float>("w_5ch_f32.tif", 5, TiffCompression.None, i => i);

// NaN payload round trip
var nan = new float[] { BitConverter.UInt32BitsToSingle(0x7FC00123), -0f, float.PositiveInfinity, 1 };
using (var ms = new MemoryStream())
{
    TiffImage.Create(2, 2, 1, nan).Save(ms, new TiffWriteOptions { Compression = TiffCompression.Deflate });
    ms.Position = 0;
    var back = TiffImage.Read(ms).GetPixels<float>().ToArray();
    Console.WriteLine("nan payload roundtrip: " + string.Join(",", back.Select(v => BitConverter.SingleToUInt32Bits(v).ToString("X8"))));
}

// 3-channel grey round trip semantics: read minisblack_3samples and resave
try
{
    var img = TiffImage.Read(Path.Combine(inDir, "minisblack_3samples.tif"));
    img.Save(Path.Combine(outDir, "resaved_minisblack_3samples.tif"));
    Console.WriteLine($"resaved minisblack 3 samples ch={img.Channels}");
    var rgba = TiffImage.Read(Path.Combine(inDir, "rgba_assoc.tif"));
    rgba.Save(Path.Combine(outDir, "resaved_rgba_assoc.tif"));
    Console.WriteLine($"resaved rgba assoc ch={rgba.Channels}");
}
catch (Exception e) { Console.WriteLine("resave fail " + e.Message); }

// memory / time for a big image
{
    long before = GC.GetTotalAllocatedBytes(true);
    var sw = Stopwatch.StartNew();
    var big = new float[6000 * 4000 * 3];
    var img = TiffImage.Create(6000, 4000, 3, big);
    long afterCreate = GC.GetTotalAllocatedBytes(true);
    var path = Path.Combine(outDir, "big.tif");
    img.Save(path, new TiffWriteOptions { Compression = TiffCompression.Deflate });
    long afterSave = GC.GetTotalAllocatedBytes(true);
    var t1 = sw.ElapsedMilliseconds; sw.Restart();
    var r = TiffImage.Read(path);
    long afterRead = GC.GetTotalAllocatedBytes(true);
    float[] asArray = r.GetPixels<float>().ToArray();
    long afterArray = GC.GetTotalAllocatedBytes(true);
    Console.WriteLine($"6000x4000x3 f32 ({big.Length * 4 / 1e6:F0} MB): create alloc {(afterCreate - before - big.Length*4L) / 1e6:F0} MB, save {t1} ms alloc {(afterSave - afterCreate) / 1e6:F0} MB, read {sw.ElapsedMilliseconds} ms alloc {(afterRead - afterSave) / 1e6:F0} MB, ToArray {(afterArray - afterRead) / 1e6:F0} MB, file {new FileInfo(path).Length / 1e6:F0} MB");
    File.Delete(path);
}
