namespace Eleris.TiffNet.Tests;

public class TiffExceptionTests
{
    [Fact]
    public void Damaged_data_has_no_limit()
    {
        Assert.Null(new TiffException("damaged").Limit);
        Assert.Null(new TiffException("damaged", new InvalidDataException()).Limit);
    }

    [Fact]
    public void An_exceeded_limit_is_named()
    {
        var ex = new TiffException("too large", "MaxPixelBytes");

        Assert.Equal("MaxPixelBytes", ex.Limit);
        Assert.Equal("too large", ex.Message);
    }
}
