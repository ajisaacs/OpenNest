using System.Drawing;
using System.Windows.Forms;

namespace OpenNest.WinForms.Tests;

public class GdiSchemeToolsTests
{
    [Fact]
    public void SharedShadeMatchesControlPaintDark()
    {
        var colors = new List<Color> { SystemColors.Control, Color.Coral, Color.White, Color.Black };
        for (var r = 0; r < 256; r += 15)
        {
            for (var g = 0; g < 256; g += 17)
            {
                for (var b = 0; b < 256; b += 13)
                    colors.Add(Color.FromArgb(r, g, b));
            }
        }

        Assert.All(colors, color => Assert.Equal(ControlPaint.Dark(color), ColorShade.Dark(color)));
        Assert.All(colors, color => Assert.Equal(ControlPaint.Dark(color, 0.25f), ColorShade.Dark(color, 0.25f)));
    }

    [Fact]
    public void PensFollowTheSchemeAndAreRebuiltAfterAColorChange()
    {
        var scheme = ColorSchemeSerializer.Deserialize(ColorSchemeSerializer.Serialize(ColorSchemeRegistry.Get("Classic")));
        var tools = scheme.Gdi();

        Assert.Same(tools, scheme.Gdi());
        Assert.Equal(scheme.RapidColor.ToArgb(), tools.RapidPen.Color.ToArgb());
        Assert.Equal(Color.FromArgb(30, scheme.PreviewPartColor).ToArgb(),
            ((SolidBrush)tools.ActivePreviewPartBrush).Color.ToArgb());

        scheme.LayoutFillColor = Color.Coral;
        var rebuilt = scheme.Gdi();

        Assert.NotSame(tools, rebuilt);
        Assert.Equal(Color.Coral.ToArgb(), ((SolidBrush)rebuilt.LayoutFillBrush).Color.ToArgb());
    }

    [Fact]
    public void EachThreadDrawsWithItsOwnPens()
    {
        var scheme = ColorSchemeRegistry.Get("Workshop");
        var here = scheme.Gdi();
        GdiSchemeTools? other = null;
        var thread = new Thread(() => other = scheme.Gdi());
        thread.Start();
        thread.Join();

        Assert.NotNull(other);
        Assert.NotSame(here, other);
        Assert.NotSame(here.LayoutOutlinePen, other.LayoutOutlinePen);
    }
}
