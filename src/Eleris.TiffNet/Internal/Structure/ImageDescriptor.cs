namespace Eleris.TiffNet.Internal.Structure;

/// <summary>
/// One image of a file as the library understands it: geometry, samples, layout, the tables of its strips or tiles, checked to be
/// coherent with each other and with the file. A defect of the data is a <see cref="TiffException"/>; a valid feature the library
/// does not handle is not an error here but <see cref="Unsupported"/>, so that the other images and the metadata stay readable.
/// </summary>
internal sealed record ImageDescriptor
{
    // Tags of the image structure.
    public const ushort NewSubfileTypeTag = 254, SubfileTypeTag = 255, ImageWidthTag = 256, ImageLengthTag = 257, BitsPerSampleTag = 258,
        CompressionTag = 259, PhotometricTag = 262, StripOffsetsTag = 273, OrientationTag = 274, SamplesPerPixelTag = 277,
        RowsPerStripTag = 278, StripByteCountsTag = 279, PlanarConfigurationTag = 284, PredictorTag = 317, ColorMapTag = 320,
        TileWidthTag = 322, TileLengthTag = 323, TileOffsetsTag = 324, TileByteCountsTag = 325, SubIfdsTag = 330, ExtraSamplesTag = 338,
        SampleFormatTag = 339, JpegTablesTag = 347, YCbCrSubSamplingTag = 530, FillOrderTag = 266;

    private ImageDescriptor()
    {
    }

    public required RawDirectory Directory { get; init; }

    public long Width { get; private init; }

    public long Height { get; private init; }

    public int SamplesPerPixel { get; private init; }

    /// <summary>The stored depth of a sample (1 to 64).</summary>
    public int BitsPerSample { get; private init; }

    /// <summary>The SampleFormat code: 1 unsigned, 2 signed, 3 floating point, 4 undefined (read as unsigned).</summary>
    public int SampleFormat { get; private init; }

    public TiffPhotometric Photometric { get; private init; }

    /// <summary>The PhotometricInterpretation code as stored (the named value of <see cref="Photometric"/>, or the code it has no name for).</summary>
    public int PhotometricCode { get; private init; }

    public TiffCompression Compression { get; private init; }

    /// <summary>The Compression code as stored (32946, the first code of Deflate, included).</summary>
    public int CompressionCode { get; private init; }

    public TiffPredictor Predictor { get; private init; }

    /// <summary>Whether each channel is stored in its own plane (PlanarConfiguration = 2, with more than one sample).</summary>
    public bool Planar { get; private init; }

    /// <summary>Whether the bits of each byte are stored from the least significant (FillOrder = 2), as some fax writers do.</summary>
    public bool ReversedBits { get; private init; }

    /// <summary>The Orientation tag, 1 to 8.</summary>
    public int Orientation { get; private init; }

    public TiffLayout Layout { get; private init; }

    /// <summary>Rows per strip (at most the height), or the tile height.</summary>
    public long BlockHeight { get; private init; }

    /// <summary>The width of the image for strips, or the tile width.</summary>
    public long BlockWidth { get; private init; }

    /// <summary>The offset of each strip or tile, plane after plane when <see cref="Planar"/>; each within the file.</summary>
    public required long[] Offsets { get; init; }

    /// <summary>The stored size of each strip or tile; each strip or tile ends within the file.</summary>
    public required long[] ByteCounts { get; init; }

    /// <summary>The NewSubfileType bits (the old SubfileType mapped onto them): 1 reduced resolution, 2 page, 4 mask.</summary>
    public int SubfileType { get; private init; }

    /// <summary>The kinds of the extra channels, after the colour channels.</summary>
    public required IReadOnlyList<TiffExtraChannelKind> ExtraChannels { get; init; }

    /// <summary>The colour map of a palette image: red, then green, then blue, 2^bits values each.</summary>
    public ushort[]? ColorMap { get; private init; }

