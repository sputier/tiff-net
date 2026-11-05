import sys, struct, zlib, random
out = sys.argv[1]
import os
def w(name, d): open(os.path.join(out, name), 'wb').write(d); print(name, len(d))

# BigTIFF with many unique unknown tags (dircount is 64-bit, so > 32767 entries possible)
def bigtiff(entries, payload):
    # entries: (tag, type, count, 8-byte inline value)
    entries = sorted(entries)
    h = bytearray(b'II+\x00\x08\x00\x00\x00') + struct.pack('<Q', 0)
    poff = len(h); h += payload
    while len(h) % 8: h += b'\x00'
    ifd = len(h); struct.pack_into('<Q', h, 8, ifd)
    h += struct.pack('<Q', len(entries))
    for tag, typ, cnt, val in entries: h += struct.pack('<HHQ', tag, typ, cnt) + val.ljust(8, b'\x00')
    h += struct.pack('<Q', 0)
    return bytes(h)
base = [(256, 3, 1, struct.pack('<H', 1)), (257, 3, 1, struct.pack('<H', 1)), (258, 3, 1, struct.pack('<H', 8)), (259, 3, 1, struct.pack('<H', 1)),
        (262, 3, 1, struct.pack('<H', 1)), (273, 16, 1, struct.pack('<Q', 16)), (277, 3, 1, struct.pack('<H', 1)), (279, 16, 1, struct.pack('<Q', 1))]
used = {e[0] for e in base}
for n in (32000, 65000):
    tags = [t for t in range(300, 65536) if t not in used][:n]
    e = base + [(t, 3, 1, struct.pack('<H', 1)) for t in tags]
    w(f'dos-bigtiff-unknowntags-{n//1000}k.tif', bigtiff(e, b'\x00' * 8))

# Many tiles: TileOffsets and TileByteCounts share one SHORT array of value 64; a zlib stream of a 16x16 gray tile at offset 64.
N = 512 * 1024
z = zlib.compress(b'\x00' * 256, 9)
assert len(z) < 64
d = bytearray(b'II*\x00\x00\x00\x00\x00') + b'\x00' * 56 + z.ljust(64, b'\x00')  # offset 64..128
arr_off = len(d); d += struct.pack('<I', 64) * N
ifd = len(d); struct.pack_into('<I', d, 4, ifd)
tw = 16; cols = 1024; rows = N // cols
ents = [(256, 4, 1, struct.pack('<I', tw * cols)), (257, 4, 1, struct.pack('<I', 16 * rows)), (258, 3, 1, struct.pack('<H', 8)), (259, 3, 1, struct.pack('<H', 8)),
        (262, 3, 1, struct.pack('<H', 1)), (277, 3, 1, struct.pack('<H', 1)), (322, 3, 1, struct.pack('<H', 16)), (323, 3, 1, struct.pack('<H', 16)),
        (324, 4, N, struct.pack('<I', arr_off)), (325, 4, N, struct.pack('<I', arr_off))]
d += struct.pack('<H', len(ents))
for t, ty, c, v in ents: d += struct.pack('<HHI', t, ty, c) + v.ljust(4, b'\x00')
d += b'\x00' * 4
w('dos-512k-tiles.tif', bytes(d))
