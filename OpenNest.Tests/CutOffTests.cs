using OpenNest.CNC;

namespace OpenNest.Tests;

public class CutOffTests
{
    [Fact]
    public void Drawing_IsCutOff_DefaultsFalse()
    {
        var drawing = new Drawing("test", new Program());
        Assert.False(drawing.IsCutOff);
    }
}
