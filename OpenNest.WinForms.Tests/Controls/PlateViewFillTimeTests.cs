using OpenNest.Controls;

namespace OpenNest.WinForms.Tests.Controls;

public class PlateViewFillTimeTests
{
    [Theory]
    [InlineData(0, "0 ms")]
    [InlineData(850, "850 ms")]
    [InlineData(999, "999 ms")]
    [InlineData(1000, "1.0 s")]
    [InlineData(2400, "2.4 s")]
    [InlineData(59960, "59.9 s")]
    [InlineData(60000, "1m 00s")]
    [InlineData(125000, "2m 05s")]
    public void FillTimeIsShownInTheLargestUnitThatFits(int milliseconds, string expected) =>
        Assert.Equal(expected, PlateView.FormatFillTime(TimeSpan.FromMilliseconds(milliseconds)));
}
