import tifffile,sys,numpy as np
from astropy.io import fits
d=sys.argv[1]
f=fits.getdata(d+'/m42.fits').astype(np.float64)
t=tifffile.imread(d+'/m42_16_astro.tif').astype(np.float64)
t32=tifffile.imread(d+'/m42_32.tif').astype(np.float64)
print('same', np.array_equal(f,t), 'flipud', np.array_equal(f[::-1],t))
print('float = u16/65535 ?', np.allclose(t32, t/65535, atol=1e-7), 't32 max', t32.max(), 'u16 max', t.max())
print(fits.getheader(d+'/m42.fits').get('ROWORDER'))
