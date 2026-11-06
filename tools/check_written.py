"""Reads back the files the library wrote (the written corpus) with tifffile and Pillow.

The corpus and its expectations (expected.json: the SHA-256 of the samples of each file) are written by the test
WrittenCorpusTests when TIFFNET_WRITTEN_DIR is set:

    TIFFNET_WRITTEN_DIR=written dotnet test tests/Eleris.TiffNet.Tests --filter WrittenCorpus
    python -I tools/check_written.py written

Every file must give, through tifffile, exactly the samples the library wrote. Pillow (libtiff) must read the files of the types
it supports (8-bit grey and RGB, 16-bit grey, 32-bit float grey) with the same samples.
"""

import hashlib
import json
import sys
from pathlib import Path

import numpy as np
import tifffile
from PIL import Image


def sha(array: np.ndarray) -> str:
    little = np.ascontiguousarray(array.astype(array.dtype.newbyteorder("<")))
    return hashlib.sha256(little.tobytes()).hexdigest().upper()


def pillow_supports(name: str) -> bool:
    if name.startswith("large-"):
        return "-planar" not in name
    kind, channels = name.split("-")[0], name.split("-")[1]
    return (kind, channels) in {("uint8", "c1"), ("uint8", "c3"), ("uint16", "c1"), ("float32", "c1")} and "-pl1-" not in name


def main() -> int:
    folder = Path(sys.argv[1])
    expected = json.loads((folder / "expected.json").read_text(encoding="utf-8"))
    problems = []
    pillow = 0
    for name, digest in expected.items():
        path = folder / name
        array = tifffile.imread(path)
        if sha(array) != digest:
            problems.append(f"{name}: tifffile reads other samples")
            continue
        if pillow_supports(name):
            with Image.open(path) as im:
                read = np.asarray(im)
            if read.shape != array.shape or not np.array_equal(read, array):
                problems.append(f"{name}: Pillow reads other samples ({read.dtype} {read.shape})")
            pillow += 1
    for p in problems[:50]:
        print("error:", p)
    print(f"{len(expected)} written files read back by tifffile, {pillow} by Pillow; {len(problems)} problem(s).")
    return 1 if problems else 0


if __name__ == "__main__":
    sys.exit(main())
