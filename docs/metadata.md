# Metadata and Astro-TIFF

`TiffImage.Metadata` holds every tag of an image except its structure (size, samples, compression, strips and tiles…, which the
library computes): the common ones as properties (`Description`, `Software`, `DateTime`, `Artist`, `Copyright`, `Resolution`,
`SampleRange`, `IccProfile`, `Xmp`, `Iptc`…), any other one through its code. Setting a property to `null` removes the tag.

<!-- example: MetadataExamples.Tags -->
```csharp
var image = TiffImage.Create<float>(new float[16], 4, 4);
var metadata = image.Metadata;
metadata.Description = "SIMPLE  =                    T\nOBJECT  = 'M42     '\nEND";   // an Astro-TIFF FITS header
metadata.Software = "Urania";
metadata.Resolution = new TiffResolution(300, 300, TiffResolutionUnit.Inch);
metadata.Set(TiffTag.FromString(65000, "a private tag"));
new TiffFile().Add(image).Save(path);

using var file = TiffFile.Open(path);
var read = file.Primary.Metadata;
Console.WriteLine(read.Description!.Split('\n')[1]);
Console.WriteLine($"{read.Software}, {read.Resolution!.X} dpi, {read[65000]!.AsString()}");
```
<!-- /example -->

- **Astro-TIFF**: the FITS header that Siril and other tools write is the text of `ImageDescription` (`Description`), kept
  byte for byte when a file is read and saved again. Converting it to FITS cards, and flipping rows (FITS counts them from the
  bottom), is the job of the bridge with Eleris.FitsNet.
- **Saving again** keeps every tag except those that hold offsets into the file (EXIF, GPS and Interoperability
  sub-directories, `SubIFDs`, `FreeOffsets`, and any tag of type `IFD`): they would point nowhere in a new file. The EXIF and GPS
  tags are readable (`Exif`, `Gps`) but not written in this version.
- Text is read as UTF-8 when it is valid UTF-8 (ASCII is), as Latin-1 otherwise; it is written as ASCII when it is, as UTF-8
  otherwise.
