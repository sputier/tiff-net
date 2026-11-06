# Files and pages

`TiffFile.Open(path)` reads the header and every directory of a file, not its pixels, and keeps the file open until it is
disposed: the pixels are read on demand, and a region reads only the strips or tiles it covers. `TiffFile.Read(stream)` reads a
stream into memory (bounded by `MaxInputBytes`); `ReadAsync` does it asynchronously.

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
  with their size, metadata and `Storage`, and `UnsupportedReason` says why; their pixels throw a `NotSupportedException`.
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
