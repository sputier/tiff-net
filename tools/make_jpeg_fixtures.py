"""Generates the JPEG fixtures of the decoder tests (tests/Fixtures/jpeg) and their manifest, with Pillow (libjpeg-turbo).

The expected samples are those libjpeg-turbo decodes, before any colour conversion: the grey plane of a grey image, and the
Y, Cb and Cr planes Pillow gives with draft("YCbCr") (no upsampling when chroma is not subsampled). The integer "islow" IDCT,
libjpeg's default, is what the library reproduces bit for bit. With subsampled chroma only the Y plane is compared here: the
upsampling of chroma is tested with the colour conversion.

    python -I tools/make_jpeg_fixtures.py            # writes the files and the manifest
    python -I tools/make_jpeg_fixtures.py --check    # checks that the committed files decode as the manifest says
"""

import hashlib
import io
import json
import sys
from pathlib import Path

import numpy as np
from PIL import Image, ImageFile, features

FOLDER = Path(__file__).resolve().parent.parent / "tests" / "Fixtures" / "jpeg"


def picture(width, height, channels, seed):
    """A smooth picture with edges and noise: every kind of block (flat, textured, saturated)."""
    rng = np.random.default_rng(seed)
    y, x = np.mgrid[0:height, 0:width]
    base = 128 + 100 * np.sin(x / 7.0) * np.cos(y / 5.0)
    base[(x // 13 + y // 11) % 3 == 0] = 255 * ((x[(x // 13 + y // 11) % 3 == 0] // 4) % 2)   # hard edges, saturated blocks
    planes = [np.clip(base + rng.normal(0, 20 + 15 * c, base.shape) + 40 * c, 0, 255) for c in range(channels)]
    data = np.stack(planes, axis=-1).astype(np.uint8)
    return Image.fromarray(data[..., 0] if channels == 1 else data, "L" if channels == 1 else "RGB")


# name, width, height, channels, save options
CASES = [
    ("grey_q90_37x29", 37, 29, 1, {"quality": 90}),
    ("grey_q100_64x64", 64, 64, 1, {"quality": 100}),
    ("grey_q20_100x75", 100, 75, 1, {"quality": 20}),
    ("grey_q75_restart_5", 61, 47, 1, {"quality": 75, "restart_marker_blocks": 5}),
    ("grey_q50_1x1", 1, 1, 1, {"quality": 50}),
    ("grey_qtable16", 40, 24, 1, {"qtables": [[1000 if k % 7 == 0 else 3 for k in range(64)]]}),
    ("ycc444_q90_37x29", 37, 29, 3, {"quality": 90, "subsampling": 0}),
    ("ycc444_q100_48x40", 48, 40, 3, {"quality": 100, "subsampling": 0}),
    ("ycc444_q75_restart_2", 53, 31, 3, {"quality": 75, "subsampling": 0, "restart_marker_blocks": 2}),
    ("ycc422_q85_45x33", 45, 33, 3, {"quality": 85, "subsampling": 1}),
    ("ycc420_q85_45x33", 45, 33, 3, {"quality": 85, "subsampling": 2}),
    ("ycc420_q60_restart_3", 70, 50, 3, {"quality": 60, "subsampling": 2, "restart_marker_rows": 1}),
    ("ycc420_q95_256x192", 256, 192, 3, {"quality": 95, "subsampling": 2}),
    # Progressive (libjpeg's default script: DC with successive approximation, AC bands, refinements), with optimised tables.
    ("prog_grey_q90_61x47", 61, 47, 1, {"quality": 90, "progressive": True}),
    ("prog_grey_q100_64x64", 64, 64, 1, {"quality": 100, "progressive": True, "optimize": True}),
    ("prog_ycc444_q85_53x31", 53, 31, 3, {"quality": 85, "subsampling": 0, "progressive": True}),
    ("prog_ycc420_q75_70x50", 70, 50, 3, {"quality": 75, "subsampling": 2, "progressive": True, "optimize": True}),
    ("prog_ycc422_q90_45x33", 45, 33, 3, {"quality": 90, "subsampling": 1, "progressive": True}),
    ("prog_ycc420_restart_256x192", 256, 192, 3, {"quality": 92, "subsampling": 2, "progressive": True, "restart_marker_rows": 1}),
]


# Files cut at two thirds of their length, decoded as libjpeg does: the MCU where the data runs out ends with zeros, the next
# ones (until a restart marker) are grey. Pillow feeds libjpeg the cut data with LOAD_TRUNCATED_IMAGES. The samples of the MCU
# where the data runs out are noise in any decoder (zero bits decoded as codes): the tests allow them to differ, so the
# expected planes are kept whole (<name>.planes) rather than as a hash.
# Progressive files are not among them: libjpeg smooths the blocks of an incomplete progressive image (block smoothing, an
# estimate of the missing coefficients), which the library does not do.
TRUNCATED = ["grey_q20_100x75", "grey_q100_64x64", "grey_q75_restart_5", "ycc444_q75_restart_2", "ycc420_q85_45x33"]


def sha(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest().upper()


def expectations(path: Path, channels: int, subsampled: bool) -> dict:
    with Image.open(path) as im:
        if channels == 1:
            return {"planes": [sha(np.asarray(im, dtype=np.uint8).tobytes())]}
        im.draft("YCbCr", im.size)
        ycc = np.asarray(im.convert("YCbCr") if im.mode != "YCbCr" else im, dtype=np.uint8)
        planes = [sha(np.ascontiguousarray(ycc[..., c]).tobytes()) for c in range(3)]
    with Image.open(path) as im:
        rgb = sha(np.asarray(im.convert("RGB"), dtype=np.uint8).tobytes())   # upsampled and converted by libjpeg-turbo
    return {"planes": planes[:1] if subsampled else planes, "rgb": rgb}


def main() -> int:
    check = "--check" in sys.argv
    FOLDER.mkdir(parents=True, exist_ok=True)
    manifest = {"generator": f"Pillow {Image.__version__}, libjpeg-turbo {features.version('jpg')}", "files": {}}
    for seed, (name, width, height, channels, options) in enumerate(CASES):
        path = FOLDER / f"{name}.jpg"
        if not check:
            buffer = io.BytesIO()
            picture(width, height, channels, seed).save(buffer, "JPEG", **options)
            path.write_bytes(buffer.getvalue())
        subsampled = channels == 3 and options.get("subsampling", 0) != 0
        sampling = {0: [[1, 1]] * 3, 1: [[2, 1], [1, 1], [1, 1]], 2: [[2, 2], [1, 1], [1, 1]]}[options.get("subsampling", 0)][:channels]
        manifest["files"][path.name] = {
            "width": width,
            "height": height,
            "components": channels,
            "sampling": sampling if channels == 3 else [[1, 1]],
            "restart": "restart_marker_blocks" in options or "restart_marker_rows" in options,
            "sha256": sha(path.read_bytes()),
            **expectations(path, channels, subsampled),
        }
    ImageFile.LOAD_TRUNCATED_IMAGES = True
    for name in TRUNCATED:
        source = FOLDER / f"{name}.jpg"
        path = FOLDER / f"{name}_cut.jpg"
        if not check:
            data = source.read_bytes()
            path.write_bytes(data[: len(data) * 2 // 3])
        entry = manifest["files"][source.name]
        subsampled = entry["sampling"][0] != [1, 1]
        manifest["files"][path.name] = {**entry, "sha256": sha(path.read_bytes()), "truncated": True,
                                        **expectations(path, entry["components"], subsampled)}
        if not check:
            with Image.open(path) as im:
                if entry["components"] == 3:
                    im.draft("YCbCr", im.size)
                ycc = np.asarray(im, dtype=np.uint8)
            planes = [ycc] if ycc.ndim == 2 else [np.ascontiguousarray(ycc[..., c]) for c in range(1 if subsampled else 3)]
            (FOLDER / f"{name}_cut.planes").write_bytes(b"".join(p.tobytes() for p in planes))
    ImageFile.LOAD_TRUNCATED_IMAGES = False
    manifest_path = FOLDER / "manifest.json"
    text = json.dumps(manifest, indent=2) + "\n"
    if check:
        committed = json.loads(manifest_path.read_text(encoding="utf-8"))
        if committed["files"] != manifest["files"]:
            print("The JPEG fixtures do not decode as their manifest says.", file=sys.stderr)
            return 1
        print(f"{len(CASES) + len(TRUNCATED)} JPEG fixtures checked.")
        return 0
    manifest_path.write_text(text, encoding="utf-8", newline="\n")
    print(f"{len(CASES) + len(TRUNCATED)} JPEG fixtures written to {FOLDER}.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
