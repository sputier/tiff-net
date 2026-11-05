# Reliability and limits

## Errors

A file that is not valid TIFF fails with a [`TiffException`](api/Eleris.TiffNet.TiffException.md). When the file is valid but
larger than the limits of the reading options allow, the exception names the limit in its `Limit` property, so that an
application can tell a file that is too large for the device from a damaged one.

<!-- example: ReliabilityExamples.TooLargeOrDamaged -->
```csharp
static string Explain(TiffException e) => e.Limit is null
    ? $"The file is damaged: {e.Message}"
    : $"The file is too large for this device ({e.Limit}).";

Console.WriteLine(Explain(new TiffException("The image declares more pixels than the file holds.")));
Console.WriteLine(Explain(new TiffException("The image needs 3 GB.", "MaxPixelBytes")));
```
<!-- /example -->

Messages name a file by its file name, never by its full path, so that they can be shown or logged without revealing the
folders of the user.

## Limits for untrusted files

The defaults suit files that you trust. For files you do not control, set the limits of
[`TiffReadOptions`](api/Eleris.TiffNet.TiffReadOptions.md) to what your application needs. Every limit is checked before the
memory is allocated or the work is done: a hostile file of a few bytes cannot make the library allocate gigabytes first.

| Limit | Default | Protects against |
| --- | --- | --- |
| `MaxInputBytes` | 2 147 483 591 bytes | A stream that is too large to hold in memory (`TiffFile.Read` only) |
| `MaxPixelBytes` | 2 147 483 591 bytes | An image, or a decoding buffer, larger than the device can hold |
| `MaxDirectories` | 10 000 | Millions of pages or chains of sub-directories |
| `MaxTagsPerDirectory` | 4 096 | Directories of thousands of entries, which cost time to read |
| `MaxTagBytes` | 64 MB | A tag announcing more data than any profile or packet needs (a larger metadata tag is left out) |
| `MaxMetadataBytes` | 256 MB | Many tags, or many directories, sharing the same bytes of a small file |
| `MaxJpegScans` | 100 | Progressive JPEG images made of thousands of cheap scans, which cost minutes to decode |

<!-- example: ReliabilityExamples.UntrustedFiles -->
```csharp
// On a phone: at most 256 MB for the pixels of an image, two decoding threads.
var untrusted = TiffReadOptions.Default with { MaxPixelBytes = 256L << 20, MaxDegreeOfParallelism = 2 };

Console.WriteLine($"Pixels: {untrusted.MaxPixelBytes >> 20} MB, threads: {untrusted.MaxDegreeOfParallelism}");
```
<!-- /example -->

## Compressed data

A strip or tile compressed with PackBits, LZW or Deflate is decoded into a buffer of the size the read needs, never more: data
that would decode to gigabytes (a "compression bomb") costs the rows the image declares, and a region decodes each strip or
tile only up to its last row. A strip that decodes to fewer bytes than its rows need is a damaged file. Data beyond the rows of
a strip is ignored, as libtiff does, unless `Strict` is set, which reports it as a damaged file.

This page grows with the library: the memory used by reads and writes, and the rules for threads, are described with the
features that bring them.
