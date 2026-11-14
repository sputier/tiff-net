"""Generates hostile TIFF files: tiny files that try to make a reader allocate, loop or compute without bound.

    pip install -r tools/requirements-interop.txt
    python -I tools/make_hostile.py

Writes tests/Fixtures/hostile/crafted/*.tif (committed) and updates the "crafted" part of tests/Fixtures/hostile/manifest.json.
Every file must fail with a TiffException (or a NotSupportedException for a feature refused by design) within the time and
allocation bounds of the manifest; the tests assert it with HostileAssert. The cases come from the audit of the first
implementation (findings SEC-01 to SEC-06), where each one was measured.

The files are built byte by byte (classic TIFF with tiffcraft-like helpers, BigTIFF by hand), so that their defects are exactly
the intended ones; the compressed payloads use zlib, imagecodecs (LZW) and a PackBits run written here.
"""
from __future__ import annotations

import json
import struct
import zlib
from pathlib import Path

import imagecodecs

ROOT = Path(__file__).resolve().parent.parent
FOLDER = ROOT / "tests" / "Fixtures" / "hostile"
OUT = FOLDER / "crafted"

# TIFF field types: byte size and struct format.
SIZES = {1: 1, 2: 1, 3: 2, 4: 4, 5: 8, 6: 1, 7: 1, 8: 2, 9: 4, 10: 8, 11: 4, 12: 8, 16: 8, 17: 8, 18: 8}
FORMATS = {1: "B", 3: "H", 4: "I", 8: "h", 9: "i", 16: "Q"}
SHORT, LONG = 3, 4

cases: dict[str, dict] = {}


# ---- Building classic TIFF --------------------------------------------------------------------------------------------------

def entry(tag: int, typ: int, values=None, raw: bytes | None = None, count: int | None = None):
    if raw is None:
        raw = b"".join(struct.pack("<" + FORMATS[typ], v) for v in values)
        count = len(values) if count is None else count
    if count is None:
        count = len(raw) // SIZES.get(typ, 1)
    return tag, typ, count, raw


def build(entries, payload: bytes = b"", sort: bool = True, pad_to: int = 0) -> bytes:
    """Header, payload at offset 8, then the directory; values longer than 4 bytes after the directory."""
    if sort:
        entries = sorted(entries, key=lambda e: e[0])
    out = bytearray(b"II*\x00\x00\x00\x00\x00") + payload
    if len(out) % 2:
        out += b"\x00"
    directory = len(out)
    struct.pack_into("<I", out, 4, directory)
    extra_offset = directory + 2 + 12 * len(entries) + 4
    ifd, extra = bytearray(struct.pack("<H", len(entries))), bytearray()
    for tag, typ, count, raw in entries:
        if len(raw) <= 4:
            ifd += struct.pack("<HHI", tag, typ, count) + raw.ljust(4, b"\x00")
        else:
            ifd += struct.pack("<HHII", tag, typ, count, extra_offset + len(extra))
            extra += raw
            if len(extra) % 2:
                extra += b"\x00"
    out += ifd + struct.pack("<I", 0) + extra
    if pad_to > len(out):
        out += bytes(pad_to - len(out))
    return bytes(out)


def grey(width, height, *, bits=8, compression=1, rows=None, offsets=(8,), counts=(1,), extra=(), offset_type=LONG):
    entries = [entry(256, LONG, [width]), entry(257, LONG, [height]), entry(258, SHORT, [bits]), entry(259, SHORT, [compression]),
               entry(262, SHORT, [1]), entry(277, SHORT, [1]),
               entry(273, offset_type, list(offsets)), entry(279, offset_type, list(counts))]
    if rows is not None:
        entries.append(entry(278, LONG, [rows]))
    return entries + list(extra)


# Reading options of a case, when the defaults would let a valid but large file through: 64 MB of pixels, as a phone would set.
SMALL_DEVICE = {"MaxPixelBytes": 64 << 20}


