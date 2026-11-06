using System.Numerics;
using System.Runtime.CompilerServices;
using Eleris.TiffNet.Internal.Pixels;

namespace Eleris.TiffNet.Tests;

/// <summary>The one table of the sample types agrees with the enumeration and with the .NET types.</summary>
public class SampleTypesTests
{
    [Fact]
    public void Every_sample_type_is_in_the_table_with_its_type_size_and_format()
    {
        foreach (var type in Enum.GetValues<TiffSampleType>())
        {
            var info = SampleTypes.Of(type);
            Assert.Equal(type, info.Type);
            var visitor = new SizeVisitor();
            SampleTypes.Visit(type, ref visitor);
            Assert.Equal(info.ClrType, visitor.Type);
            Assert.Equal(info.Size, visitor.Size);
            Assert.InRange(info.Format, 1, 3);
        }

        Assert.Equal(TiffSampleType.Float16, SampleTypes.Of<Half>());
        Assert.Null(SampleTypes.Of<Int128>());
        Assert.Throws<ArgumentOutOfRangeException>(() => SampleTypes.Of((TiffSampleType)99));
    }

    private struct SizeVisitor : ISampleVisitor
    {
        public Type? Type;
        public int Size;

        public void Visit<TD>() where TD : unmanaged, INumber<TD>
        {
            Type = typeof(TD);
            Size = Unsafe.SizeOf<TD>();
        }
    }
}