    /// <summary>The JPEGTables of a JPEG image: an abbreviated stream of the tables its strips or tiles share, or null.</summary>
    public byte[]? JpegTables { get; private init; }

    /// <summary>Why the library cannot deliver the pixels of this image, or <see langword="null"/>.</summary>
    public string? Unsupported { get; private init; }

    /// <summary>The colour model of the delivered samples (palette and YCbCr images are delivered as RGB).</summary>
    public TiffColorModel ColorModel { get; private init; }

    /// <summary>The type of the delivered samples.</summary>
    public TiffSampleType SampleType { get; private init; }

    /// <summary>The number of delivered channels: colour channels and extra channels.</summary>
    public int Channels { get; private init; }

    public bool IsReducedResolution => (SubfileType & 1) != 0;

    public bool IsMask => (SubfileType & 4) != 0;

    /// <summary>Reads and checks the image of a directory.</summary>
    /// <exception cref="TiffException">The image is damaged: a required tag is missing, the tables do not match its geometry, a
    /// strip or tile is outside the file.</exception>
    public static ImageDescriptor Read(StructureReader reader, RawDirectory directory)
    {
        var budget = reader.Budget;
        bool strict = budget.Options.Strict;

        long width = Required(reader, directory, ImageWidthTag, "ImageWidth");
        long height = Required(reader, directory, ImageLengthTag, "ImageLength");
        if (width <= 0 || height <= 0 || width > uint.MaxValue || height > uint.MaxValue)
        {
            throw budget.Invalid($"the image is {width} x {height} pixels.");
        }

        int samples = (int)reader.ReadInteger(directory, SamplesPerPixelTag, 1, ushort.MaxValue);
        if (samples < 1)
        {
            throw budget.Invalid("the image declares 0 samples per pixel.");
        }

        long[] bits = AllOrDefault(reader, directory, BitsPerSampleTag, samples, 1);
        long[] formats = AllOrDefault(reader, directory, SampleFormatTag, samples, 1);
        if (bits.Any(b => b is < 1 or > 64) || formats.Any(f => f is < 0 or > ushort.MaxValue))
        {
            throw budget.Invalid($"the image declares {bits[0]} bits per sample in the format {formats[0]}.");
        }

        long compression = reader.ReadInteger(directory, CompressionTag, 1, ushort.MaxValue);
        int planarCode = (int)reader.ReadInteger(directory, PlanarConfigurationTag, 1, ushort.MaxValue);
        if (planarCode is not (1 or 2))
        {
            planarCode = strict ? throw budget.Invalid($"PlanarConfiguration is {planarCode}.") : 1;
        }

        bool planar = planarCode == 2 && samples > 1;
        int orientation = (int)reader.ReadInteger(directory, OrientationTag, 1, ushort.MaxValue);
        if (orientation is < 1 or > 8)
        {
            orientation = strict ? throw budget.Invalid($"Orientation is {orientation}.") : 1;
        }

        long photometricCode;
        if (directory.TryGet(PhotometricTag, out _))
        {
            photometricCode = reader.ReadInteger(directory, PhotometricTag, 1, ushort.MaxValue);
        }
        else
        {
            // As libtiff: grey, or RGB for three samples and more.
            photometricCode = strict ? throw budget.Invalid("the required tag PhotometricInterpretation is missing.") : samples >= 3 ? 2 : 1;
        }

        var photometric = Enum.IsDefined((TiffPhotometric)photometricCode) ? (TiffPhotometric)photometricCode : TiffPhotometric.Unknown;
        int colorChannels = photometric is TiffPhotometric.Rgb or TiffPhotometric.YCbCr ? 3 : 1;
        int extras = samples - colorChannels;
        if (extras < 0)
        {
            throw budget.Invalid($"a {photometric} image needs {colorChannels} samples per pixel, it has {samples}.");
        }

        var extraKinds = ExtraKinds(reader, directory, extras);
        int subfile = SubfileBits(reader, directory);

        // The layout and the tables of strips or tiles, coherent with the geometry and within the file.
        bool tiled = directory.TryGet(TileOffsetsTag, out _) || directory.TryGet(TileWidthTag, out _);
        var blocks = ReadBlocks(reader, directory, tiled, width, height);
        var (offsets, counts) = ReadTables(reader, directory, blocks, tiled, planar ? samples : 1, planar ? 1 : samples, compression, (int)bits[0], height);
        long blockWidth = blocks.Width, blockHeight = blocks.Height;

        long predictor = reader.ReadInteger(directory, PredictorTag, 1, ushort.MaxValue);
        long fillOrder = reader.ReadInteger(directory, FillOrderTag, 1, ushort.MaxValue);
        var descriptor = new ImageDescriptor
        {
            Directory = directory,
            Width = width,
            Height = height,
            SamplesPerPixel = samples,
            BitsPerSample = (int)bits[0],
            SampleFormat = (int)formats[0],
            Photometric = photometric,
            PhotometricCode = (int)photometricCode,
            Compression = compression == 32946 ? TiffCompression.Deflate   // the first code of Deflate
                : Enum.IsDefined((TiffCompression)compression) ? (TiffCompression)compression : TiffCompression.Unknown,
            CompressionCode = (int)compression,
            Predictor = predictor == 0 ? TiffPredictor.None : (TiffPredictor)predictor,   // 0 is not defined; libtiff reads it as none
            Planar = planar,
            Orientation = orientation,
            ReversedBits = fillOrder == 2,
            Layout = tiled ? TiffLayout.Tiles : TiffLayout.Strips,
            BlockWidth = blockWidth,
            BlockHeight = blockHeight,
            Offsets = offsets,
            ByteCounts = counts,
            SubfileType = subfile,
            ExtraChannels = extraKinds,
        };
        descriptor = descriptor.WithSupport(bits, formats);

        // The colour map matters only when the pixels can be delivered: a defect in it must not make an image the library would
        // refuse anyway (or the whole file) fail.
        if (descriptor.Photometric == TiffPhotometric.Palette && descriptor.Unsupported is null)
        {
            descriptor = descriptor with { ColorMap = ReadColorMap(reader, directory, descriptor.BitsPerSample) };
        }

        // The tables shared by the JPEG streams of the strips or tiles (Technical Note 2), read only when they can be used.
        if (descriptor.Compression == TiffCompression.Jpeg && descriptor.Unsupported is null && directory.TryGet(JpegTablesTag, out var tables))
        {
            descriptor = descriptor with { JpegTables = reader.ReadBytes(tables) };
        }

        return descriptor;
    }

