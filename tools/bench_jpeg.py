"""Writes the JPEG inputs of the decoder benchmarks (benchmarks/data, not committed) and times libjpeg-turbo on them.

The images are 4000 x 3000 (12 MP), quality 90, of a smooth picture with noise: grey, YCbCr 4:4:4 and 4:2:0, sequential and
progressive. libjpeg-turbo is timed through Pillow, on one thread, decoding to the components (draft) and to RGB:

    python -I tools/bench_jpeg.py
    dotnet run -c Release --project benchmarks/Eleris.TiffNet.Benchmarks -- --filter "*Jpeg*"
"""

import io
import time
from pathlib import Path

import numpy as np
from PIL import Image, features

FOLDER = Path(__file__).resolve().parent.parent / "benchmarks" / "data"
WIDTH, HEIGHT = 4000, 3000


def picture(channels: int) -> Image.Image:
    rng = np.random.default_rng(7)
    y, x = np.mgrid[0:HEIGHT, 0:WIDTH].astype(np.float32)
    planes = [np.clip(128 + 80 * np.sin(x / (40 + 9 * c)) * np.cos(y / 31) + rng.normal(0, 12, x.shape), 0, 255) for c in range(channels)]
    data = np.stack(planes, axis=-1).astype(np.uint8)
    return Image.fromarray(data[..., 0] if channels == 1 else data, "L" if channels == 1 else "RGB")


CASES = {
    "grey": (1, {}),
    "ycc444": (3, {"subsampling": 0}),
    "ycc420": (3, {"subsampling": 2}),
    "prog420": (3, {"subsampling": 2, "progressive": True}),
}


def best(fn, runs=7) -> float:
    times = []
    for _ in range(runs):
        start = time.perf_counter()
        fn()
        times.append(time.perf_counter() - start)
    return min(times) * 1000


def main() -> None:
    FOLDER.mkdir(parents=True, exist_ok=True)
    print(f"libjpeg-turbo {features.version('jpg')} through Pillow {Image.__version__}, one thread, best of 7 (ms):")
    for name, (channels, options) in CASES.items():
        buffer = io.BytesIO()
        picture(channels).save(buffer, "JPEG", quality=90, **options)
        data = buffer.getvalue()
        (FOLDER / f"{name}.jpg").write_bytes(data)

        def components():
            im = Image.open(io.BytesIO(data))
            if channels == 3:
                im.draft("YCbCr", im.size)
            im.load()

        def rgb():
            Image.open(io.BytesIO(data)).load()

        print(f"  {name:8} {len(data) / 1e6:5.1f} MB   components {best(components):7.1f}   image {best(rgb):7.1f}")


if __name__ == "__main__":
    main()
