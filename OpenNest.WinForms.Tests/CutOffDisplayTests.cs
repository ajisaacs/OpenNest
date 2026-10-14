using System.Drawing;
using System.Drawing.Drawing2D;
using OpenNest.Controls;
using OpenNest.Geometry;

namespace OpenNest.WinForms.Tests;

public class CutOffDisplayTests
{
    private static readonly Color OldLineGray = Color.FromArgb(64, 64, 64);
    private static readonly Color CutoffLine = Color.OrangeRed;
    private static readonly Color SelectedBlue = Color.FromArgb(0, 120, 255);

    // A 100x100 quadrant-4 sheet puts a vertical cut-off at x=50 on the
    // graph column x=50, rows 0..100 (PointWorldToGraph flips Y).
    private static (PlateView View, CutOff Cutoff) BuildPlateWithCutOff()
    {
        var view = new PlateView(ColorSchemeRegistry.Get("Workshop"))
        {
            Size = new System.Drawing.Size(100, 100),
        };
        view.Matrix = new Matrix();
        view.Plate.Size = new OpenNest.Geometry.Size(100, 100);
        view.Plate.Quadrant = 4;
        var cutoff = new CutOff(new Vector(50, 0), CutOffAxis.Vertical);
        view.Plate.CutOffs.Add(cutoff);
        view.Plate.RegenerateCutOffs(new CutOffSettings());
        return (view, cutoff);
    }

    private static (int Orange, int White, int OldGray, int Blue) CountLinePixels(Bitmap image)
    {
        var orange = 0;
        var white = 0;
        var oldGray = 0;
        var blue = 0;

        for (var y = 3; y <= 96; y++)
        {
            for (var x = 48; x <= 52; x++)
            {
                var c = image.GetPixel(x, y);

                if (c.R >= 200 && c.G <= 120 && c.B <= 80)
                    orange++;
                else if (c.R >= 240 && c.G >= 240 && c.B >= 240)
                    white++;
                else if (
                    System.Math.Abs(c.R - OldLineGray.R) <= 32
                    && System.Math.Abs(c.G - OldLineGray.G) <= 32
                    && System.Math.Abs(c.B - OldLineGray.B) <= 32
                )
                    oldGray++;
                else if (c.B >= 200 && c.R <= 60)
                    blue++;
            }
        }

        return (orange, white, oldGray, blue);
    }

    [Fact]
    public void CutOffLineIsDashedOrangeNotSolidBlack() => StaTestThread.Run(() =>
    {
        var (view, _) = BuildPlateWithCutOff();
        using (view)
        using (var image = new Bitmap(100, 100))
        using (var g = Graphics.FromImage(image))
        {
            g.Clear(Color.White);
            view.Renderer.DrawCutOffs(g);

            var (orange, white, oldGray, _) = CountLinePixels(image);
            Assert.True(orange >= 10, $"Expected the orange cut-off dash, found {orange} pixels.");
            Assert.True(white >= 10, $"Expected dashed gaps along the cut-off line, found {white} white pixels.");
            Assert.Equal(0, oldGray);
        }
    }, TimeSpan.FromMinutes(1), "Cut-off display test timed out.");

    [Fact]
    public void SelectedCutOffKeepsSolidBlueHighlight() => StaTestThread.Run(() =>
    {
        var (view, cutoff) = BuildPlateWithCutOff();
        using (view)
        {
            view.Selection.SelectedCutOffs.Add(cutoff);
            using var image = new Bitmap(100, 100);
            using var g = Graphics.FromImage(image);
            g.Clear(Color.White);
            view.Renderer.DrawCutOffs(g);

            var (orange, _, oldGray, blue) = CountLinePixels(image);
            Assert.True(blue >= 20, $"Expected the solid blue selection line, found {blue} pixels.");
            Assert.Equal(0, orange);
            Assert.Equal(0, oldGray);
        }
    }, TimeSpan.FromMinutes(1), "Selected cut-off display test timed out.");
}
