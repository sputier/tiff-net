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

This page grows with the library: the limits for untrusted files, the memory used by reads and writes, and the rules for threads
are described with the features that bring them.
