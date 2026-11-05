import sys, glob, os, tifffile, numpy as np
from PIL import Image
for f in sorted(glob.glob(sys.argv[1] + "/*.tif")):
    n = os.path.basename(f)
    try:
        with tifffile.TiffFile(f) as t:
            p = t.pages[0]; a = p.asarray()
            es = p.tags.get(338); es = es.value if es else None
            tt = f"tifffile OK {a.shape} {a.dtype} {p.photometric.name} comp={p.compression.name} pred={p.predictor} rps={p.rowsperstrip} extrasamples={es} sw={p.software!r} desc={p.description!r}"
    except Exception as e: tt = f"tifffile FAIL {e}"
    try:
        im = Image.open(f); im.load(); pp = f"PIL OK mode={im.mode}"
    except Exception as e: pp = f"PIL FAIL {type(e).__name__}: {str(e)[:60]}"
    print(f"{n}: {tt} | {pp}")
