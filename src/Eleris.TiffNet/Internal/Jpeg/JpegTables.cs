using System.Buffers.Binary;

namespace Eleris.TiffNet.Internal.Jpeg;

/// <summary>
/// The tables of TIFF's JPEGTables, read and built once per read and shared by the decoders of its strips or tiles (which never
/// change them): a JPEGTables tag costs its parsing once, not once per tile.
/// </summary>
internal sealed class JpegTables(ushort[]?[] quantization, HuffmanTable?[] dc, HuffmanTable?[] ac)
{
    public ushort[]?[] Quantization { get; } = quantization;

    public HuffmanTable?[] Dc { get; } = dc;

    public HuffmanTable?[] Ac { get; } = ac;

    /// <summary>Reads an abbreviated table-specification stream.</summary>
    /// <exception cref="InvalidDataException">The stream is damaged, or holds more than tables.</exception>
    public static JpegTables Read(ReadOnlySpan<byte> stream, ResourceBudget budget)
    {
        var decoder = new JpegDecoder(budget);
        decoder.ReadTables(stream);
        return decoder.Tables();
    }
}
