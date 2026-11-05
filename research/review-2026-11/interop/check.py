import sys, glob, os, numpy as np, tifffile
d = sys.argv[1]
dt = dict(u8=np.uint8,i8=np.int8,u16=np.uint16,i16=np.int16,u32=np.uint32,i32=np.int32,u64=np.uint64,i64=np.int64,f32=np.float32,f64=np.float64)
ok=bad=0
for f in sorted(glob.glob(d+'/*.tif')):
    n=os.path.basename(f); t,ch,c=n[:-4].split('-'); ch=int(ch)
    i=np.arange(35*ch)
    exp={'u8':(i*7)%256,'i8':((i*7-100+128)%256)-128,'u16':(i*1000)%65536,'i16':((i*1000-30000+32768)%65536)-32768,
         'u32':(i.astype(np.uint64)*30000000)%2**32,'i32':i*30000000-2000000000,'u64':i.astype(np.uint64)*np.uint64(10**17),
         'i64':i.astype(np.int64)*10**17-5*10**18,'f32':(i*np.float32(0.37)-np.float32(3)),'f64':i*0.37-3}[t]
    try:
        a=tifffile.imread(f)
        e=np.asarray(exp).astype(dt[t]).reshape((5,7,ch) if ch>1 else (5,7))
        if t=='f32': e=(np.arange(35*ch,dtype=np.float32)*np.float32(0.37)-np.float32(3)).reshape(e.shape)
        if a.dtype==e.dtype and a.shape==e.shape and np.array_equal(a,e): ok+=1
        else: bad+=1; print('MISMATCH',n,a.dtype,a.shape, np.abs(a.astype(float)-e.astype(float)).max() if a.shape==e.shape else '')
    except Exception as ex: bad+=1; print('FAIL',n,type(ex).__name__,ex)
print('tifffile read of TiffNet output: ok',ok,'bad',bad)