    /// <summary>
    /// The description of pixels held in memory (an image created by the application): uncompressed, one strip (one per plane
    /// when planar), in the byte order of the machine, top-left.
    /// </summary>
    /// <returns>The description, whose <see cref="Unsupported"/> says why such pixels cannot be held (rows larger than an array).</returns>
    public static ImageDescriptor ForPixels(long width, long height, int channels, TiffSampleType type, TiffColorModel model,
        IReadOnlyList<TiffExtraChannelKind> extras, bool planar)
    {
        int bytes = Pixels.SampleConversion.SizeOf(type);
        int format = Pixels.SampleTypes.Of(type).Format;
        long plane = width * height * bytes;
        int planes = planar ? channels : 1;
        var descriptor = new ImageDescriptor
        {
            Directory = new RawDirectory(0, [], 0),
            Width = width,
            Height = height,
            SamplesPerPixel = channels,
            BitsPerSample = bytes * 8,
            SampleFormat = format,
            Photometric = model == TiffColorModel.Rgb ? TiffPhotometric.Rgb : TiffPhotometric.MinIsBlack,
            Compression = TiffCompression.None,
            CompressionCode = 1,
            PhotometricCode = model == TiffColorModel.Rgb ? 2 : 1,
            Predictor = TiffPredictor.None,
            Planar = planar && channels > 1,
            Orientation = 1,
            Layout = TiffLayout.Strips,
            BlockWidth = width,
            BlockHeight = height,
            Offsets = [.. Enumerable.Range(0, planes).Select(p => p * plane * (planar ? 1 : channels))],
            ByteCounts = [.. Enumerable.Repeat(plane * (planar ? 1 : channels), planes)],
            ExtraChannels = extras,
        };
        long[] bits = [.. Enumerable.Repeat((long)bytes * 8, channels)];
        long[] formats = [.. Enumerable.Repeat((long)format, channels)];
        return descriptor.WithSupport(bits, formats);
    }