def write(name: str, data: bytes, finding: str, attack: str, outcome: str = "TiffException", seconds: float = 2.0,
          megabytes: int = 64, options: dict | None = None) -> None:
    """Outcome: "TiffException" (damaged: no limit), "TiffException:Limit" (that limit), "TiffException:*" (any limit),
    "NotSupportedException", or alternatives separated by "|"."""
    (OUT / name).write_bytes(data)
    cases[name] = {"finding": finding, "attack": attack, "outcome": outcome, "options": options or {},
                   "maxSeconds": seconds, "maxAllocatedMB": megabytes}


# ---- Cases --------------------------------------------------------------------------------------------------------------------

def strip_and_tile_tables() -> None:
    # SEC-01: the number of strips comes from the geometry (1 x height, 1 row per strip) while the file holds one offset.
    for height, name in [(0x7FFFFFFF, "nstrips-2G.tif"), (0x10000000, "nstrips-256M.tif")]:
        write(name, build(grey(1, height, rows=1), payload=b"\x00\x00"), "SEC-01",
              f"1 x {height} image, one row per strip, a single strip offset: tables of {height} entries implied")
    write("nstrips-256M-short.tif", build(grey(1, 0x10000000, rows=1, offset_type=SHORT), payload=b"\x00\x00"), "SEC-01",
          "same with SHORT offset tables")
    tiles = [entry(256, LONG, [16 * 16384]), entry(257, LONG, [16 * 16384]), entry(258, SHORT, [8]), entry(259, SHORT, [1]),
             entry(262, SHORT, [1]), entry(277, SHORT, [1]), entry(322, LONG, [16]), entry(323, LONG, [16]),
             entry(324, LONG, [8]), entry(325, LONG, [1])]
    write("ntiles-256M.tif", build(tiles, payload=b"\x00\x00"), "SEC-01", "262144 x 262144 image in 16 x 16 tiles: 2^28 tiles, one offset")


def byte_counts() -> None:
    # SEC-02: a strip byte count far beyond the file.
    write("bytecount-2G-deflate.tif", build(grey(1, 1, compression=8, counts=(0x7FFFF000,)), payload=b"\x00\x00"), "SEC-02",
          "StripByteCounts of 2 GB in a file of a few bytes")


def unknown_tags() -> None:
    # SEC-03: many distinct unknown tags; the first implementation merged its field table once per tag.
    base = grey(1, 1)
    used = {e[0] for e in base}
    candidates = [t for t in range(1000, 65535) if t not in used and not 32768 <= t <= 34999 and not 36000 <= t <= 42999
                  and t not in (50706, 50707, 50708)]
    for count, name in [(5000, "unknowntags-5k.tif"), (20000, "unknowntags-20k.tif")]:
        entries = base + [entry(t, SHORT, [1]) for t in candidates[:count]]
        write(name, build(entries, payload=b"\x00\x00", sort=False), "SEC-03", f"{count} distinct unknown tags, unsorted",
              outcome="TiffException:MaxTagsPerDirectory")

    # BigTIFF: a 64-bit entry count allows more than 32767 entries.
    def bigtiff(entries, payload: bytes) -> bytes:
        entries = sorted(entries)
        head = bytearray(b"II+\x00\x08\x00\x00\x00") + struct.pack("<Q", 0)
        head += payload
        while len(head) % 8:
            head += b"\x00"
        directory = len(head)
        struct.pack_into("<Q", head, 8, directory)
        head += struct.pack("<Q", len(entries))
        for tag, typ, count, value in entries:
            head += struct.pack("<HHQ", tag, typ, count) + value.ljust(8, b"\x00")
        return bytes(head + struct.pack("<Q", 0))

    base8 = [(256, 3, 1, struct.pack("<H", 1)), (257, 3, 1, struct.pack("<H", 1)), (258, 3, 1, struct.pack("<H", 8)),
             (259, 3, 1, struct.pack("<H", 1)), (262, 3, 1, struct.pack("<H", 1)), (273, 16, 1, struct.pack("<Q", 16)),
             (277, 3, 1, struct.pack("<H", 1)), (279, 16, 1, struct.pack("<Q", 1))]
    used8 = {e[0] for e in base8}
    tags = [t for t in range(300, 65536) if t not in used8][:32000]
    write("bigtiff-unknowntags-32k.tif", bigtiff(base8 + [(t, 3, 1, struct.pack("<H", 1)) for t in tags], bytes(8)), "SEC-03",
          "BigTIFF directory of 32008 entries, 32000 unknown tags", outcome="TiffException:MaxTagsPerDirectory")


