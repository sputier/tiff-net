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

## Memory

- A whole-image read allocates its pixels and the buffers of one strip or tile per thread: 1.00 to 1.07 times the pixels
  above about 50 MB, at most 1.1 times. `ReadPixels(Memory<T>)` reads into a buffer of the application.
- A region reads only the strips or tiles under it, and decodes a compressed strip only up to the last row it needs.
- Writing holds a band of rows of the source (the whole image when the source is one compressed strip, or stored on its
  side) and at most 2 × `MaxDegreeOfParallelism` encoded blocks; `TiffImageWriter` writes an
  image of any size with a bounded window (under 16 MB for 64 MB of pixels).

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