    /// <summary>The size of the strips or tiles of an image, and how many there are across and down.</summary>
    private readonly record struct BlockGeometry(long Width, long Height, long Across, long Down);

    /// <summary>The strips (their rows) or the tiles (their size, checked) of an image.</summary>
    private static BlockGeometry ReadBlocks(StructureReader reader, RawDirectory directory, bool tiled, long width, long height)
    {
        var budget = reader.Budget;
        if (!tiled)
        {
            long rows = reader.ReadInteger(directory, RowsPerStripTag, uint.MaxValue);
            long blockHeight = rows <= 0 || rows > height ? height : rows;
            return new BlockGeometry(width, blockHeight, 1, (height + blockHeight - 1) / blockHeight);
        }

        long tileWidth = Required(reader, directory, TileWidthTag, "TileWidth");
        long tileHeight = Required(reader, directory, TileLengthTag, "TileLength");
        if (tileWidth <= 0 || tileHeight <= 0 || tileWidth > uint.MaxValue || tileHeight > uint.MaxValue)
        {
            throw budget.Invalid($"the tiles are {tileWidth} x {tileHeight} pixels.");
        }

        if (budget.Options.Strict && (tileWidth % 16 != 0 || tileHeight % 16 != 0))
        {
            throw budget.Invalid($"the tiles are {tileWidth} x {tileHeight}, not multiples of 16.");
        }

        return new BlockGeometry(tileWidth, tileHeight, (width + tileWidth - 1) / tileWidth, (height + tileHeight - 1) / tileHeight);
    }

    /// <summary>
    /// The offsets and byte counts of the strips or tiles: as many as the geometry needs, within the file (a block cut by a
    /// truncated file keeps what there is), the byte counts of uncompressed data computed when they are missing.
    /// </summary>
    private static (long[] Offsets, long[] Counts) ReadTables(StructureReader reader, RawDirectory directory, BlockGeometry geometry,
        bool tiled, int planes, int rowSamples, long compression, int bits, long height)
    {
        var budget = reader.Budget;
        bool strict = budget.Options.Strict;
        string unit = tiled ? "tile" : "strip";
        long blocks = 0;
        try
        {
            blocks = checked(geometry.Across * geometry.Down * planes);
        }
        catch (OverflowException)
        {
            throw budget.Invalid($"the image would have more than 2^63 {unit}s.");
        }

        long[] offsets = Table(reader, directory, tiled ? TileOffsetsTag : StripOffsetsTag, blocks, unit, "offsets");
        long[] counts = [];
        ushort countsTag = tiled ? TileByteCountsTag : StripByteCountsTag;
        if (directory.TryGet(countsTag, out _))
        {
            counts = Table(reader, directory, countsTag, blocks, unit, "byte counts");
        }
        else if (compression == 1 && !strict)
        {
            // Uncompressed data: the size of each block follows from the geometry, as libtiff computes it.
            long rowBytes = BytesPerRow(geometry.Width, rowSamples, bits);
            counts = new long[blocks];
            for (long i = 0; i < blocks; i++)
            {
                long firstRow = tiled ? 0 : i % geometry.Down * geometry.Height;
                counts[i] = rowBytes * (tiled ? geometry.Height : Math.Min(geometry.Height, height - firstRow));
            }
        }
        else
        {
            throw budget.Invalid($"the {unit} byte counts are missing.");
        }

        for (int i = 0; i < offsets.Length; i++)
        {
            if (offsets[i] < 0 || counts[i] < 0 || offsets[i] > reader.Source.Length)
            {
                throw budget.Invalid($"{unit} {i} starts outside the file (offset {offsets[i]}, {counts[i]} bytes).");
            }

            if (counts[i] > reader.Source.Length - offsets[i])
            {
                // A last strip cut by a truncated file: read what there is (the decoder reports what is missing).
                counts[i] = strict
                    ? throw budget.Invalid($"{unit} {i} ends beyond the end of the file.")
                    : reader.Source.Length - offsets[i];
            }
        }

        return (offsets, counts);
    }

