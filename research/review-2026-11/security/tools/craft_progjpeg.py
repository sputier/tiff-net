"""Progressive JPEG-in-TIFF with many cheap AC scans (EOBRUN covers the whole image per scan)."""
import sys, struct
sys.path.insert(0, sys.argv[1])
from tiffcraft import *

class BW:
    def __init__(s): s.out = bytearray(); s.acc = 0; s.n = 0
    def bits(s, v, n):
        for i in range(n - 1, -1, -1):
            s.acc = (s.acc << 1) | ((v >> i) & 1); s.n += 1
            if s.n == 8:
                s.out.append(s.acc)
                if s.acc == 0xFF: s.out.append(0)
                s.acc = 0; s.n = 0
    def flush(s):
        while s.n: s.bits(1, 1)
        return bytes(s.out)

def seg(marker, body): return struct.pack('>BBH', 0xFF, marker, len(body) + 2) + body

def jpeg(w, h, nscans, dc_scan=True):
    blocks = ((w + 7) // 8) * ((h + 7) // 8)
    j = bytearray(b'\xff\xd8')
    j += seg(0xDB, b'\x00' + b'\x01' * 64)
    j += seg(0xC2, struct.pack('>BHHB', 8, h, w, 1) + b'\x01\x11\x00')
    # DC table 0: one code of length 1 -> category 0. AC table 0: one code length 1 -> 0xE0 (EOB14)
    j += seg(0xC4, b'\x00' + bytes([1] + [0] * 15) + b'\x00')
    j += seg(0xC4, b'\x10' + bytes([1] + [0] * 15) + b'\xe0')
    if dc_scan:
        j += seg(0xDA, b'\x01' + b'\x01\x00' + bytes([0, 0, 0x00]))
        bw = BW()
        for _ in range(blocks): bw.bits(0, 1)
        j += bw.flush()
    bw = BW()
    left = blocks
    while left > 0:
        bw.bits(0, 1); bw.bits(0x3FFF, 14); left -= 32767
    acdata = bw.flush()
    sos = seg(0xDA, b'\x01' + b'\x01\x00' + bytes([1, 63, 0x00]))
    for _ in range(nscans):
        j += sos + acdata
    j += b'\xff\xd9'
    return bytes(j)

out = sys.argv[2]
for (w, h, n, name) in [(4096, 4096, 100, 'jpeg-prog-4k-100scans.tif'), (4096, 4096, 1000, 'jpeg-prog-4k-1000scans.tif'), (4096, 4096, 10000, 'jpeg-prog-4k-10000scans.tif')]:
    data = jpeg(w, h, n)
    e = basic(w, h, compression=7, rps=h, offsets=[8], counts=[len(data)])
    pad = (w * h) // 4096 + 1024
    d, _ = build(e, payload=data, pad_to=max(pad, len(data) + 400))
    open(f'{out}/{name}', 'wb').write(d); print(name, len(d), 'jpeg', len(data))
