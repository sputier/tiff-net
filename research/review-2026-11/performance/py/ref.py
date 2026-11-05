"""Reference timings: tifffile (+imagecodecs, C codecs) read/write of the same files, 1 thread and all threads.
Median of N runs (after one warm-up), throughput on decoded pixel bytes."""
import sys, os, glob, time, statistics, numpy as np, tifffile, imagecodecs

d = sys.argv[1]
n = int(sys.argv[2]) if len(sys.argv) > 2 else 5
print('tifffile', tifffile.__version__, 'imagecodecs', imagecodecs.__version__, 'numpy', np.__version__, 'cores', os.cpu_count())


def bench(label, fn, nbytes, extra=''):
    fn()
    ts = []
    for _ in range(n):
        t = time.perf_counter(); fn(); ts.append(time.perf_counter() - t)
    m = statistics.median(ts)
    print(f'{label:<52} {m*1000:9.1f} ms {nbytes/1e6/m:8.0f} MB/s {extra() if callable(extra) else extra}', flush=True)


for f in ([] if 'noread' in sys.argv else sorted(glob.glob(os.path.join(d, '*.tif')))):
    name = os.path.basename(f)
    if name.startswith('out_') or name.startswith('mem_') or '_61' in name and 'f32' in name:
        continue
    a = tifffile.imread(f)
    for workers in (1, 0):
        bench(f'tifffile read {name} workers={workers or "all"}', lambda: tifffile.imread(f, maxworkers=workers or None), a.nbytes)

u16 = np.load(os.path.join(d, 'u16_24.npy'))
f32 = np.load(os.path.join(d, 'f32rgb_24.npy'))
out = os.path.join(d, 'out_py.tif')
for label, a, pred in (('u16_24', u16, 2), ('f32rgb_24', f32, 3)):
    rps = max(1, 256 * 1024 // (a.shape[1] * a.itemsize * (a.shape[2] if a.ndim == 3 else 1)))
    for comp, kw in (('none', {}), ('lzw+pred', dict(compression='lzw', predictor=pred)),
                     ('deflate6+pred', dict(compression='zlib', compressionargs={'level': 6}, predictor=pred)),
                     ('deflate6', dict(compression='zlib', compressionargs={'level': 6})),
                     ('packbits', dict(compression='packbits'))):
        for workers in (1, 0):
            if comp == 'none' and workers == 0:
                continue
            bench(f'tifffile write {label} {comp} workers={workers or "all"}',
                  lambda: tifffile.imwrite(out, a, rowsperstrip=rps, maxworkers=workers or None,
                                           photometric='rgb' if a.ndim == 3 else 'minisblack', **kw),
                  a.nbytes, lambda: f'out {os.path.getsize(out)/1e6:.1f} MB')
os.remove(out)
# raw codec references (C zlib/zlib-ng via imagecodecs)
raw = u16.tobytes()
for lvl in (1, 6):
    z = imagecodecs.zlib_encode(raw, level=lvl)
    bench(f'imagecodecs.zlib_encode u16 level={lvl}', lambda: imagecodecs.zlib_encode(raw, level=lvl), len(raw), f'ratio {len(z)/len(raw):.3f}')
    bench(f'imagecodecs.zlib_decode u16 level={lvl}', lambda: imagecodecs.zlib_decode(z), len(raw))
if hasattr(imagecodecs, 'deflate_encode'):
    z = imagecodecs.deflate_encode(raw, level=6)
    bench('imagecodecs.deflate_decode (libdeflate) u16', lambda: imagecodecs.deflate_decode(z, out=len(raw)), len(raw))