    /// <summary>The same image with other kinds of extra channels.</summary>
    public ImageDescriptor WithExtraChannels(IReadOnlyList<TiffExtraChannelKind> extras) => this with { ExtraChannels = extras };

    /// <summary>The same image with the NewSubfileType bits of another (an image whose pixels are replaced stays what it was).</summary>
    public ImageDescriptor WithSubfileType(int subfileType) => this with { SubfileType = subfileType };

    /// <summary>A palette image as its indices: grey samples of the stored depth, no colour map to expand.</summary>
    public ImageDescriptor AsIndices() => this with { Photometric = TiffPhotometric.MinIsBlack, PhotometricCode = 1, ColorMap = null, Channels = SamplesPerPixel };

    /// <summary>Bytes of one row of <paramref name="width"/> pixels of <paramref name="samples"/> samples, padded to a byte.</summary>
    public static long BytesPerRow(long width, int samples, int bits) => (width * samples * bits + 7) / 8;

    private ImageDescriptor WithSupport(long[] bits, long[] formats)
    {
        string? reason = UnsupportedReason(bits, formats);
        bool palette = Photometric == TiffPhotometric.Palette;
        bool colour = Photometric is TiffPhotometric.Rgb or TiffPhotometric.YCbCr || palette;
        return this with
        {
            Unsupported = reason,
            ColorModel = colour ? TiffColorModel.Rgb : TiffColorModel.Grey,
            SampleType = reason is null ? DeliveredType(palette) : default,
            Channels = (colour ? 3 : 1) + ExtraChannels.Count,
        };
    }

    private TiffSampleType DeliveredType(bool palette)
    {
        if (palette)
        {
            return TiffSampleType.UInt16;
        }

        return (SampleFormat, BitsPerSample) switch
        {
            (2, 8) => TiffSampleType.Int8,
            (2, 16) => TiffSampleType.Int16,
            (2, 32) => TiffSampleType.Int32,
            (2, 64) => TiffSampleType.Int64,
            (3, 16) => TiffSampleType.Float16,
            (3, 32) => TiffSampleType.Float32,
            (3, 64) => TiffSampleType.Float64,
            (_, <= 8) => TiffSampleType.UInt8,
            (_, <= 16) => TiffSampleType.UInt16,
            (_, 32) => TiffSampleType.UInt32,
            _ => TiffSampleType.UInt64,
        };
    }

