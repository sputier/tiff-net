# Threads and memory

Reading decodes the strips or tiles of an image on every core (`TiffReadOptions.MaxDegreeOfParallelism`), writing encodes them
on every core (`TiffWriteOptions.MaxDegreeOfParallelism`); 1 keeps the work on the calling thread. `ReadPixels(Memory<T>)` fills a
buffer of the application on every core; `ReadPixels(Span<T>)` on the calling thread only. Results do not depend on the
number of threads: the same pixels, the same file.

<!-- example: ThreadExamples.Limits -->
```csharp
// Reading: two decoding threads, at most 256 MB for the pixels of an image.
var read = TiffReadOptions.Default with { MaxDegreeOfParallelism = 2, MaxPixelBytes = 256L << 20 };

// Writing on the calling thread only.
var write = TiffWriteOptions.Default with { MaxDegreeOfParallelism = 1 };

Console.WriteLine($"read on {read.MaxDegreeOfParallelism} threads, write on {write.MaxDegreeOfParallelism}");
```
<!-- /example -->

- **Memory**: a whole-image read costs its pixels and the buffers of one strip or tile per thread (at most 1.1 × the pixels
  for large images); `ReadPixels(Memory<T>)` fills a buffer of the application without allocating the pixels. Writing holds at most
  2 × `MaxDegreeOfParallelism` encoded blocks, and a band of rows of the source (the whole image when the source is one
  compressed strip, or stored on its side: Orientation 5 to 8). Every buffer a file asks for is counted in
  `MaxPixelBytes` before it is allocated.
- **Thread safety**: an image can be read from several threads at once. Changing it (`SetPixels`, `SetExtraChannels`, its
  metadata) or the pages of a file must not happen while it is read or saved. A `TiffImageWriter` is used from one thread.
- **Performance**: see the [performance page](performance.md).
