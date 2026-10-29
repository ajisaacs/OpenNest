using System.Drawing;
using System.Drawing.Drawing2D;
using OpenNest.Controls;
using OpenNest.Geometry;

namespace OpenNest.WinForms.Tests.Controls;

/// <summary>The plate view draws an outlined sheet as its outline: the missing corner shows the
/// background and an obstacle is hatched, in the plate's quadrant.</summary>
public class OutlinedPlateDisplayTests
{
    // A 100 x 100 quadrant-4 sheet missing its upper-right 50 x 50 corner, with a 10 x 10 burned
    // patch at (10..20, 70..80). PointWorldToGraph flips Y, so plate (x, y) lands on graph (x, 100 - y).
    private static PlateView BuildView()
    {
        var view = new PlateView(ColorSchemeRegistry.Get("Workshop"))
        {
            Size = new System.Drawing.Size(100, 100),
        };
        view.Matrix = new Matrix();
        view.Plate.Quadrant = 4;
        view.Plate.Outline = new SheetOutline(
            new[] { new Vector(0, 0), new Vector(100, 0), new Vector(100, 50), new Vector(50, 50), new Vector(50, 100), new Vector(0, 100) },
            new[] { new[] { new Vector(10, 70), new Vector(20, 70), new Vector(20, 80), new Vector(10, 80) } });
        return view;
    }

    [Fact]
    public void MissingCornerShowsTheBackgroundAndTheRestIsFilled() => StaTestThread.Run(() =>
    {
        using var view = BuildView();
        using var image = new Bitmap(100, 100);
        using var g = Graphics.FromImage(image);
        g.Clear(Color.White);

        view.Renderer.DrawPlate(g);

        var fill = ((SolidBrush)view.ColorScheme.LayoutFillBrush).Color;
        Assert.Equal(Color.White.ToArgb(), image.GetPixel(75, 25).ToArgb()); // the missing corner
        Assert.Equal(fill.ToArgb(), image.GetPixel(25, 75).ToArgb()); // the sheet
        Assert.Equal(fill.ToArgb(), image.GetPixel(75, 75).ToArgb()); // the arm
        Assert.NotEqual(fill.ToArgb(), image.GetPixel(15, 25).ToArgb()); // the hatched obstacle
        Assert.NotEqual(Color.White.ToArgb(), image.GetPixel(15, 25).ToArgb());
    }, TimeSpan.FromMinutes(1), "Outlined plate display test timed out.");
}