    private string? UnsupportedReason(long[] bits, long[] formats)
    {
        if (Width > int.MaxValue || Height > int.MaxValue)
        {
            return $"images wider or taller than {int.MaxValue} pixels are not supported ({Width} x {Height}).";
        }

        string? compression = CompressionCode switch
        {
            1 or 5 or 7 or 8 or 32946 or 32773 => null,
            2 or 3 or 4 => "CCITT fax compression is not supported (bilevel images are read when uncompressed, LZW, Deflate or PackBits).",
            6 => "old-style JPEG compression (6), deprecated since 1995, is not supported.",
            34712 => "JPEG 2000 compression is not supported.",
            34925 => "LZMA compression is not supported.",
            50000 => "ZSTD compression is not supported.",
            50001 => "WebP compression is not supported.",
            50002 => "JPEG XL compression is not supported.",
            var code => $"compression {code} is not supported.",
        };
        if (compression is not null)
        {
            return compression;
        }

        string? photometric = PhotometricCode switch
        {
            0 or 1 or 2 or 3 => null,
            6 when Compression == TiffCompression.Jpeg => null,
            6 => "YCbCr images are supported with JPEG compression only.",
            4 => "transparency masks (photometric interpretation 4) are not supported.",
            5 => "separated (CMYK) images are not supported.",
            8 or 9 or 10 => "Lab images are not supported.",
            32803 or 34892 => "raw camera data (CFA or LinearRaw) is not supported: use a raw decoder.",
            32844 or 32845 => "LogL and LogLuv images are not supported.",
            var code => $"the photometric interpretation {code} is not supported.",
        };
        if (photometric is not null)
        {
            return photometric;
        }

        if (bits.Any(b => b != bits[0]))
        {
            return "images whose channels have different numbers of bits are not supported.";
        }

        if (formats.Any(f => f != formats[0]))
        {
            return "images whose channels have different sample formats are not supported.";
        }

        int b0 = BitsPerSample;
        string? samples = (SampleFormat, b0) switch
        {
            (1 or 4, <= 16 or 32 or 64) => null,
            (2, 8 or 16 or 32 or 64) => null,
            (3, 16 or 32 or 64) => null,
            (5 or 6, _) => "complex samples are not supported.",
            (3, _) => $"{b0}-bit floating point samples are not supported.",
            (2, _) => $"{b0}-bit signed samples are not supported.",
            (1 or 4, _) => $"{b0}-bit samples are not supported.",
            _ => $"the sample format {SampleFormat} is not supported.",
        };
        if (samples is not null)
        {
            return samples;
        }

        if (Photometric == TiffPhotometric.Palette && (b0 > 16 || SampleFormat is not (1 or 4)))
        {
            return "palette images need unsigned samples of 1 to 16 bits.";
        }

        if (Compression == TiffCompression.Jpeg && (b0 != 8 || SampleFormat is not (1 or 4) || Planar
            || (SamplesPerPixel, Photometric) is not ((1, TiffPhotometric.MinIsBlack) or (3, TiffPhotometric.Rgb) or (3, TiffPhotometric.YCbCr))))
        {
            return "JPEG images are supported as 8-bit grey (one sample) or 8-bit colour (three interleaved samples).";
        }

        if (BlockWidth > int.MaxValue || BlockHeight > int.MaxValue)
        {
            return $"tiles of {BlockWidth} x {BlockHeight} pixels are not supported.";
        }

        // A row of a block is read in one buffer and unpacked in another (a palette gives two samples more per pixel).
        if (BytesPerRow(BlockWidth, Planar ? 1 : SamplesPerPixel, b0) > Array.MaxLength || BlockWidth * (SamplesPerPixel + 2L) > Array.MaxLength)
        {
            return $"rows of {BlockWidth} pixels of {SamplesPerPixel} samples are not supported: they are larger than an array.";
        }

        // A predictor is undone after LZW and Deflate only (as libtiff does): with other compressions the tag is ignored.
        if (Compression is not (TiffCompression.Lzw or TiffCompression.Deflate))
        {
            return null;
        }

        return Predictor switch
        {
            TiffPredictor.None => null,
            TiffPredictor.Horizontal when b0 is 8 or 16 or 32 or 64 => null,   // on the bits of the samples, floating point included
            TiffPredictor.FloatingPoint when SampleFormat is 3 => null,
            _ => $"the predictor {(int)Predictor} is not supported for {b0}-bit {(SampleFormat == 3 ? "floating point" : "integer")} samples.",
        };
    }

