import sys, numpy as np, tifffile
d=sys.argv[1]
full=np.full((300,400,3),1000,np.uint16); thumb=np.full((30,40,3),7,np.uint8)
with tifffile.TiffWriter(d+"/dng_like.tif") as t:
    t.write(thumb, photometric='rgb', subfiletype=1, subifds=1)
    t.write(full, photometric='rgb', subfiletype=0)
