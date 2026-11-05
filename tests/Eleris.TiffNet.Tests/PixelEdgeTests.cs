using Eleris.TiffNet.Internal;
using Eleris.TiffNet.Internal.Pixels;
using Eleris.TiffNet.Tests.Fixtures;

namespace Eleris.TiffNet.Tests;

/// <summary>Pixel cases the fixtures do not hold, built byte by byte.</summary>
public class PixelEdgeTests
{
    private static TiffImage Open(TiffBuilder builder, TiffReadOptions? options = null) =>
        TiffFile.Read(new MemoryStream(builder.Build()), options).Primary;

    [Fact]
    public void MinIsWhite_inverts_the_grey_samples_and_not_the_alpha()
    {
        var builder = new TiffBuilder { Data = [0, 10, 255, 200, 100, 0] };
        builder.AddDirectory().Image(3, 1, samples: 2, photometric: 0).Add(338, TiffTagType.Short, 2);   // unassociated alpha

        Assert.Equal(new byte[] { 255, 10, 0, 200, 155, 0 }, Open(builder).ReadRawPixels<byte>());
    }

    [Fact]
    public void Planar_MinIsWhite_inverts_the_grey_plane_only()
    {
        var builder = new TiffBuilder { Data = [0, 255, 10, 200] };
        builder.AddDirectory().Image(2, 1, samples: 2, photometric: 0).Add(338, TiffTagType.Short, 2).Add(284, TiffTagType.Short, 2)
            .Remove(273).Remove(279).Add(273, TiffTagType.Long, 8, 10).Add(279, TiffTagType.Long, 2, 2);

        Assert.Equal(new byte[] { 255, 10, 0, 200 }, Open(builder).ReadRawPixels<byte>());
    }

    [Fact]
    public void Floating_point_MinIsWhite_is_negated()
    {
        var builder = new TiffBuilder { Data = [.. BitConverter.GetBytes(0.25f), .. BitConverter.GetBytes(-2f)] };
        builder.AddDirectory().Image(2, 1, bits: 32, photometric: 0).Add(339, TiffTagType.Short, 3);

        Assert.Equal(new[] { -0.25f, 2f }, Open(builder).ReadRawPixels<float>());
    }

    [Fact]
    public void The_extra_channels_of_a_palette_image_are_normalised_by_their_own_depth()
    {
        // Two entries of 8 bits: index 1 is (65535, 32768, 0); the alpha sample is 51.
        ushort[] map = new ushort[3 * 256];
        (map[1], map[256 + 1], map[512 + 1]) = (65535, 32768, 0);
        var builder = new TiffBuilder { Data = [1, 51] };
        builder.AddDirectory().Image(1, 1, samples: 2, photometric: 3).Add(338, TiffTagType.Short, 2)
            .Add(320, TiffTagType.Short, [.. map.Select(v => (long)v)]);
        var image = Open(builder);

        Assert.Equal(new ushort[] { 65535, 32768, 0, 51 }, image.ReadRawPixels<ushort>());
        Assert.Equal(new[] { 1.0, 32768 / 65535.0, 0, 51 / 255.0 }, image.ReadPixels<double>(TiffPixelScale.Normalized));
        Assert.Equal(new[] { 51 / 255.0 }, image.ReadChannel<double>(3, TiffPixelScale.Normalized));
    }

    [Fact]
    public void Normalised_samples_are_divided_exactly_as_numpy_does()
    {
        var builder = new TiffBuilder { Data = [.. Enumerable.Range(0, 256).Select(v => (byte)v)] };
        builder.AddDirectory().Image(256, 1);

        Assert.Equal(Enumerable.Range(0, 256).Select(v => v / 255.0), Open(builder).ReadPixels<double>(TiffPixelScale.Normalized));
    }

    [Fact]
    public void Signed_samples_are_normalised_by_their_largest_positive_value()
    {
        var builder = new TiffBuilder { Data = [.. BitConverter.GetBytes((short)-32768), .. BitConverter.GetBytes((short)16384)] };
        builder.AddDirectory().Image(2, 1, bits: 16).Add(339, TiffTagType.Short, 2);

        Assert.Equal(new[] { -32768 / 32767.0, 16384 / 32767.0 }, Open(builder).ReadPixels<double>(TiffPixelScale.Normalized));
    }

    [Fact]
    public void Big_endian_12_bit_samples_are_a_stream_of_bits_most_significant_first()
    {
        // 0xABC and 0x123 packed in three bytes, whatever the byte order of the file.
        var builder = new TiffBuilder(littleEndian: false) { Data = [0xAB, 0xC1, 0x23] };
        builder.AddDirectory().Image(2, 1, bits: 12);

        Assert.Equal(new ushort[] { 0xABC, 0x123 }, Open(builder).ReadRawPixels<ushort>());
    }

