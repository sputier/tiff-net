using System.Numerics;
using Eleris.TiffNet.Internal.Jpeg;
using Eleris.TiffNet.Internal.Pixels;

namespace Eleris.TiffNet.Tests;

/// <summary>Classes that were only tested through the public API, tested directly: the conversions allowed, the bits of JPEG.</summary>
public class DirectTests
{
    private static readonly Type[] Targets =
        [typeof(byte), typeof(sbyte), typeof(ushort), typeof(short), typeof(uint), typeof(int), typeof(ulong), typeof(long), typeof(Half), typeof(float), typeof(double)];

    public static TheoryData<TiffSampleType> SampleTypeValues() => [.. Enum.GetValues<TiffSampleType>()];

    [Theory]
    [MemberData(nameof(SampleTypeValues))]
    public void Every_pair_of_types_is_allowed_exactly_when_no_value_can_be_lost(TiffSampleType stored)
    {
        var source = SampleTypes.Of(stored).ClrType;
        foreach (var target in Targets)
        {
            // The rule: to float or double always (the documented exception of 64-bit integers), to the same type, or to an
            // integer type that holds every value; normalised only to float or double; raw only to the stored type.
            bool lossless = target == source || target == typeof(float) || target == typeof(double) || Holds(source, target);
            Assert.Equal(lossless, Allowed(stored, target, TiffPixelScale.Stored, raw: false));
            Assert.Equal(target == typeof(float) || target == typeof(double), Allowed(stored, target, TiffPixelScale.Normalized, raw: false));
            Assert.Equal(target == source, Allowed(stored, target, TiffPixelScale.Stored, raw: true));
        }
    }

    [Fact]
    public void Stuffed_bytes_are_data_and_a_marker_ends_it()
    {
        // 0xFF 0x00 is the byte 0xFF; 0xFF 0xD9 (EOI) ends the data: what follows reads as zeros, and is insufficient.
        byte[] data = [0xAB, 0xFF, 0x00, 0x5C, 0xFF, 0xD9, 0x12];
        var reader = new JpegBitReader(data, 0);

        Assert.Equal(0xAB, reader.Read(8));
        Assert.Equal(0xFF, reader.Read(8));
        Assert.Equal(0x5C, reader.Read(8));
        Assert.False(reader.Insufficient);
        Assert.Equal(0, reader.Read(8));
        Assert.True(reader.Insufficient);
        Assert.Equal(4, reader.Position);   // at the marker
    }

    [Fact]
    public void A_restart_marker_drops_the_bits_left_and_resumes()
    {
        byte[] data = [0b1010_0000, 0xFF, 0xD0, 0x7E];
        var reader = new JpegBitReader(data, 0);

        Assert.Equal(0b101, reader.Read(3));
        reader.Restart();

        Assert.Equal(0x7E, reader.Read(8));
        Assert.False(reader.Insufficient);
    }

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(1, 0b1, 1)]
    [InlineData(1, 0b0, -1)]
    [InlineData(3, 0b101, 5)]
    [InlineData(3, 0b010, -5)]
    [InlineData(11, 0b000_0000_0000, -2047)]
    public void Differences_are_extended_as_the_standard_says(int size, int bits, int expected)
    {
        // The bits at the top of the data, then zeros.
        ulong word = size == 0 ? 0 : (ulong)bits << (64 - size);
        byte[] data = [.. BitConverter.GetBytes(word).Reverse(), 0];
        var reader = new JpegBitReader(data, 0);

        Assert.Equal(expected, reader.ReadDifference(size));
    }

    private static bool Holds(Type source, Type target)
    {
        var (sb, ss, si) = Describe(source);
        var (tb, ts, ti) = Describe(target);
        return si && ti && (ss ? ts && tb >= sb : ts ? tb > sb : tb >= sb);
    }

    private static (int Bits, bool Signed, bool Integer) Describe(Type t) =>
        t == typeof(Half) || t == typeof(float) || t == typeof(double) ? (0, true, false)
        : (System.Runtime.InteropServices.Marshal.SizeOf(t) * 8, t == typeof(sbyte) || t == typeof(short) || t == typeof(int) || t == typeof(long), true);

    private static bool Allowed(TiffSampleType stored, Type target, TiffPixelScale scale, bool raw)
    {
        try
        {
            typeof(DirectTests).GetMethod(nameof(Check), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
                .MakeGenericMethod(target).Invoke(null, [stored, scale, raw]);
            return true;
        }
        catch (System.Reflection.TargetInvocationException ex) when (ex.InnerException is InvalidOperationException)
        {
            return false;
        }
    }

    private static void Check<T>(TiffSampleType stored, TiffPixelScale scale, bool raw) where T : unmanaged, INumber<T> =>
        SampleConversion.Check<T>(stored, scale, raw);
}
