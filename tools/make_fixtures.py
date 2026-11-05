"""Generates the interoperability corpus: TIFF files written by tifffile, the independent oracle of the tests.

    pip install -r tools/requirements-interop.txt
    python -I tools/make_fixtures.py [output folder] [--update]

The files are written to the output folder (default: tests/Fixtures/interop/generated, ignored by git). Each one is then read
back with tifffile, compared with the samples it was written from, and the SHA-256 of its samples (interleaved, rows from the
top, little endian) is compared with tests/Fixtures/interop/manifest.json, which is committed: a difference means that the
generator or tifffile changed, and the script fails. --update rewrites the manifest instead (review the diff before committing).

The samples come from a seeded generator and are built from integers only (floating point values are integers times a power of
two), so they do not depend on the floating point functions of the platform: the hashes are the same on every machine.
"""
from __future__ import annotations

import hashlib
import itertools
import json
import sys
import zlib
from pathlib import Path

import imagecodecs
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
# Kind of the extra channel (ExtraSamples): unspecified data, associated (premultiplied) alpha, unassociated alpha.
EXTRA_KINDS = {"u": "unspecified", "a": "assocalpha", "s": "unassalpha"}
# Mantissa bits used by the floating point samples, so that the integer-to-float conversion is exact.
FLOAT_BITS = {np.float16: (11, 4), np.float32: (24, 8), np.float64: (53, 20)}   # (integer bits, fraction bits)


def samples(dtype, channels: int, seed: int) -> np.ndarray:
    rng = np.random.default_rng(seed)
    shape = (HEIGHT, WIDTH, channels) if channels > 1 else (HEIGHT, WIDTH)
    if np.issubdtype(dtype, np.floating):
        bits, fraction = FLOAT_BITS[dtype]
        integers = rng.integers(-(2 ** (bits - 1)), 2 ** (bits - 1), size=shape, dtype=np.int64)
        data = (integers.astype(np.float64) * 2.0 ** -fraction).astype(dtype)   # exact: powers of two only
        flat = data.reshape(-1)
        # The values a reader must keep exactly: NaN, infinities, signed zero, the smallest subnormal.
        flat[:5] = [np.nan, np.inf, -np.inf, -0.0, np.finfo(dtype).smallest_subnormal]
        return data
    info = np.iinfo(dtype)
    return rng.integers(info.min, info.max, size=shape, dtype=dtype, endpoint=True)


def sample_hash(data: np.ndarray) -> str:
    little = np.ascontiguousarray(data.astype(data.dtype.newbyteorder("<")))
    return hashlib.sha256(little.tobytes()).hexdigest().upper()


def same_bits(a: np.ndarray, b: np.ndarray) -> bool:
    """Equal bit for bit, NaN payloads and signed zeros included."""
    a = np.ascontiguousarray(a.astype(a.dtype.newbyteorder("<")))
    b = np.ascontiguousarray(b.astype(b.dtype.newbyteorder("<")))
    return a.shape == b.shape and a.tobytes() == b.tobytes()


def dataset_of(dtype, channels) -> str:
    return f"{np.dtype(dtype).name}-c{channels}"


def name_of(dtype, channels, extra, compression, predictor, tiled, planar, bigtiff, order) -> str:
    return (f"{np.dtype(dtype).name}-c{channels}{extra}-{compression or 'none'}-p{int(predictor)}-t{int(tiled)}"
            f"-pl{int(planar)}-b{int(bigtiff)}-{'le' if order == '<' else 'be'}.tif")


def cases():
    """(dtype, channels, extra kind, compression, predictor, tiled, planar, bigtiff, byte order) of every file."""
    for dtype, channels in itertools.product(DTYPES, CHANNELS):
        extra = "u" if channels in (2, 4) else ""
        for compression, predictor, tiled, planar, bigtiff, order in itertools.product(
                COMPRESSIONS, [False, True], [False, True], [False, True], [False, True], ["<", ">"]):
            if predictor and compression not in ("lzw", "zlib", "zstd", "lzma"):
                continue
            if planar and channels == 1:
                continue
            if predictor and dtype in (np.uint64, np.int64):
                # tifffile does not write predictor 2 on 64-bit integers: those files are encoded here (see write_encoded),
                # for the codecs this library reads, in strips.
                if compression not in ("lzw", "zlib") or tiled:
                    continue
            yield dtype, channels, extra, compression, predictor, tiled, planar, bigtiff, order
        # The other kinds of extra channel, in a few layouts: the kind is metadata, the samples are the same.
        if channels in (2, 4):
            for extra, compression, order in itertools.product("as", [None, "zlib"], ["<", ">"]):
                yield dtype, channels, extra, compression, False, False, False, False, order


