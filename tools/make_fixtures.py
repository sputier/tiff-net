"""Generates the interoperability corpus: TIFF files written by tifffile, the independent oracle of the tests.

    pip install -r tools/requirements-interop.txt
    python -I tools/make_fixtures.py [output folder] [--update]

The files are written to the output folder (default: tests/Fixtures/interop/generated, ignored by git). Each one is then read
back with tifffile and the SHA-256 of its samples (interleaved, rows from the top, little endian) is compared with
tests/Fixtures/interop/manifest.json, which is committed: a difference means that the generator or tifffile changed, and the
script fails. --update rewrites the manifest instead (review the diff before committing it).

The data comes from a seeded generator, so the samples, and their hashes, are the same on every machine.
"""
from __future__ import annotations

import hashlib
import itertools
import json
import sys
from pathlib import Path

import numpy as np
import tifffile

ROOT = Path(__file__).resolve().parent.parent
DEFAULT_OUTPUT = ROOT / "tests" / "Fixtures" / "interop" / "generated"
MANIFEST = ROOT / "tests" / "Fixtures" / "interop" / "manifest.json"

WIDTH, HEIGHT = 53, 37   # odd sizes: edge tiles and strips are partial
TILE = (16, 16)
ROWS_PER_STRIP = 8       # several strips per image

DTYPES = [np.uint8, np.int8, np.uint16, np.int16, np.uint32, np.int32, np.uint64, np.int64,
          np.float16, np.float32, np.float64]
CHANNELS = [1, 2, 3, 4]  # 2 = grey + extra channel, 4 = RGB + extra channel
# None, then the codecs this library reads, then two it refuses by design (the tests expect NotSupportedException).
COMPRESSIONS = [None, "packbits", "lzw", "zlib", "zstd", "lzma"]
SUPPORTED_COMPRESSIONS = {None, "packbits", "lzw", "zlib"}


def samples(dtype, channels: int, seed: int) -> np.ndarray:
    rng = np.random.default_rng(seed)
    shape = (HEIGHT, WIDTH, channels) if channels > 1 else (HEIGHT, WIDTH)
    if np.issubdtype(dtype, np.floating):
        data = (rng.standard_normal(shape) * 1000).astype(dtype)
        flat = data.reshape(-1)
        # The values a reader must keep exactly: NaN, infinities, signed zero, the smallest subnormal.
        flat[:5] = [np.nan, np.inf, -np.inf, -0.0, np.finfo(dtype).smallest_subnormal]
        return data
    info = np.iinfo(dtype)
    return rng.integers(info.min, info.max, size=shape, dtype=dtype, endpoint=True)


def sample_hash(data: np.ndarray) -> str:
    little = np.ascontiguousarray(data.astype(data.dtype.newbyteorder("<")))
    return hashlib.sha256(little.tobytes()).hexdigest().upper()


def cases():
    for d_index, dtype in enumerate(DTYPES):
        for channels in CHANNELS:
            data = samples(dtype, channels, seed=1000 + d_index * 10 + channels)
            for compression, predictor, tiled, planar, bigtiff, order in itertools.product(
                    COMPRESSIONS, [False, True], [False, True], [False, True], [False, True], ["<", ">"]):
                if predictor and compression not in ("lzw", "zlib", "zstd", "lzma"):
                    continue
                if predictor and dtype in (np.uint64, np.int64):
                    continue   # TIFF defines no horizontal predictor gain for 64-bit integers; tifffile refuses it
                if planar and channels == 1:
                    continue
                yield dtype, channels, data, compression, predictor, tiled, planar, bigtiff, order


def dataset_of(dtype, channels) -> str:
    return f"{np.dtype(dtype).name}-c{channels}"


def name_of(dtype, channels, compression, predictor, tiled, planar, bigtiff, order) -> str:
    return (f"{np.dtype(dtype).name}-c{channels}-{compression or 'none'}-p{int(predictor)}-t{int(tiled)}"
            f"-pl{int(planar)}-b{int(bigtiff)}-{'le' if order == '<' else 'be'}.tif")


def write(folder: Path) -> list[dict]:
    folder.mkdir(parents=True, exist_ok=True)
    for stale in folder.glob("*.tif"):
        stale.unlink()
    entries = []
    for dtype, channels, data, compression, predictor, tiled, planar, bigtiff, order in cases():
        name = name_of(dtype, channels, compression, predictor, tiled, planar, bigtiff, order)
        rgb = channels >= 3
        kwargs = dict(photometric="rgb" if rgb else "minisblack", compression=compression, bigtiff=bigtiff, byteorder=order,
                      metadata=None)
        if channels in (2, 4):
            kwargs["extrasamples"] = ["unspecified"]
        if predictor:
            kwargs["predictor"] = True
        if tiled:
            kwargs["tile"] = TILE
        else:
            kwargs["rowsperstrip"] = ROWS_PER_STRIP
        stored = data
        if planar:
            kwargs["planarconfig"] = "separate"
            stored = np.moveaxis(data, -1, 0)
        elif channels > 1:
            kwargs["planarconfig"] = "contig"
        tifffile.imwrite(folder / name, stored, **kwargs)

        # The oracle is what tifffile reads back, not what was given to it.
        read = tifffile.imread(folder / name)
        if planar:
            read = np.moveaxis(read, 0, -1)
        entries.append({"file": name, "dataset": dataset_of(dtype, channels), "samplesSha256": sample_hash(read)})
    return entries


def main() -> int:
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    update = "--update" in sys.argv
    folder = Path(args[0]) if args else DEFAULT_OUTPUT
    entries = write(folder)

    # Every storage variant of a dataset (type x channels) holds the same samples: the manifest keeps one hash per dataset and
    # the list of files, whose names encode their parameters (see name_of; the tests parse them).
    datasets: dict[str, str] = {}
    for e in entries:
        known = datasets.setdefault(e["dataset"], e["samplesSha256"])
        if known != e["samplesSha256"]:
            print(f"error: {e['file']}: tifffile reads other samples than the rest of {e['dataset']}")
            return 1
    document = {
        "generator": "tools/make_fixtures.py",
        "tifffile": tifffile.__version__,
        "numpy": np.__version__,
        "width": WIDTH,
        "height": HEIGHT,
        "supportedCompressions": sorted(c or "none" for c in SUPPORTED_COMPRESSIONS),
        "datasets": dict(sorted(datasets.items())),
        "files": sorted(e["file"] for e in entries),
    }
    if update or not MANIFEST.exists():
        MANIFEST.parent.mkdir(parents=True, exist_ok=True)
        MANIFEST.write_text(json.dumps(document, indent=1) + "\n", encoding="utf-8", newline="\n")
        print(f"{len(entries)} files written to {folder}; manifest updated: {MANIFEST.relative_to(ROOT)}")
        return 0

    expected = json.loads(MANIFEST.read_text(encoding="utf-8"))
    problems = []
    for key in ("width", "height", "supportedCompressions", "datasets"):
        if expected.get(key) != document[key]:
            problems.append(f"{key} differs from the manifest")
    problems += [f"{name}: missing from the generated corpus" for name in sorted(set(expected["files"]) - set(document["files"]))]
    problems += [f"{name}: not in the manifest" for name in sorted(set(document["files"]) - set(expected["files"]))]
    for p in problems[:50]:
        print("error:", p)
    print(f"{len(entries)} files written to {folder}; {len(problems)} difference(s) with the manifest")
    return 1 if problems else 0


if __name__ == "__main__":
    sys.exit(main())
