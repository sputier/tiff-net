namespace Eleris.TiffNet.Internal.Structure;

/// <summary>Reads the metadata of a directory: every tag that is not structure, and the EXIF and GPS sub-directories.</summary>
internal static class MetadataReader
{
    private const ushort ExifTag = 34665, GpsTag = 34853, IptcTag = 33723;

    public static TiffMetadata Read(StructureReader reader, RawDirectory directory) =>
        new(Tags(reader, directory, structure: true), SubDirectory(reader, directory, ExifTag), SubDirectory(reader, directory, GpsTag));

    private static List<TiffTag> Tags(StructureReader reader, RawDirectory directory, bool structure)
    {
        var tags = new List<TiffTag>();
        foreach (var entry in directory.Entries.Values)
        {
            if ((structure && TiffMetadata.Excluded.Contains(entry.Code)) || entry.Type is TiffTagType.Ifd or TiffTagType.Ifd8)
            {
                continue;
            }

            if (!entry.Valid || !reader.Budget.TagFits(entry.ByteLength))
            {
                // Values outside the file, or larger than MaxTagBytes: the tag is left out (as libtiff drops tags it cannot
                // read), so that a device with a low limit still opens the image; a strict read refuses the file.
                if (reader.Budget.Options.Strict)
                {
                    reader.Ensure(entry);
                }

                continue;
            }

            byte[] values = reader.ReadBytes(entry);
            // IPTC is a stream of bytes that writers declare as LONG: swapping its "values" would corrupt it.
            var type = entry.Code == IptcTag ? TiffTagType.Undefined : entry.Type;
            tags.Add(TiffTag.FromFile(entry.Code, type, values, reader.Header.LittleEndian || type == TiffTagType.Undefined));
        }

        return tags;
    }

    private static List<TiffTag> SubDirectory(StructureReader reader, RawDirectory directory, ushort pointer)
    {
        if (!directory.TryGet(pointer, out var entry) || !entry.Valid || !TagTypes.IsInteger(entry.Type))
        {
            return [];
        }

        long offset = reader.ReadIntegers(entry with { Count = 1 })[0];
        // An offset outside the file or already read (a loop) gives no tags; Strict makes it a TiffException.
        return reader.ReadDirectory(offset) is { } sub ? Tags(reader, sub, structure: false) : [];
    }
}
