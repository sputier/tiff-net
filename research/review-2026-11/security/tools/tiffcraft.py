"""Minimal classic-TIFF builder for crafting hostile files."""
import struct
SIZES = {1:1,2:1,3:2,4:4,5:8,6:1,7:1,8:2,9:4,10:8,11:4,12:8,16:8,17:8,18:8}
FMT = {1:'B',3:'H',4:'I',8:'h',9:'i',16:'Q'}

def entry(tag, typ, values=None, raw=None, count=None):
    """values: list of ints for typ in FMT; raw: bytes payload; count overrides."""
    if raw is None:
        raw = b''.join(struct.pack('<' + FMT[typ], v) for v in values)
        if count is None: count = len(values)
    if count is None: count = len(raw) // SIZES.get(typ, 1)
    return (tag, typ, count, raw)

def build(entries, payload=b'', pad_to=0, next_ifd=0, sort=True, payload_first=True):
    """Header + payload at offset 8 + IFD. Returns (bytes, payload_offset).
    raw values > 4 bytes are appended after the IFD."""
    if sort: entries = sorted(entries, key=lambda e: e[0])
    out = bytearray(b'II*\x00\x00\x00\x00\x00')
    payload_off = len(out)
    out += payload
    if len(out) % 2: out += b'\x00'
    ifd_off = len(out)
    struct.pack_into('<I', out, 4, ifd_off)
    n = len(entries)
    extra_off = ifd_off + 2 + 12 * n + 4
    ifd = bytearray(struct.pack('<H', n))
    extra = bytearray()
    for tag, typ, count, raw in entries:
        if len(raw) <= 4:
            ifd += struct.pack('<HHI', tag, typ, count) + raw.ljust(4, b'\x00')
        else:
            off = extra_off + len(extra)
            ifd += struct.pack('<HHII', tag, typ, count, off)
            extra += raw
            if len(extra) % 2: extra += b'\x00'
    ifd += struct.pack('<I', next_ifd)
    out += ifd + extra
    if pad_to > len(out): out += b'\x00' * (pad_to - len(out))
    return bytes(out), payload_off

def basic(width, height, bps=8, spp=1, photometric=1, compression=1, rps=None, offsets=(8,), counts=(1,), extra=(), sampleformat=None, planar=None, off_type=4, cnt_type=4):
    e = [entry(256, 4, [width]), entry(257, 4, [height]), entry(258, 3, [bps] * spp if spp <= 2 else [bps]*spp),
         entry(259, 3, [compression]), entry(262, 3, [photometric]), entry(277, 3, [spp]),
         entry(273, off_type, list(offsets)), entry(279, cnt_type, list(counts))]
    if rps is not None: e.append(entry(278, 4, [rps]))
    if sampleformat is not None: e.append(entry(339, 3, [sampleformat]*spp))
    if planar is not None: e.append(entry(284, 3, [planar]))
    e.extend(extra)
    return e
