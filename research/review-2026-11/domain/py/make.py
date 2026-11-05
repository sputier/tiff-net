import sys, numpy as np, tifffile
from PIL import Image, ImageCms
d = sys.argv[1]
rng = np.random.default_rng(1)
h, w = 64, 48
def save(name, data, **kw):
    try:
        tifffile.imwrite(f"{d}/{name}", data, **kw); print("ok", name)
    except Exception as e: print("FAIL", name, e)
g16 = rng.integers(0, 65535, (h, w), dtype=np.uint16)
save("int8.tif", rng.integers(-128, 127, (h, w), dtype=np.int8))
f = rng.random((h, w), dtype=np.float32); f[0,0]=np.nan; f[0,1]=np.inf; f[0,2]=-np.inf; f[0,3]=-0.0
f.view(np.uint32)[0,4] = 0x7fc00123  # NaN payload
save("float_nan.tif", f)
save("float_nan_deflate_pred3.tif", f, compression='zlib', predictor=3)
save("float_tiled_pred3_be.tif", f.astype('>f4'), compression='zlib', predictor=3, tile=(16,16), byteorder='>')
save("minisblack_3samples.tif", rng.integers(0,65535,(h,w,3),dtype=np.uint16), photometric='minisblack', planarconfig='contig')
save("minisblack_3planes.tif", rng.integers(0,65535,(3,h,w),dtype=np.uint16), photometric='minisblack')
save("rgba_unassoc.tif", rng.integers(0,65535,(h,w,4),dtype=np.uint16), photometric='rgb', extrasamples=['unassalpha'])
save("rgba_assoc.tif", rng.integers(0,65535,(h,w,4),dtype=np.uint16), photometric='rgb', extrasamples=['assocalpha'])
save("float16.tif", rng.random((h,w)).astype(np.float16))
save("miniswhite16.tif", g16, photometric='miniswhite')
save("mask1bit.tif", rng.integers(0,2,(h,w)).astype(bool))
with tifffile.TiffWriter(f"{d}/multipage.tif") as t:
    t.write(g16, description="page0"); t.write(g16[::2, ::2], description="page1-thumb", subfiletype=1); t.write(g16 // 2, description="page2")
with tifffile.TiffWriter(f"{d}/pyramid_subifds.tif") as t:
    t.write(g16, subifds=1, description="full"); t.write(g16[::2, ::2], subfiletype=1)
save("zstd.tif", g16, compression='zstd')
save("lzw_pred2_be.tif", g16.astype('>u2'), compression='lzw', predictor=2, byteorder='>')
save("with_meta.tif", g16, description="OBJECT  = 'M42'", resolution=(300,300), resolutionunit='INCH', software='tifffile-test', datetime='2026:11:05 12:00:00', metadata=None)
save("rotated_6.tif", g16, extratags=[(274, 'H', 1, 6, True)])
save("complex.tif", (rng.random((h,w))+1j).astype(np.complex64))
save("int16_signed.tif", rng.integers(-32768, 32767, (h,w), dtype=np.int16))
save("rgb_float_planar.tif", rng.random((3,h,w), dtype=np.float32), photometric='rgb', planarconfig='separate')
# Pillow outputs
im = Image.fromarray(rng.integers(0,255,(h,w,3),dtype=np.uint8), 'RGB')
srgb = ImageCms.ImageCmsProfile(ImageCms.createProfile("sRGB")).tobytes()
im.save(f"{d}/pil_rgb8_icc.tif", icc_profile=srgb, dpi=(300,300), description="pillow desc"); print("ok pil_rgb8_icc")
try: im.save(f"{d}/pil_rgb8_jpeg.tif", compression="jpeg"); print("ok pil_jpeg")
except Exception as e: print("FAIL pil jpeg", e)
im.save(f"{d}/pil_rgb8_deflate.tif", compression="tiff_adobe_deflate"); print("ok pil deflate")
Image.fromarray(f.astype(np.float32), 'F').save(f"{d}/pil_float.tif"); print("ok pil float")
Image.fromarray(g16).save(f"{d}/pil_u16.tif"); print("ok pil u16")
im.convert("P").save(f"{d}/pil_palette.tif"); print("ok palette")
