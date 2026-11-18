# Files and pages

`TiffFile.Open(path)` (or `OpenAsync`) reads the header and every directory of a file, not its pixels, and keeps the file open until it is
disposed: the pixels are read on demand, and a region reads only the strips or tiles it covers. `TiffFile.Open(stream)` does
the same with a stream that seeks (a `FileStream` is read through its handle, from several threads), without copying it.
`TiffFile.Read(stream)` reads a stream into memory, including one that cannot seek (bounded by `MaxInputBytes`); `ReadAsync`
does it asynchronously; `TiffFile.Read(bytes)` reads bytes the application already holds, without a copy.

<!-- example: FilesExamples.Pages -->
```csharp
using var file = TiffFile.Open(path);
foreach (var page in file)
{
    string support = page.UnsupportedReason ?? "readable";
    Console.WriteLine($"{page.Width} x {page.Height}, {page.Channels} channel(s), {page.Storage!.Compression}: {support}");
}

// The first page that is neither a thumbnail nor a mask.
Console.WriteLine($"Primary: {file.Primary.Width} x {file.Primary.Height}");
```
<!-- /example -->

- **Pages** are the images of the main chain of directories, in file order. `Primary` is the first that is neither a
  reduced-resolution version (a thumbnail) nor a transparency mask, as `NewSubfileType` says.
- **Sub-images** (`SubIFDs`, a pyramid) are reached through `TiffImage.SubImages`; they are read only in this version.
- **Images the library does not handle** (CMYK, Lab, a compression it does not read) do not make the file fail: they are listed
  with their size, metadata and `Storage`, and `UnsupportedReason` says why; their pixels throw a `NotSupportedException`. [Images the library cannot decode](unsupported-images.md) lists what stays usable.
- **`Storage`** tells how an image is stored: compression, predictor, layout, byte order, BigTIFF, the stored depth and
  photometric interpretation, the orientation.

<!-- example: FilesExamples.FromStream -->
```csharp
// The stream is read into memory (at most MaxInputBytes): it can be closed afterwards, and need not seek.
using var file = TiffFile.Read(source);
Console.WriteLine($"{file.Count} page(s) read from a stream");
```
<!-- /example -->

A file can be built and changed: `new TiffFile()`, `Add`, `Insert`, `Remove`, then `Save` (see [writing](writing.md)).

## Stored blocks

`TiffImage.Blocks` lists where the strips or tiles of an image are stored: for each, its `Offset` in the file and its `ByteCount`,
in file order (plane after plane for a planar image; `Storage.Layout` tells strips from tiles). Nothing is decoded, so the list
is available for every image, including one the library cannot decode, such as a vendor compression. This is what an
application with its own decoder needs: the tags that hold the tables are not part of `Metadata`.

```csharp
using var file = TiffFile.Open(path);
var image = file.Primary;
byte[] buffer = new byte[image.Blocks.Max(b => b.ByteCount)];   // allocated once, reused
for (int i = 0; i < image.Blocks.Count; i++)
{
    int count = image.ReadBlock(i, buffer);                     // the stored bytes, still compressed
    MyDecoder.Decode(buffer.AsSpan(0, count));
}
```

- The offsets and counts are those the library itself reads with, after the checks of a damaged file: a block that starts outside
  the file makes the file fail to open (`TiffException`), and a last block cut by a truncated file is shortened to the end of
  the file, unless `TiffReadOptions.Strict` is set, which refuses the file when it is opened.
- `ReadBlock(index, destination)` copies the block and returns its byte count; it allocates nothing, honours the limits of
  the file and can be called from several threads. `ArgumentException` when the destination is too short,
  `ArgumentOutOfRangeException` for an index outside `Blocks`. `ReadBlockAsync` does the same asynchronously and can be
  cancelled; its failures come through the task.
- `TryGetBlockMemory(index)` gives the block as a view of the memory the file is held in, without a copy; it returns `null` when
  the file is read on demand from a file or a stream, and for a stream read with `TiffFile.Read(stream)` that cannot seek.
- An image created in memory (`TiffImage.Create`, or after `SetPixels`) stores nothing: its `Blocks` is empty and `ReadBlock`
  throws `InvalidOperationException`.
