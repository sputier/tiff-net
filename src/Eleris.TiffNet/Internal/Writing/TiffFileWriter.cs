using Eleris.TiffNet.Internal.Structure;

namespace Eleris.TiffNet.Internal.Writing;

/// <summary>Writes the pages of a file: their blocks, encoded in parallel and appended in order, then their directories.</summary>
internal static class TiffFileWriter
{
    /// <summary>Tags of the structure.</summary>
    private const ushort NewSubfileType = 254, ImageWidth = 256, ImageLength = 257, BitsPerSample = 258, CompressionTag = 259,
        Photometric = 262, StripOffsets = 273, Orientation = 274, SamplesPerPixel = 277, RowsPerStrip = 278, StripByteCounts = 279,
        PlanarConfiguration = 284, PredictorTag = 317, TileWidth = 322, TileLength = 323, TileOffsets = 324, TileByteCounts = 325,
        ExtraSamples = 338, SampleFormat = 339;

    /// <summary>Writes the images as one file into a stream that seeks.</summary>
    public static void Write(IReadOnlyList<TiffImage> images, Stream output, TiffWriteOptions options, CancellationToken cancellationToken)
    {
        if (images.Count == 0)
        {
            throw new InvalidOperationException("A TIFF file needs at least one image.");
        }

        var encoders = images.Select(i => new ImageEncoder(i, options)).ToList();
        bool bigTiff = options.BigTiff switch
        {
            TiffBigTiff.Always => true,
            TiffBigTiff.Never => false,
            _ => MaxBytes(encoders, images) > uint.MaxValue,
        };

        var writer = new FileWriter(output, bigTiff);
        for (int i = 0; i < images.Count; i++)
        {
            var encoder = encoders[i];
            long[] offsets = new long[encoder.Blocks], counts = new long[encoder.Blocks];
            BlockPipeline.Run(encoder.Blocks, options.Degree, encoder.Encode, (block, data) =>
            {
                offsets[block] = writer.Append(data);
                counts[block] = data.Length;
            }, cancellationToken);
            writer.WriteDirectory(Entries(encoder.Shape(images[i]), images[i].Metadata, offsets, counts, bigTiff));
        }
    }

    /// <summary>An upper bound of the size of the file: the encoded blocks, the tables, the metadata.</summary>
    private static long MaxBytes(List<ImageEncoder> encoders, IReadOnlyList<TiffImage> images)
    {
        long total = 16;
        for (int i = 0; i < encoders.Count; i++)
        {
            total += encoders[i].MaxEncodedBytes() + 16L * encoders[i].Blocks + 4096;
            total += images[i].Metadata.Sum(t => (long)t.AsBytes().Length + 32);
        }

        return total;
    }

    /// <summary>The entries of the directory of an image: its structure, then its metadata (except the tags of offsets).</summary>
    internal static List<DirectoryEntry> Entries(ImageShape e, TiffMetadata metadata, long[] offsets, long[] counts, bool bigTiff)
    {
        int samples = e.Channels;
        int bits = Pixels.SampleConversion.SizeOf(e.SampleType) * 8;
        int format = e.SampleType switch
        {
            TiffSampleType.Float16 or TiffSampleType.Float32 or TiffSampleType.Float64 => 3,
            TiffSampleType.Int8 or TiffSampleType.Int16 or TiffSampleType.Int32 or TiffSampleType.Int64 => 2,
            _ => 1,
        };
        var entries = new List<DirectoryEntry>
        {
            DirectoryEntry.Long(ImageWidth, e.Width),
            DirectoryEntry.Long(ImageLength, e.Height),
            DirectoryEntry.Short(BitsPerSample, [.. Enumerable.Repeat(bits, samples)]),
            DirectoryEntry.Short(CompressionTag, (int)e.Compression),
            DirectoryEntry.Short(Photometric, e.ColorModel == TiffColorModel.Rgb ? 2 : 1),
            DirectoryEntry.Short(Orientation, 1),
            DirectoryEntry.Short(SamplesPerPixel, samples),
            DirectoryEntry.Offsets(e.Tiled ? TileOffsets : StripOffsets, offsets, bigTiff),
            DirectoryEntry.Offsets(e.Tiled ? TileByteCounts : StripByteCounts, counts, bigTiff),
        };

        if (e.Tiled)
        {
            entries.Add(DirectoryEntry.Long(TileWidth, e.BlockWidth));
            entries.Add(DirectoryEntry.Long(TileLength, e.BlockHeight));
        }
        else
        {
            entries.Add(DirectoryEntry.Long(RowsPerStrip, e.BlockHeight));
        }

        if (samples > 1)
        {
            entries.Add(DirectoryEntry.Short(PlanarConfiguration, e.Planar ? 2 : 1));
        }

        if (e.Predictor != TiffPredictor.None)
        {
            entries.Add(DirectoryEntry.Short(PredictorTag, (int)e.Predictor));
        }

        if (e.Extras.Count > 0)
        {
            entries.Add(DirectoryEntry.Short(ExtraSamples, [.. e.Extras.Select(k => (int)k)]));
        }

        if (format != 1)
        {
            entries.Add(DirectoryEntry.Short(SampleFormat, [.. Enumerable.Repeat(format, samples)]));
        }

        // What the image is (a reduced-resolution version, a mask), as it was read; a page otherwise.
        int subfile = e.SubfileType & 5;
        if (subfile != 0)
        {
            entries.Add(DirectoryEntry.Long(NewSubfileType, subfile));
        }

        foreach (var tag in metadata)
        {
            if (Writable(tag))
            {
                entries.Add(new DirectoryEntry((ushort)tag.Code, tag.Type, tag.Count, tag.AsBytes().ToArray()));
            }
        }

        return entries;
    }

    /// <summary>
    /// Whether a tag of the metadata is written: every tag but those of the structure (computed above) and those that hold offsets
    /// into the file (they would point nowhere in the new file): types IFD and IFD8, and the known offset tags.
    /// </summary>
    internal static bool Writable(TiffTag tag) =>
        !TiffMetadata.Excluded.Contains(tag.Code) && tag.Type is not (TiffTagType.Ifd or TiffTagType.Ifd8)
        && TagTypes.Size(tag.Type) > 0 && tag.Code is > 0 and <= ushort.MaxValue;
}

/// <summary>What the directory of an image says about its structure.</summary>
internal readonly record struct ImageShape(long Width, long Height, int Channels, TiffSampleType SampleType, TiffColorModel ColorModel,
    IReadOnlyList<TiffExtraChannelKind> Extras, int SubfileType, bool Tiled, bool Planar, int BlockWidth, int BlockHeight,
    TiffCompression Compression, TiffPredictor Predictor);