    [Fact]
    public void Big_endian_16_bit_samples_are_swapped()
    {
        var builder = new TiffBuilder(littleEndian: false) { Data = [0x12, 0x34, 0xAB, 0xCD] };
        builder.AddDirectory().Image(2, 1, bits: 16);

        Assert.Equal(new ushort[] { 0x1234, 0xABCD }, Open(builder).ReadRawPixels<ushort>());
    }

    [Fact]
    public void FillOrder_2_reverses_the_bits_of_each_byte()
    {
        var builder = new TiffBuilder { Data = [0b0000_0001] };   // 1000 0000 once reversed
        builder.AddDirectory().Image(8, 1, bits: 1).Add(266, TiffTagType.Short, 2);

        Assert.Equal(new byte[] { 1, 0, 0, 0, 0, 0, 0, 0 }, Open(builder).ReadRawPixels<byte>());
    }

    /// <summary>A tiled, planar, oriented image of random samples, of several tiles per plane, with partial edge tiles.</summary>
    private static TiffBuilder Tiled(int width, int height, int tile, ushort orientation, out byte[] samples)
    {
        int across = (width + tile - 1) / tile, down = (height + tile - 1) / tile, tiles = across * down * 3;
        samples = new byte[tiles * tile * tile];
        new Random(width * height).NextBytes(samples);
        var builder = new TiffBuilder { Data = samples };
        builder.AddDirectory().Image(width, height, samples: 3, photometric: 2).Remove(273).Remove(278).Remove(279)
            .Add(284, TiffTagType.Short, 2).Add(274, TiffTagType.Short, orientation)
            .Add(322, TiffTagType.Short, tile).Add(323, TiffTagType.Short, tile)
            .Add(324, TiffTagType.Long, [.. Enumerable.Range(0, tiles).Select(i => builder.DataOffset + (long)i * tile * tile)])
            .Add(325, TiffTagType.Long, [.. Enumerable.Repeat((long)tile * tile, tiles)]);
        return builder;
    }

    [Theory]
    [InlineData(1)]
    [InlineData(6)]
    [InlineData(7)]
    public void A_tiled_planar_oriented_read_does_not_depend_on_the_number_of_threads(ushort orientation)
    {
        var builder = Tiled(70, 45, 16, orientation, out _);
        var one = Open(builder, TiffReadOptions.Default with { MaxDegreeOfParallelism = 1 });
        var many = Open(builder, TiffReadOptions.Default with { MaxDegreeOfParallelism = 8 });

        Assert.Equal(one.ReadPixels<float>(), many.ReadPixels<float>());
        Assert.Equal(one.ReadRegion<double>(5, 3, 30, 33, order: TiffPixelOrder.Planar), many.ReadRegion<double>(5, 3, 30, 33, order: TiffPixelOrder.Planar));
        Assert.Equal(one.ReadChannel<byte>(2), many.ReadChannel<byte>(2));
    }

