using System.Globalization;
using OpenNest.Forms;

namespace OpenNest.WinForms.Tests.Forms;

public class MainStatusBarTests
{
    [Fact]
    public void FormatMaterial_ShowsNameAndThicknessWithUnits()
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        try
        {
            Assert.Equal("Material: A36 · 0.25 in", MainStatusBar.FormatMaterial("A36", 0.25, Units.Inches));
            Assert.Equal("Material: 304 SS · 3 mm", MainStatusBar.FormatMaterial(" 304 SS ", 3, Units.Millimeters));
            Assert.Equal("Material: A36", MainStatusBar.FormatMaterial("A36", 0, Units.Inches));
            Assert.Equal("Material: 0.1875 in", MainStatusBar.FormatMaterial(null, 0.1875, Units.Inches));
            Assert.Equal("Material: (none)", MainStatusBar.FormatMaterial("", 0, Units.Inches));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }
}
