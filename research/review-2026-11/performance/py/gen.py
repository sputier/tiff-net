"""Generates realistic astronomy-like TIFF test files with tifffile (+imagecodecs).

u16 grey: sky background ~1200 ADU + gradient + Gaussian read/shot noise (sigma ~25) + ~4000 stars (Gaussian PSF), like a
raw/calibrated camera frame. float32 RGB: normalised [0,1] stacked image (lower noise, sigma ~0.004) - full-entropy mantissas.
"""
import sys, os, numpy as np, tifffile

out = sys.argv[1]
os.makedirs(out, exist_ok=True)
rng = np.random.default_rng(42)


def sky(h, w, noise, base, stars):
    y, x = np.mgrid[0:h, 0:w].astype(np.float32)
    img = base * (1 + 0.15 * x / w + 0.08 * y / h)
    img += rng.normal(0, noise, (h, w)).astype(np.float32)
    # stars: add small gaussian blobs
    n = stars
    sy = rng.integers(8, h - 8, n); sx = rng.integers(8, w - 8, n)
    amp = rng.pareto(1.5, n) * base * 0.5 + base * 0.2
    r = np.arange(-7, 8, dtype=np.float32)
    k = np.exp(-(r[:, None] ** 2 + r[None, :] ** 2) / (2 * 1.8 ** 2))
    for i in range(n):
        img[sy[i] - 7:sy[i] + 8, sx[i] - 7:sx[i] + 8] += amp[i] * k
    return img


def u16(h, w):
    return np.clip(sky(h, w, 25, 1200, 4000), 0, 65535).astype(np.uint16)


def f32rgb(h, w):
    g = sky(h, w, 0.004, 0.08, 4000) / 1.0
    return np.stack([np.clip(g * f, 0, 1).astype(np.float32) for f in (1.0, 0.93, 1.07)], axis=-1)


def w(name, data, **kw):
    p = os.path.join(out, name)
    if os.path.exists(p):
        return
    tifffile.imwrite(p, data, **kw)
    print(name, os.path.getsize(p) // 1024, 'KB', flush=True)


RPS = dict(rowsperstrip=None)
H, W = 4000, 6000           # 24 MP
gu = u16(H, W)
np.save(os.path.join(out, 'u16_24.npy'), gu)
# strip ~256 KB like the facade writes: 6000*2 = 12 KB per row -> 21 rows
w('u16_24_none.tif', gu, rowsperstrip=21)
w('u16_24_lzw_p2.tif', gu, compression='lzw', predictor=2, rowsperstrip=21)
w('u16_24_lzw.tif', gu, compression='lzw', rowsperstrip=21)
w('u16_24_zip_p2.tif', gu, compression='zlib', compressionargs={'level': 6}, predictor=2, rowsperstrip=21)
w('u16_24_zip.tif', gu, compression='zlib', compressionargs={'level': 6}, rowsperstrip=21)
w('u16_24_packbits.tif', gu, compression='packbits', rowsperstrip=21)
w('u16_24_tiled_zip_p2.tif', gu, compression='zlib', compressionargs={'level': 6}, predictor=2, tile=(256, 256))
w('u16_24_tiled_none.tif', gu, tile=(256, 256))
w('u16_24_none_be.tif', gu, rowsperstrip=21, byteorder='>')
w('u16_24_zip_p2_be.tif', gu, compression='zlib', compressionargs={'level': 6}, predictor=2, rowsperstrip=21, byteorder='>')
w('u16_24_none_onestrip.tif', gu, rowsperstrip=H)  # single strip, PixInsight style
w('u16_24_none_flip.tif', gu, rowsperstrip=21, extratags=[(274, 'H', 1, 4, True)])  # Orientation=BOTLEFT
del gu

frgb = f32rgb(H, W)
np.save(os.path.join(out, 'f32rgb_24.npy'), frgb)
# 6000*12 = 72 KB per row -> 4 rows per strip (~288 KB)
w('f32rgb_24_none.tif', frgb, photometric='rgb', rowsperstrip=4)
w('f32rgb_24_lzw_p3.tif', frgb, photometric='rgb', compression='lzw', predictor=3, rowsperstrip=4)
w('f32rgb_24_zip_p3.tif', frgb, photometric='rgb', compression='zlib', compressionargs={'level': 6}, predictor=3, rowsperstrip=4)
w('f32rgb_24_zip.tif', frgb, photometric='rgb', compression='zlib', compressionargs={'level': 6}, rowsperstrip=4)
w('f32rgb_24_planar_none.tif', np.ascontiguousarray(frgb.transpose(2,0,1)), photometric='rgb', planarconfig='separate', rowsperstrip=11)
w('f32rgb_24_planar_zip_p3.tif', np.ascontiguousarray(frgb.transpose(2,0,1)), photometric='rgb', planarconfig='separate', compression='zlib', compressionargs={'level': 6}, predictor=3, rowsperstrip=11)
w('f32rgb_24_tiled_zip_p3.tif', frgb, photometric='rgb', compression='zlib', compressionargs={'level': 6}, predictor=3, tile=(256, 256))
del frgb

# ~61 MP 16-bit (9576 x 6388, IMX455) - uncompressed and deflate+predictor
H6, W6 = 6388, 9576
g6 = u16(H6, W6)
w('u16_61_none.tif', g6, rowsperstrip=13)
w('u16_61_zip_p2.tif', g6, compression='zlib', compressionargs={'level': 6}, predictor=2, rowsperstrip=13)
del g6
