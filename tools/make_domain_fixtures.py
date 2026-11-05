"""Generates the domain fixtures: small TIFF files written by Siril, tifffile and Pillow for the cases the design names.

    pip install -r tools/requirements-interop.txt astropy pillow
    python -I tools/make_domain_fixtures.py [--siril path/to/siril-cli]

Unlike the interop corpus, these files are committed (tests/Fixtures/domain, each under 1 MB): Siril is not available in the
CI, and some writers stamp the date. The script also writes tests/Fixtures/domain/manifest.json, the oracle of the tests: for
each file, the SHA-256 of the file (to detect an accidental change) and what a reader must find in each page, as tifffile reads
it (geometry, sample type, photometric interpretation, extra channels, metadata, SHA-256 of the samples), plus the samples a
reader of this library must deliver when they differ from the stored ones (orientation applied, palette expanded, MinIsWhite
inverted).

--manifest-only rebuilds the manifest from the files as they are (keeping the expectations of the previous manifest).

Siril files are made only when siril-cli is found (default path of the Windows installer, or --siril); the other files are
always made. Run it again only to add cases: regenerated files differ in their dates, and the diff must be reviewed.
"""
from __future__ import annotations

import hashlib
import json
import shutil
import subprocess
import sys
import tempfile
from pathlib import Path

import numpy as np
import tifffile

ROOT = Path(__file__).resolve().parent.parent
OUT = ROOT / "tests" / "Fixtures" / "domain"
DEFAULT_SIRIL = Path(r"C:\Program Files\Siril\bin\siril-cli.exe")
MAX_BYTES = 1 << 20

rng = np.random.default_rng(7)
notes: dict[str, dict] = {}   # file name -> extra expectations of this library (delivered samples, primary page, refusal)


