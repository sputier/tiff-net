using Eleris.TiffNet.Internal;
using Eleris.TiffNet.Internal.Jpeg;
using Eleris.TiffNet.Tests.Fixtures;

namespace Eleris.TiffNet.Tests.Jpeg;

/// <summary>JPEG streams made to cost more than their bytes.</summary>
[Collection(nameof(Measurement))]
public class HostileJpegTests
{
    [Fact]
    public void Thousands_of_table_definitions_cost_their_bytes_not_their_tables()
    {
        // 100 000 DHT segments of one small AC table each, then nothing: each definition is checked and copied, not built.
        byte[] table = [0xFF, 0xC4, 0, 20, 0x10, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0x01];
        byte[] stream = [0xFF, 0xD8, .. Enumerable.Repeat(table, 100_000).SelectMany(t => t), 0xFF, 0xD9];

        var decoder = new JpegDecoder(new ResourceBudget(TiffReadOptions.Default, "t"));
        decoder.ReadTables([.. stream[..^2], 0xFF, 0xD9]);   // the definitions themselves are valid

        HostileAssert.FailsWithinBounds(() => new JpegDecoder(new ResourceBudget(TiffReadOptions.Default, "t")).Decode(stream),
            "InvalidDataException", TimeSpan.FromSeconds(1), 1 << 20, "100 000 DHT");
    }
}
