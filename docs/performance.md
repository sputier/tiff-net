# Performance

Measured on an 8-core x64 laptop (AVX2, .NET 10), warm cache, with images of 24 MP (6 000 x 4 000) smooth with noise, as
astronomical frames are. `benchmarks/` reproduces the figures (`TargetBenchmarks`, with `Fastest` included, `JpegBenchmarks`,
`tools/bench_jpeg.py`); the measurements are kept in `benchmarks/results`.

## Throughput

| Scenario | 1 thread | 8 threads |
| --- | --- | --- |
| Read u16, no compression | 5.2 GB/s | 4.5 GB/s (memory bound) |
| Read u16, Deflate + horizontal predictor | 300 MB/s | 1.4 to 1.5 GB/s |
| Read f32 RGB, Deflate + floating point predictor | 650 MB/s | 2.6 GB/s |
| Read u16 RGB uncompressed (144 MB) as a FITS cube (planar) | | 11 ms |
| Read u16 RGB uncompressed (144 MB) normalised to `float` | | 9.6 ms |
| Read 12-bit grey uncompressed (36 MB) | | 6.1 ms |
| Write u16, Deflate + predictor | 0.6 s | 0.11 to 0.13 s |
| Write f32 RGB, Deflate + predictor | 4.4 to 4.6 s | 0.8 to 0.9 s |
| Write f32 RGB, Deflate `Fastest` + predictor | 1.8 s | 0.4 s |

JPEG (12 MP, quality 90, one thread) is decoded in 1.1 to 1.35 times the time of libjpeg-turbo, with the same samples bit for bit.

Gains of the last batch, each measured against the previous version (`PathBenchmarks`, `JpegBenchmarks`, medium job unless noted):

| Change | Case | Before | After |
| --- | --- | --- | --- |
| YCbCr 4:2:0 colour and upsampling with vectors | `ToImage` ycc420 | 63.0 ms | 53.2 ms (conversion 23.2 to 13.3 ms) |
| Small compressed strips read together | u16 Deflate, small strips, short job | 54 to 69 ms | 26 to 37 ms |
| Planes of a planar file read together, band by band | u16 RGB planar read interleaved | 15.8 ms | 13.9 ms |
| | the same, converted to `double` | 56.8 ms | 44.2 ms |

Not changed (measured, within the noise): YCbCr 4:4:4 (94.1 to 91.2 ms), and the planar read of a planar file.

## Memory

- A whole-image read allocates its pixels and the buffers of one strip or tile per thread: 1.00 to 1.07 times the pixels
  above about 50 MB, at most 1.1 times. `ReadPixels(Memory<T>)` reads into a buffer of the application.
- A region reads only the strips or tiles under it, and decodes a compressed strip only up to the last row it needs.
- A Deflate or PackBits strip of more than 4 MB, or an LZW strip of more than 19 MB (an image written in one strip), is
  decoded as a stream, a megabyte of rows at a time, straight to the destination: it costs its pixels, not twice its pixels.
  The stream of LZW allocates, for each strip it decodes, the output of a generation of its table (since its last clear code),
  at most 9.6 MB with its table and its input, which flat data comes close to: that memory is counted once per worker in
  `MaxPixelBytes` before the read. Decoding LZW as a stream takes 1.4 to 2 times the time of
  decoding it whole (an extra copy of its output), PackBits the same time: the price of the memory.
- Writing holds a band of rows of the source (the whole image when the source is one compressed strip, or stored on its
  side) and at most 2 × `MaxDegreeOfParallelism` encoded blocks; `TiffImageWriter` writes an
  image of any size with a bounded window (under 16 MB for 64 MB of pixels).

## Cold cache

A file opened by path is read through a handle without an access hint, so that the system keeps its read-ahead. Reading a
whole 6000 × 4000 RGB 16-bit image from a file that is not in the system cache (Windows 11, median of 7 reads; the program is
`benchmarks/ColdCache`) took:

| File | Threads | `RandomAccess` (before) | none (now) | `SequentialScan` |
| --- | --- | --- | --- | --- |
| Uncompressed, 144 MB | 1 | 114 ms | 53 ms | 52 ms |
| Uncompressed, 144 MB | all cores | 100 ms | 75 ms | 63 ms |
| Deflate, 72 MB | 1 | 581 ms | 451 ms | 460 ms |
| Deflate, 72 MB | all cores | 133 ms | 82 ms | 76 ms |

`SequentialScan` was not adopted: it gains a further 7 to 16 % on all cores, but the hint is fixed when the file is opened,
before the caller chooses between a whole image and a region, and the reads of a region and of the directories are not
sequential. Not measured: Linux, regions on a cold cache. A stream of the application is read as it opens it.

## Tuning

| To | Set |
| --- | --- |
| Use fewer cores (a phone, a server with other work) | `MaxDegreeOfParallelism` of the read or write options; 1 keeps the work on the calling thread |
| Write 2 to 2.5 times faster, files 15 to 50 % larger | `Level = TiffCompressionLevel.Fastest` (Deflate) |
| Write fastest, files largest | `Compression = TiffCompression.None` |
| Read regions of a large image quickly | write it in tiles (`Layout = TiffLayout.Tiles`) |
| Read one channel quickly | write it planar (`PlanarConfiguration = TiffPixelOrder.Planar`): only its plane is read |
| Avoid an array per read | `ReadPixels(Memory<T>)` into a reused buffer (on every core; the `Span<T>` overload decodes on the calling thread only) |
| Bound memory on untrusted files | `MaxPixelBytes` (see [reliability](reliability.md)) |

Deflate at the default level compresses noisy floating point data at about 60 MB/s per core (288 MB in 4.4 to 4.6 s): such files are the slowest to
write, and the predictor matters more than the level for their size.