def horizontal_differences(block: np.ndarray, planar: bool) -> np.ndarray:
    """TIFF predictor 2 on a block of rows: each sample minus the previous one of the same channel, modulo 2^bits."""
    unsigned = block.view(block.dtype.str.replace("i", "u"))
    diff = unsigned.copy()
    axis = -1 if planar or block.ndim == 2 else -2
    if axis == -1:
        diff[..., 1:] = unsigned[..., 1:] - unsigned[..., :-1]
    else:
        diff[:, 1:, :] = unsigned[:, 1:, :] - unsigned[:, :-1, :]
    return diff.view(block.dtype)


def encoded_strips(data: np.ndarray, planar: bool, compression: str, order: str):
    """The strips of 'data' with predictor 2, compressed, in file order (plane after plane when planar)."""
    planes = [np.moveaxis(data, -1, 0)[c] for c in range(data.shape[-1])] if planar else [data]
    for plane in planes:
        for y in range(0, HEIGHT, ROWS_PER_STRIP):
            block = horizontal_differences(plane[y:y + ROWS_PER_STRIP], planar)
            raw = np.ascontiguousarray(block.astype(block.dtype.newbyteorder(order))).tobytes()
            yield zlib.compress(raw) if compression == "zlib" else imagecodecs.lzw_encode(raw)


def write_file(path: Path, data: np.ndarray, channels, extra, compression, predictor, tiled, planar, bigtiff, order) -> None:
    rgb = channels >= 3
    kwargs = dict(photometric="rgb" if rgb else "minisblack", bigtiff=bigtiff, byteorder=order, metadata=None)
    if extra:
        kwargs["extrasamples"] = [EXTRA_KINDS[extra]]
    if channels > 1:
        kwargs["planarconfig"] = "separate" if planar else "contig"
    stored = np.moveaxis(data, -1, 0) if planar else data
    if predictor and data.dtype in (np.uint64, np.int64):
        with tifffile.TiffWriter(path, bigtiff=bigtiff, byteorder=order) as writer:
            del kwargs["bigtiff"], kwargs["byteorder"]
            writer.write(encoded_strips(data, planar, compression, order), shape=stored.shape, dtype=data.dtype,
                         compression=compression, predictor=2, rowsperstrip=ROWS_PER_STRIP, **kwargs)
        return
    kwargs["compression"] = compression
    if predictor:
        kwargs["predictor"] = True
    if tiled:
        kwargs["tile"] = TILE
    else:
        kwargs["rowsperstrip"] = ROWS_PER_STRIP
    tifffile.imwrite(path, stored, **kwargs)


def write(folder: Path) -> list[dict]:
    folder.mkdir(parents=True, exist_ok=True)
    all_cases = list(cases())
    names = [name_of(*case) for case in all_cases]
    # Only the files of the corpus are replaced: the folder may be given on the command line, other files are left alone.
    expected_names = set(names)
    for stale in folder.glob("*.tif"):
        if stale.name in expected_names or _looks_generated(stale.name):
            stale.unlink()
    datasets: dict[str, np.ndarray] = {}
    entries = []
    for (dtype, channels, extra, compression, predictor, tiled, planar, bigtiff, order), name in zip(all_cases, names):
        key = dataset_of(dtype, channels)
        if key not in datasets:
            datasets[key] = samples(dtype, channels, seed=1000 + DTYPES.index(dtype) * 10 + channels)
        data = datasets[key]
        write_file(folder / name, data, channels, extra, compression, predictor, tiled, planar, bigtiff, order)

        # The oracle is what tifffile reads back; it must be what was written.
        read = tifffile.imread(folder / name)
        if planar:
            read = np.moveaxis(read, 0, -1)
        if not same_bits(read, data):
            raise SystemExit(f"error: tifffile does not read back what was written in {name}")
        entries.append({"file": name, "dataset": key, "samplesSha256": sample_hash(read)})
    return entries


def _looks_generated(name: str) -> bool:
    return any(name.startswith(np.dtype(d).name + "-c") for d in DTYPES)


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
