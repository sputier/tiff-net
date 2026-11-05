import sys, glob, os, hashlib, numpy as np, tifffile
d=sys.argv[1]
with open(d+'/hashes.txt','w') as o:
    for f in sorted(glob.glob(d+'/*.tif')):
        a=tifffile.imread(f)
        if 'pl1' in f: a=np.moveaxis(a,0,-1)
        a=np.ascontiguousarray(a.astype(a.dtype.newbyteorder('<')))
        o.write(os.path.basename(f)+' '+hashlib.sha256(a.tobytes()).hexdigest().upper()+'\n')
