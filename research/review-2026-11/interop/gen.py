import sys, numpy as np, tifffile, itertools
d=sys.argv[1]
try:
    import imagecodecs; print('imagecodecs', imagecodecs.__version__)
    comps=[None,'lzw','zlib','packbits','zstd','lzma']
except Exception as e:
    print('no imagecodecs', e); comps=[None]
rng=np.random.default_rng(1)
sums=[]
for dt in [np.uint8,np.int8,np.uint16,np.int16,np.uint32,np.int32,np.uint64,np.int64,np.float32,np.float64]:
  for ch in [1,3]:
    shape=(37,53,ch) if ch>1 else (37,53)
    if np.issubdtype(dt,np.floating): a=rng.standard_normal(shape).astype(dt)
    else:
        ii=np.iinfo(dt); a=rng.integers(ii.min,ii.max,size=shape,dtype=dt,endpoint=True)
    for comp,pred,tile,planar,big,bo in itertools.product(comps,[False,True],[None,(16,16)],[False,True],[False,True],['<','>']):
        if pred and comp not in ('lzw','zlib','zstd','lzma'): continue
        if planar and ch==1: continue
        if pred and dt in (np.uint64,np.int64): continue
        name=f"{np.dtype(dt).name}-{ch}-{comp}-p{int(pred)}-t{int(tile is not None)}-pl{int(planar)}-b{int(big)}-{'le' if bo=='<' else 'be'}.tif"
        kw=dict(photometric='rgb' if ch==3 else 'minisblack', compression=comp, bigtiff=big, byteorder=bo)
        if pred: kw['predictor']=True
        if tile: kw['tile']=tile
        if planar: kw['planarconfig']='separate'; data=np.moveaxis(a,-1,0)
        else: data=a
        try: tifffile.imwrite(f"{d}/{name}", data, **kw)
        except Exception as e: print('skip',name,e); continue
        sums.append((name, repr(float(a.astype(np.float64).sum()))))
open(f"{d}/expected.txt","w").write("\n".join(f"{n} {s}" for n,s in sums))
print(len(sums),'files')