    private static long Required(StructureReader reader, RawDirectory directory, ushort tag, string name)
    {
        if (!directory.TryGet(tag, out _))
        {
            throw reader.Budget.Invalid($"the required tag {name} is missing.");
        }

        return reader.ReadInteger(directory, tag, 0);
    }

    /// <summary>The values of a per-sample tag: as many as samples (one value repeated, as some writers store it), or the default.</summary>
    private static long[] AllOrDefault(StructureReader reader, RawDirectory directory, ushort tag, int samples, long fallback)
    {
        if (!directory.TryGet(tag, out var entry))
        {
            return [fallback];
        }

        if (entry.Count > 1 && entry.Count < samples && reader.Budget.Options.Strict)
        {
            throw reader.Budget.Invalid($"tag {tag} has {entry.Count} values for {samples} samples.");
        }

        long[] values = reader.ReadIntegers(entry with { Count = Math.Min(entry.Count, samples) });
        return values.Length == 0 ? [fallback] : values;
    }

    private static long[] Table(StructureReader reader, RawDirectory directory, ushort tag, long blocks, string unit, string what)
    {
        if (!directory.TryGet(tag, out var entry))
        {
            throw reader.Budget.Invalid($"the {unit} {what} are missing.");
        }

        if (entry.Type is not (TiffTagType.Short or TiffTagType.Long or TiffTagType.Long8 or TiffTagType.Ifd or TiffTagType.Ifd8))
        {
            throw reader.Budget.Invalid($"the {unit} {what} are {entry.Type} values; SHORT, LONG or LONG8 are required.");
        }

        if (entry.Count < blocks || (entry.Count > blocks && reader.Budget.Options.Strict))
        {
            throw reader.Budget.Invalid($"the image has {blocks} {unit}s, and {entry.Count} {unit} {what}.");
        }

        // The table is in the file (its entry is valid) and has the number of blocks the geometry implies; reading it is counted
        // in MaxMetadataBytes. Nothing is allocated from a count the file cannot back up.
        return reader.ReadIntegers(entry with { Count = blocks });
    }

    private static TiffExtraChannelKind[] ExtraKinds(StructureReader reader, RawDirectory directory, int extras)
    {
        var kinds = new TiffExtraChannelKind[extras];
        if (extras > 0 && directory.TryGet(ExtraSamplesTag, out var entry))
        {
            long[] values = reader.ReadIntegers(entry with { Count = Math.Min(entry.Count, extras) });
            for (int i = 0; i < values.Length; i++)
            {
                kinds[i] = values[i] is 1 or 2 ? (TiffExtraChannelKind)values[i] : TiffExtraChannelKind.Data;
            }
        }

        return kinds;
    }

    private static int SubfileBits(StructureReader reader, RawDirectory directory)
    {
        if (directory.TryGet(NewSubfileTypeTag, out _))
        {
            return (int)(reader.ReadInteger(directory, NewSubfileTypeTag, 0) & 0x7);
        }

        // SubfileType (deprecated): 1 full resolution, 2 reduced resolution, 3 a page of a multi-page image.
        return reader.ReadInteger(directory, SubfileTypeTag, 1) switch
        {
            2 => 1,
            3 => 2,
            _ => 0,
        };
    }

    private static ushort[] ReadColorMap(StructureReader reader, RawDirectory directory, int bits)
    {
        if (!directory.TryGet(ColorMapTag, out var entry))
        {
            throw reader.Budget.Invalid("the ColorMap of a palette image is missing.");
        }

        long expected = 3L << bits;
        if (entry.Count != expected)
        {
            throw reader.Budget.Invalid($"the ColorMap has {entry.Count} values, {expected} are expected for {bits}-bit indices.");
        }

        return [.. reader.ReadIntegers(entry).Select(v => (ushort)v)];
    }
}