    [Fact]
    public void Tiled_planar_samples_are_where_the_tiles_put_them()
    {
        var image = Open(Tiled(20, 18, 16, 1, out byte[] samples));
        var planar = image.ReadRawPixels<byte>(TiffPixelOrder.Planar);

        // Tiles of 16 x 16, 2 across and 2 down per plane: sample (c, y, x) is in tile c * 4 + (y / 16) * 2 + x / 16.
        for (int c = 0; c < 3; c++)
        {
            for (int y = 0; y < 18; y++)
            {
                for (int x = 0; x < 20; x++)
                {
                    int tile = c * 4 + y / 16 * 2 + x / 16;
                    Assert.Equal(samples[tile * 256 + y % 16 * 16 + x % 16], planar[(c * 18 + y) * 20 + x]);
                }
            }
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    public void A_read_is_cancelled_between_tiles(int threads)
    {
        // 64 tiles; the source cancels the read when the tenth is read: the token is checked between tiles, not only at the start.
        var builder = new TiffBuilder { Data = new byte[128 * 128] };
        builder.AddDirectory().Image(128, 128).Remove(273).Remove(278).Remove(279)
            .Add(322, TiffTagType.Short, 16).Add(323, TiffTagType.Short, 16)
            .Add(324, TiffTagType.Long, [.. Enumerable.Range(0, 64).Select(i => builder.DataOffset + i * 256L)])
            .Add(325, TiffTagType.Long, [.. Enumerable.Repeat(256L, 64)]);
        byte[] bytes = builder.Build();
        var image = Open(builder);
        using var cancellation = new CancellationTokenSource();
        using var source = new CancellingSource(bytes, cancellation, after: 10);
        var budget = new ResourceBudget(TiffReadOptions.Default with { MaxDegreeOfParallelism = threads }, "test", cancellation.Token);
        var request = new PixelRequest(0, 0, 128, 128, null, TiffPixelOrder.Interleaved, TiffPixelScale.Normalized);
        var plan = PixelPlan.Create(image.Descriptor, budget, request, 1, parallel: true);
        var destination = new double[128 * 128];

        Assert.ThrowsAny<OperationCanceledException>(() =>
            PixelReader.Read<byte, double>(plan, source, budget, 1, littleEndian: true, destination, destination));
        Assert.True(source.Reads < 64, $"{source.Reads} tiles read");
    }

    /// <summary>The bytes of a file in memory; cancels a token once a number of reads is reached.</summary>
    private sealed class CancellingSource(byte[] data, CancellationTokenSource cancellation, int after) : TiffSource("test", data.Length)
    {
        private int _reads;

        public int Reads => Volatile.Read(ref _reads);

        protected override void ReadCore(long offset, Span<byte> destination)
        {
            if (Interlocked.Increment(ref _reads) == after)
            {
                cancellation.Cancel();
            }

            data.AsSpan((int)offset, destination.Length).CopyTo(destination);
        }
    }
}

/// <summary>Files that declare more than they hold: a read fails before it allocates for what is declared.</summary>
[Collection(nameof(Measurement))]
public class HostilePixelTests
{
    [Fact]
    public void A_tiny_file_declaring_a_huge_image_fails_before_allocating_its_pixels()
    {
        // 10 000 x 10 000 grey pixels (100 MB as bytes, 800 MB as doubles) in one strip of 16 bytes.
        var builder = new TiffBuilder { Data = new byte[16] };
        builder.AddDirectory().Image(10_000, 10_000);
        using var file = TiffFile.Read(new MemoryStream(builder.Build()), TiffReadOptions.Default with { MaxPixelBytes = 1L << 30 });

        HostileAssert.FailsWithinBounds(() => file.Primary.ReadPixels<double>(), "TiffException", TimeSpan.FromSeconds(1), 1 << 20, "huge image");
        byte[] buffer = new byte[100_000_000];
        HostileAssert.FailsWithinBounds(() => file.Primary.ReadPixels<byte>(buffer), "TiffException", TimeSpan.FromSeconds(1), 1 << 20,
            "huge image into a span");
    }

    [Fact]
    public void A_narrow_region_of_a_wide_strip_costs_its_rows_only()
    {
        // One row of 4 million pixels: the region of 1 x 1 reads one chunk of the row, not the row 4 times over.
        var builder = new TiffBuilder { Data = new byte[4_000_000] };
        builder.AddDirectory().Image(4_000_000, 1);
        using var file = TiffFile.Read(new MemoryStream(builder.Build()));

        GC.Collect();
        long before = GC.GetTotalAllocatedBytes(precise: true);
        Assert.Equal(new byte[1], file.Primary.ReadRegion<byte>(3_999_999, 0, 1, 1));
        long allocated = GC.GetTotalAllocatedBytes(precise: true) - before;

        Assert.True(allocated < 20_000_000, $"{allocated} bytes allocated");
    }

    [Fact]
    public void A_count_of_samples_beyond_long_saturates_instead_of_wrapping()
    {
        var request = new PixelRequest(0, 0, int.MaxValue, int.MaxValue, null, TiffPixelOrder.Interleaved, TiffPixelScale.Stored);

        Assert.Equal(long.MaxValue, request.Count(1 << 20));
        Assert.Equal((long)int.MaxValue * int.MaxValue * 2, request.Count(2));
    }

    [Fact]
    public void An_image_of_more_samples_than_an_array_is_refused_without_allocating()
    {
        // Within the limit of the application, beyond what an array holds (with the default limit, MaxPixelBytes refuses first).
        var limited = new TiffBuilder { Data = new byte[16] };
        limited.AddDirectory().Image(1 << 20, 1 << 20);
        using (var defaults = TiffFile.Read(new MemoryStream(limited.Build())))
        {
            Assert.Equal("MaxPixelBytes", Assert.Throws<TiffException>(() => defaults.Primary.ReadPixels<byte>()).Limit);
        }

        AnImageBeyondAnArray();
    }

    private static void AnImageBeyondAnArray()
    {
        var builder = new TiffBuilder { Data = new byte[16] };
        builder.AddDirectory().Image(1 << 20, 1 << 20);
        using var file = TiffFile.Read(new MemoryStream(builder.Build()), TiffReadOptions.Default with { MaxPixelBytes = long.MaxValue });

        HostileAssert.FailsWithinBounds(() => file.Primary.ReadPixels<byte>(), "InvalidOperationException", TimeSpan.FromSeconds(1), 1 << 20, "2^40 samples");
    }

    [Fact]
    public void A_row_larger_than_an_array_makes_the_image_unsupported()
    {
        var builder = new TiffBuilder { Data = new byte[16] };
        builder.AddDirectory().Image(int.MaxValue, 1, bits: 16, samples: 3, photometric: 2);
        using var file = TiffFile.Read(new MemoryStream(builder.Build()));

        Assert.Contains("larger than an array", file.Primary.UnsupportedReason, StringComparison.Ordinal);
    }
}