def many_tiles() -> None:
    # SEC-06: 512k tiny Deflate tiles whose offset and byte count tables are the same array of 64s: one 64-byte stream reused.
    n = 512 * 1024
    stream = zlib.compress(bytes(256), 9)
    data = bytearray(b"II*\x00\x00\x00\x00\x00") + bytes(56) + stream.ljust(64, b"\x00")
    table = len(data)
    data += struct.pack("<I", 64) * n
    directory = len(data)
    struct.pack_into("<I", data, 4, directory)
    columns = 1024
    entries = [(256, 4, 1, struct.pack("<I", 16 * columns)), (257, 4, 1, struct.pack("<I", 16 * (n // columns))),
               (258, 3, 1, struct.pack("<H", 8)), (259, 3, 1, struct.pack("<H", 8)), (262, 3, 1, struct.pack("<H", 1)),
               (277, 3, 1, struct.pack("<H", 1)), (322, 3, 1, struct.pack("<H", 16)), (323, 3, 1, struct.pack("<H", 16)),
               (324, 4, n, struct.pack("<I", table)), (325, 4, n, struct.pack("<I", table))]
    data += struct.pack("<H", len(entries))
    for tag, typ, count, value in entries:
        data += struct.pack("<HHI", tag, typ, count) + value.ljust(4, b"\x00")
    data += bytes(4)
    # A valid file of 128 MB of pixels: refused by a device that allows 64 MB, before 524288 tiles are decoded.
    write("tiles-512k-shared-stream.tif", bytes(data), "SEC-06", "524288 Deflate tiles of 16 x 16 sharing one 64-byte stream",
          outcome="TiffException:MaxPixelBytes", options=SMALL_DEVICE)


def decompression_bombs() -> None:
    # SEC-05: every strip offset points to the same small compressed strip, so a 0.5 MB file declares 2 GB of pixels.
    width, rows = 65536, 16
    strip = bytes(width * rows)
    payloads = {
        "deflate": (8, zlib.compress(strip, 9)),
        "lzw": (5, imagecodecs.lzw_encode(strip)),
        "packbits": (32773, packbits_zeros(len(strip))),
    }
    strips = 2048
    for name, (code, stream) in payloads.items():
        entries = grey(width, rows * strips, compression=code, rows=rows, offsets=[8] * strips, counts=[len(stream)] * strips)
        # 65536 x 32768 bytes = 2^31, just beyond the default MaxPixelBytes (2 147 483 591): the default limit is tested here.
        write(f"bomb-{name}-2G.tif", build(entries, payload=stream), "SEC-05",
              f"{strips} strips of {width} x {rows} sharing one {len(stream)}-byte {name} stream: 2 GB of pixels declared",
              outcome="TiffException:MaxPixelBytes")


def packbits_zeros(length: int) -> bytes:
    out = bytearray()
    while length > 0:
        run = min(128, length)
        out += bytes([257 - run, 0])
        length -= run
    return bytes(out)


def progressive_jpeg() -> None:
    # SEC-04 and SEC-05: progressive JPEG whose AC scans cost a few bytes each (one EOBRUN covers 32767 blocks).
    class Bits:
        def __init__(self):
            self.out, self.acc, self.n = bytearray(), 0, 0

        def put(self, value, count):
            for i in range(count - 1, -1, -1):
                self.acc, self.n = (self.acc << 1) | ((value >> i) & 1), self.n + 1
                if self.n == 8:
                    self.out.append(self.acc)
                    if self.acc == 0xFF:
                        self.out.append(0)
                    self.acc, self.n = 0, 0

        def flush(self):
            while self.n:
                self.put(1, 1)
            return bytes(self.out)

    def segment(marker, body):
        return struct.pack(">BBH", 0xFF, marker, len(body) + 2) + body

    def jpeg(width, height, scans):
        blocks = ((width + 7) // 8) * ((height + 7) // 8)
        j = bytearray(b"\xff\xd8") + segment(0xDB, b"\x00" + b"\x01" * 64)
        j += segment(0xC2, struct.pack(">BHHB", 8, height, width, 1) + b"\x01\x11\x00")
        j += segment(0xC4, b"\x00" + bytes([1] + [0] * 15) + b"\x00")       # DC: one code -> category 0
        j += segment(0xC4, b"\x10" + bytes([1] + [0] * 15) + b"\xe0")       # AC: one code -> EOB14
        j += segment(0xDA, b"\x01\x01\x00" + bytes([0, 0, 0]))
        dc = Bits()
        for _ in range(blocks):
            dc.put(0, 1)
        j += dc.flush()
        ac = Bits()
        left = blocks
        while left > 0:
            ac.put(0, 1)
            ac.put(0x3FFF, 14)
            left -= 32767
        scan = segment(0xDA, b"\x01\x01\x00" + bytes([1, 63, 0])) + ac.flush()
        for _ in range(scans):
            j += scan
        return bytes(j + b"\xff\xd9")

    for width, height, scans, name, finding in [(4096, 4096, 1000, "jpeg-progressive-1000-scans.tif", "SEC-04"),
                                                 (4096, 4096, 10000, "jpeg-progressive-10000-scans.tif", "SEC-04"),
                                                 (16384, 16384, 1, "jpeg-progressive-coefficients-16k.tif", "SEC-05")]:
        stream = jpeg(width, height, scans)
        entries = grey(width, height, compression=7, rows=height, counts=(len(stream),))
        if finding == "SEC-04":
            # The same scan again and again: refused as refining nothing from the second, or by MaxJpegScans.
            write(name, build(entries, payload=stream), finding, f"{width} x {height} progressive JPEG with {scans} AC scans of a few bytes",
                  outcome=SCAN_AMPLIFICATION)
        else:
            # 256 MB of pixels and 512 MB of coefficients: within the defaults, beyond a device that allows 64 MB.
            write(name, build(entries, payload=stream), finding, f"{width} x {height} progressive JPEG: 512 MB of coefficients",
                  outcome="TiffException:MaxPixelBytes", options=SMALL_DEVICE)


# A progressive JPEG made of thousands of scans: refused by MaxJpegScans, or before as soon as a scan adds no bit (a damaged file).
SCAN_AMPLIFICATION = "TiffException:MaxJpegScans|TiffException"

# The files of the audit (tests/Fixtures/hostile/review), measured on the first implementation; kept as they are. The fourth
# value says whether a small device's options apply: the bombs of the audit declare 2 147 024 896 bytes, just below the default
# MaxPixelBytes, so they are refused only with a lower limit.
REVIEW_CASES = {
    "bomb-deflate-2G.tif": ("SEC-05", "Deflate strips sharing one stream, 2 GB declared", "TiffException:MaxPixelBytes", True),
    "bomb-deflate-2G-botright.tif": ("SEC-05", "same, Orientation = 3 (a mirror pass on 2 GB)", "TiffException:MaxPixelBytes", True),
    "bomb-deflate-fp3-2G.tif": ("SEC-05", "same, floating point predictor", "TiffException:MaxPixelBytes", True),
    "bomb-deflate-pred2-2G.tif": ("SEC-05", "same, horizontal predictor", "TiffException:MaxPixelBytes", True),
    "bomb-jpeg-2G.tif": ("SEC-05", "JPEG strips sharing one stream, 2 GB declared", "TiffException:MaxPixelBytes", True),
    "bomb-lzw-2G.tif": ("SEC-05", "LZW strips sharing one stream", "TiffException:MaxPixelBytes", True),
    "bomb-packbits-2G.tif": ("SEC-05", "PackBits strips sharing one stream", "TiffException:MaxPixelBytes", True),
    "dos-512k-tiles.tif": ("SEC-06", "524288 Deflate tiles sharing one stream", "TiffException:MaxPixelBytes", True),
    "dos-bigtiff-unknowntags-32k.tif": ("SEC-03", "BigTIFF with 32000 unknown tags", "TiffException:MaxTagsPerDirectory", False),
    "dos-bytecount-2G-deflate.tif": ("SEC-02", "StripByteCounts of 2 GB in 124 bytes", "TiffException", False),
    "dos-nstrips-268M.tif": ("SEC-01", "268 million strips implied by the geometry, one offset", "TiffException", False),
    "dos-nstrips-2G.tif": ("SEC-01", "2^31 strips implied by the geometry, one offset", "TiffException", False),
    "dos-unknowntags-20k.tif": ("SEC-03", "20000 unknown tags", "TiffException:MaxTagsPerDirectory", False),
    "dos-unknowntags-32k.tif": ("SEC-03", "32000 unknown tags", "TiffException:MaxTagsPerDirectory", False),
    "fuzz-cmyka-dircount-5s.tif": ("SEC-03", "a CMYK file whose directory count was mutated (5.4 s on the first implementation)",
                                   "TiffException:*|NotSupportedException", False),
    "fuzz-worker-fatal-oom-not-reproducible.tif": ("SEC-05", "fuzzing input of an out-of-memory crash that did not reproduce alone",
                                                   "TiffException:*|NotSupportedException", False),
    "jpeg-prog-16k-coefbuf.tif": ("SEC-05", "16k x 16k progressive JPEG: 512 MB of coefficients", "TiffException:MaxPixelBytes", True),
    "jpeg-prog-46k-coefbuf.tif": ("SEC-05", "46k x 46k progressive JPEG: 8 GB of coefficients", "TiffException:MaxPixelBytes", False),
    "jpeg-prog-4k-10000scans.tif": ("SEC-04", "progressive JPEG with 10000 AC scans (35 s on the first implementation)",
                                    SCAN_AMPLIFICATION, False),
    "jpeg-prog-4k-1000scans.tif": ("SEC-04", "progressive JPEG with 1000 AC scans", SCAN_AMPLIFICATION, False),
}


def main() -> int:
    OUT.mkdir(parents=True, exist_ok=True)
    for old in OUT.glob("*.tif"):
        old.unlink()
    strip_and_tile_tables(); byte_counts(); unknown_tags(); many_tiles(); decompression_bombs(); progressive_jpeg()

    manifest_path = FOLDER / "manifest.json"
    manifest = json.loads(manifest_path.read_text(encoding="utf-8")) if manifest_path.exists() else {}
    manifest["crafted"] = dict(sorted(cases.items()))
    manifest["review"] = {name: {"finding": finding, "attack": attack, "outcome": outcome,
                                 "options": SMALL_DEVICE if small else {}, "maxSeconds": 2.0, "maxAllocatedMB": 64}
                          for name, (finding, attack, outcome, small) in sorted(REVIEW_CASES.items())}
    manifest_path.write_text(json.dumps(manifest, indent=1) + "\n", encoding="utf-8", newline="\n")
    total = sum((OUT / n).stat().st_size for n in cases)
    print(f"{len(cases)} files, {total // 1024} KB in {OUT.relative_to(ROOT)}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