def sha(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest().upper()


def samples_sha(a: np.ndarray) -> str:
    return sha(np.ascontiguousarray(a.astype(a.dtype.newbyteorder("<"))).tobytes())


def save(name: str, data, note: dict | None = None, **kwargs) -> None:
    tifffile.imwrite(OUT / name, data, metadata=None, **kwargs)
    if note:
        notes[name] = note


# ---- Cases ------------------------------------------------------------------------------------------------------------------

def structure_cases() -> None:
    # A reduced-resolution thumbnail in IFD0 (DNG-like): the primary image is the second page.
    full = rng.integers(0, 65535, (120, 160, 3), dtype=np.uint16)
    thumb = rng.integers(0, 255, (12, 16, 3), dtype=np.uint8)
    with tifffile.TiffWriter(OUT / "thumbnail_in_ifd0.tif") as t:
        t.write(thumb, photometric="rgb", subfiletype=1, metadata=None)
        t.write(full, photometric="rgb", subfiletype=0, metadata=None)
    notes["thumbnail_in_ifd0.tif"] = {"primary": 1}

    g16 = rng.integers(0, 65535, (64, 48), dtype=np.uint16)
    with tifffile.TiffWriter(OUT / "multipage.tif") as t:
        t.write(g16, description="page 0", metadata=None)
        t.write(g16[::2, ::2], description="page 1, reduced resolution", subfiletype=1, metadata=None)
        t.write(g16 // 2, description="page 2", metadata=None)
    notes["multipage.tif"] = {"primary": 0}

    with tifffile.TiffWriter(OUT / "pyramid_subifds.tif") as t:
        t.write(g16, subifds=2, description="full resolution", metadata=None)
        t.write(g16[::2, ::2], subfiletype=1, metadata=None)
        t.write(g16[::4, ::4], subfiletype=1, metadata=None)
    notes["pyramid_subifds.tif"] = {"primary": 0, "subImages": 2}


def channel_cases() -> None:
    save("grey_3_samples.tif", rng.integers(0, 65535, (40, 30, 3), dtype=np.uint16), photometric="minisblack",
         planarconfig="contig", note={"colorModel": "grey"})
    save("grey_3_planes.tif", rng.integers(0, 65535, (3, 40, 30), dtype=np.uint16), photometric="minisblack",
         planarconfig="separate", note={"colorModel": "grey"})
    for kind in ("assocalpha", "unassalpha"):
        save(f"rgba_{kind}.tif", rng.integers(0, 65535, (40, 30, 4), dtype=np.uint16), photometric="rgb", extrasamples=[kind])
        save(f"grey_alpha_{kind}.tif", rng.integers(0, 255, (40, 30, 2), dtype=np.uint8), photometric="minisblack",
             planarconfig="contig", extrasamples=[kind])


def depth_cases() -> None:
    for bits in (1, 2, 4):
        save(f"grey_{bits}bit.tif", rng.integers(0, 2 ** bits, (37, 53), dtype=np.uint8), bitspersample=bits)
    for bits in (12, 14):
        save(f"grey_{bits}bit_packed.tif", rng.integers(0, 2 ** bits, (37, 53), dtype=np.uint16), bitspersample=bits)
    # 12-bit RGB: packed samples interleaved across channels.
    save("rgb_12bit_packed.tif", rng.integers(0, 4096, (21, 19, 3), dtype=np.uint16), photometric="rgb", bitspersample=12)

    white16 = rng.integers(0, 65535, (37, 53), dtype=np.uint16)
    save("miniswhite_16bit.tif", white16, photometric="miniswhite", note={"delivered": samples_sha(np.uint16(65535) - white16)})
    white1 = rng.integers(0, 2, (37, 53), dtype=np.uint8)
    save("miniswhite_1bit.tif", white1, photometric="miniswhite", bitspersample=1,
         note={"delivered": samples_sha(np.uint8(1) - white1)})

    palette = rng.integers(0, 65535, (3, 256), dtype=np.uint16)
    indices = rng.integers(0, 256, (37, 53), dtype=np.uint8)
    save("palette_8bit.tif", indices, photometric="palette", colormap=palette,
         note={"delivered": samples_sha(np.moveaxis(palette[:, indices], 0, -1)), "colorModel": "rgb"})

    save("float16.tif", (rng.integers(-2048, 2048, (37, 53)) * 0.125).astype(np.float16))
    save("complex64.tif", (rng.integers(0, 100, (8, 8)) + 1j).astype(np.complex64), note={"refused": "NotSupportedException"})


def orientation_cases() -> None:
    # 2 rows x 3 columns, stored values 1..6; the delivered image is what a viewer shows, top-down, left to right.
    stored = np.arange(1, 7, dtype=np.uint16).reshape(2, 3)
    # TIFF 6.0, Orientation: where the 0th row and the 0th column of the stored image are on the visual image.
    views = {
        1: stored,                  # top, left
        2: stored[:, ::-1],         # top, right
        3: stored[::-1, ::-1],      # bottom, right
        4: stored[::-1, :],         # bottom, left
        5: stored.T,                # left, top
        6: stored.T[:, ::-1],       # right, top (rotated 90 degrees clockwise)
        7: stored.T[::-1, ::-1],    # right, bottom
        8: stored.T[::-1, :],       # left, bottom (rotated 90 degrees counterclockwise)
    }
    for value, view in views.items():
        save(f"orientation_{value}.tif", stored, extratags=[(274, "H", 1, value, True)],
             note={"delivered": samples_sha(np.ascontiguousarray(view)), "deliveredShape": list(view.shape)})


def metadata_cases() -> None:
    from PIL import ImageCms
    srgb = ImageCms.ImageCmsProfile(ImageCms.createProfile("sRGB")).tobytes()
    xmp = b'<x:xmpmeta xmlns:x="adobe:ns:meta/"><rdf:RDF xmlns:rdf="http://www.w3.org/1999/02/22-rdf-syntax-ns#"/></x:xmpmeta>'
    g16 = rng.integers(0, 65535, (37, 53), dtype=np.uint16)
    save("metadata.tif", g16, description="OBJECT  = 'M42'", software="make_domain_fixtures", datetime="2026:11:05 12:00:00",
         resolution=(300, 300), resolutionunit="INCH", iccprofile=srgb,
         extratags=[(315, "s", 0, "An astronomer", True), (33432, "s", 0, "CC0", True), (700, "B", len(xmp), xmp, True),
                    (340, "H", 1, 0, True), (341, "H", 1, 65535, True), (42113, "s", 0, "0", True),
                    (65000, "B", 4, b"\x01\x02\x03\x04", True)])
    # Many tools write UTF-8 in ASCII tags; tifffile refuses non-ASCII text, so the bytes are written as they are.
    utf8 = "Nébuleuse d'Orion — M42 ✨".encode("utf-8")
    save("description_utf8.tif", g16[:8, :8], extratags=[(270, 2, len(utf8) + 1, utf8 + bytes(1), True)],
         note={"description": utf8.decode("utf-8")})


def pillow_cases() -> None:
    from PIL import Image, ImageCms
    srgb = ImageCms.ImageCmsProfile(ImageCms.createProfile("sRGB")).tobytes()
    rgb = Image.fromarray(rng.integers(0, 255, (48, 64, 3), dtype=np.uint8), "RGB")
    rgb.save(OUT / "pillow_rgb8_icc.tif", icc_profile=srgb, dpi=(300, 300), description="written by Pillow")
    rgb.save(OUT / "pillow_rgb8_deflate.tif", compression="tiff_adobe_deflate")
    rgb.save(OUT / "pillow_rgb8_jpeg.tif", compression="jpeg", quality=90)
    rgb.convert("L").save(OUT / "pillow_grey8_jpeg.tif", compression="jpeg", quality=90)
    rgb.convert("P").save(OUT / "pillow_palette.tif")
    Image.fromarray(rng.integers(0, 65535, (48, 64), dtype=np.uint16)).save(OUT / "pillow_grey16.tif")
    Image.fromarray((rng.integers(-1000, 1000, (48, 64)) / 8).astype(np.float32), "F").save(OUT / "pillow_float32.tif")


def siril_cases(siril: Path | None) -> None:
    if siril is None or not siril.exists():
        print("siril-cli not found: the Siril files are left as they are")
        return
    from astropy.io import fits
    work = Path(tempfile.mkdtemp(prefix="siril-"))
    try:
        # A small synthetic raw frame with a Bayer pattern and the keywords of an acquisition.
        y, x = np.indices((160, 240))
        star = 20000 * np.exp(-((y - 80) ** 2 + (x - 120) ** 2) / 400)
        frame = (rng.normal(1000, 30, (160, 240)) + star).clip(0, 65535).astype(np.uint16)
        header = fits.Header()
        for key, value in [("OBJECT", "M42"), ("EXPTIME", 30.0), ("DATE-OBS", "2026-11-05T21:00:00"), ("GAIN", 100),
                           ("BAYERPAT", "RGGB"), ("INSTRUME", "Synthetic camera")]:
            header[key] = value
        fits.PrimaryHDU(frame, header=header).writeto(work / "m42.fits")
        # Three different channels for the colour files (Siril 1.4 has no "debayer" command in scripts).
        for name, gain in (("r", 1.0), ("g", 0.8), ("b", 0.6)):
            fits.PrimaryHDU((frame * gain).astype(np.uint16), header=header).writeto(work / f"{name}.fits")
        script = "\n".join(["requires 1.2.0", "load m42",
                            "savetif m42_grey16", "savetif m42_grey16_astro -astro", "savetif m42_grey16_deflate -astro -deflate",
                            "savetif32 m42_grey32_astro -astro", "savetif8 m42_grey8",
                            "rgbcomp r g b -out=rgb", "load rgb",
                            "savetif m42_rgb16_astro -astro", "savetif32 m42_rgb32_astro -astro", "savetif8 m42_rgb8", ""])
        (work / "run.ssf").write_text(script, encoding="utf-8")
        run = subprocess.run([str(siril), "-d", str(work), "-s", str(work / "run.ssf")], capture_output=True, text=True)
        if run.returncode != 0:
            raise SystemExit(f"siril-cli failed:\n{run.stdout}{run.stderr}")
        for tif in sorted(work.glob("m42_*.tif")):
            shutil.copy(tif, OUT / f"siril_{tif.name}")
            notes[f"siril_{tif.name}"] = {"writer": "Siril 1.4.4"}
    finally:
        shutil.rmtree(work, ignore_errors=True)


# ---- Manifest -----------------------------------------------------------------------------------------------------------------

def describe(path: Path) -> dict:
    entry: dict = {"fileSha256": sha(path.read_bytes()), "bytes": path.stat().st_size}
    with tifffile.TiffFile(path) as tif:
        entry["bigEndian"] = tif.byteorder == ">"
        pages = []
        for page in tif.pages:
            tags = page.tags
            info = {
                "width": page.imagewidth, "height": page.imagelength, "samplesPerPixel": page.samplesperpixel,
                "bitsPerSample": page.bitspersample, "sampleFormat": int(page.sampleformat),
                "photometric": int(page.photometric), "compression": int(page.compression),
                "planar": int(page.planarconfig) == 2, "subfileType": int(page.subfiletype),
                "extraSamples": [int(x) for x in page.extrasamples],
                "orientation": int(tags[274].value) if 274 in tags else 1,
                "subIfds": len(tags[330].value) if 330 in tags else 0,
            }
            if 270 in tags:
                info["description"] = tags[270].value
            for code, key in [(305, "software"), (306, "dateTime"), (315, "artist"), (33432, "copyright")]:
                if code in tags:
                    info[key] = tags[code].value
            for code, key in [(34675, "iccBytes"), (700, "xmpBytes")]:
                if code in tags:
                    info[key] = len(tags[code].value)
            info["tagCodes"] = sorted(int(t.code) for t in tags)
            try:
                data = page.asarray()
                if info["planar"] and data.ndim == 3:
                    data = np.moveaxis(data, 0, -1)
                info["storedSamplesSha256"] = samples_sha(data)
                # What this library delivers when it differs from what is stored: palettes expanded to RGB 16-bit, MinIsWhite
                # inverted (max of the stored depth minus the value).
                if info["photometric"] == 3:
                    colormap = page.colormap.astype(np.uint16)
                    info["deliveredSamplesSha256"] = samples_sha(np.moveaxis(colormap[:, data], 0, -1))
                elif info["photometric"] == 0 and data.dtype.kind in "ui":
                    info["deliveredSamplesSha256"] = samples_sha((2 ** info["bitsPerSample"] - 1 - data.astype(np.int64)).astype(data.dtype))
            except Exception as e:   # tifffile cannot decode it (for example JPEG without imagecodecs support)
                info["storedSamplesSha256"] = None
                info["tifffileError"] = type(e).__name__
            pages.append(info)
        entry["pages"] = pages
    return entry


def main() -> int:
    siril = DEFAULT_SIRIL
    if "--siril" in sys.argv:
        siril = Path(sys.argv[sys.argv.index("--siril") + 1])
    OUT.mkdir(parents=True, exist_ok=True)
    if "--manifest-only" not in sys.argv:
        structure_cases(); channel_cases(); depth_cases(); orientation_cases(); metadata_cases(); pillow_cases(); siril_cases(siril)

    manifest_path = OUT / "manifest.json"
    previous = json.loads(manifest_path.read_text(encoding="utf-8"))["files"] if manifest_path.exists() else {}
    files = {}
    for path in sorted(OUT.glob("*.tif")):
        if path.stat().st_size > MAX_BYTES:
            raise SystemExit(f"{path.name} is larger than 1 MB")
        entry = describe(path)
        # Expectations of this library: from this run, or kept from the previous manifest for files not regenerated (Siril).
        expect = notes.get(path.name) or previous.get(path.name, {}).get("expect")
        if expect:
            entry["expect"] = expect
        files[path.name] = entry
    manifest_path.write_text(json.dumps({"generator": "tools/make_domain_fixtures.py", "files": files}, indent=1, ensure_ascii=False) + "\n",
                             encoding="utf-8", newline="\n")
    print(f"{len(files)} files in {OUT.relative_to(ROOT)}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
