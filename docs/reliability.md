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

`TiffReadOptions.Constrained` sets the limits of a phone, or of a server that reads files from anywhere: 256 MB of pixels, 64 MB
of a stream held in memory, two decoding threads.

<!-- example: ReliabilityExamples.UntrustedFiles -->
```csharp
// On a phone, or a server reading files from anywhere: 256 MB for the pixels of an image, two decoding threads.
var untrusted = TiffReadOptions.Constrained;

Console.WriteLine($"Pixels: {untrusted.MaxPixelBytes >> 20} MB, threads: {untrusted.MaxDegreeOfParallelism}");
```
<!-- /example -->

Every hostile file the library is tested with (crafted, and from its security review) fails with the expected error within its
time and memory bounds, and every change to the code that reads untrusted bytes is fuzzed in continuous integration: no
exception other than `TiffException` and `NotSupportedException` comes out of reading a file. A longer campaign of 148 million
inputs over the directories, the pixels and the JPEG decoder found nothing ([report](fuzzing-2026-11.md)).

## Compressed data

A strip or tile compressed with PackBits, LZW or Deflate is decoded into a buffer of the size the read needs, never more: data
that would decode to gigabytes (a "compression bomb") costs the rows the image declares, and a region decodes each strip or
tile only up to its last row. A strip that decodes to fewer bytes than its rows need is a damaged file. Data beyond the rows of
a strip is ignored, as libtiff does, unless `Strict` is set, which reports it as a damaged file. A strip or tile is read
only up to the compressed bytes its rows can need, whatever byte count it declares, so that blocks sharing their data cost no
more than their pixels. An LZW stream that goes on once its table is full, without a clear code, is decoded with the table as
it is, where libtiff refuses it: the codes stay defined, and the decoding stays bounded.

## Bounds of time and memory

What a file can make the library spend is bounded by the file itself or by the options, never by what the file declares:

| A file that… | Costs at most |
| --- | --- |
| declares millions of strips, tiles, directories or tags | the tables it really holds, within `MaxDirectories`, `MaxTagsPerDirectory`, `MaxTagBytes` and `MaxMetadataBytes` |
| declares a huge image in a few bytes | a refusal before the pixels are allocated: the strips must hold their rows (uncompressed), or could decode to them (a fixed ratio per codec) |
| holds a decompression bomb | the rows its image declares, within `MaxPixelBytes` |
| shares one stream between thousands of strips or tiles | the compressed bytes each one can need, not the file each time |
| holds a progressive JPEG of thousands of scans | `MaxJpegScans`; a scan that refines no coefficient is refused |
| ends early (truncated) | what is there: the rest of a JPEG scan is grey, as libjpeg gives it; other codecs report the missing rows |
| is opened, then saved again (a service converting what it receives) | what reading its pixels would: they count in `MaxPixelBytes` although they are decoded band after band, and the file must hold them, checked before anything is allocated |

Every allocation a file asks for (decoded images, decoding buffers, JPEG planes and coefficients) is counted in `MaxPixelBytes`
before it is made; each thread holds the buffers of one strip or tile. Reading and writing can be cancelled between strips or
tiles (`ReadPixelsAsync`, `Save`, `SaveAsync`).

## Saving

A save to a path writes a temporary file next to the target, created on Unix with the permissions of the file it replaces,
then puts it in place; a stream that cannot seek is written through a temporary file readable by its owner only. Symbolic
links are followed: a program that saves into a folder other users can write should not trust the path it was given.

## Threads

An image can be read from several threads at once. Changing an image or the pages of a file must not happen while it is read
or saved; a `TiffImageWriter` is used from one thread. The results of reads and writes do not depend on the number of threads.
