import sys, tifffile, numpy as np
for f in sys.argv[1:]:
    with tifffile.TiffFile(f) as t:
        print("=====", f.split('/')[-1], "pages", len(t.pages), "bigtiff", t.is_bigtiff, "byteorder", t.byteorder)
        for i,p in enumerate(t.pages):
            print(" page", i, p.shape, p.dtype, p.photometric.name, "planar", p.planarconfig.name, "compr", p.compression.name, "pred", p.predictor, "tiled", p.is_tiled, "subifds", len(p.subifds or []))
            for tag in p.tags.values():
                if tag.code in (273,279,324,325): continue
                v = tag.value
                s = repr(v)
                if isinstance(v,(bytes,bytearray)): s = f"<{len(v)} bytes> " + repr(bytes(v[:60]))
                if len(s) > 400: s = s[:400] + f"... (len {len(s)})"
                print(f"   {tag.code} {tag.name}: {s}")
            a = p.asarray()
            print("   min/max", a.min(), a.max(), "nan", np.isnan(a).sum() if a.dtype.kind=='f' else '-')
