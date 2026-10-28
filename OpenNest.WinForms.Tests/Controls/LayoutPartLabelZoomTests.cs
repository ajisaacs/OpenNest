using System.Drawing;
using System.Drawing.Drawing2D;
using OpenNest.Controls;
using OpenNest.Geometry;

namespace OpenNest.WinForms.Tests.Controls;

/// <summary>
/// As the CAD converter numbers entities, a part's id shows only once the zoom makes the part big
/// enough on screen to hold it; zoomed out, small parts would otherwise pile their ids on each other.
/// </summary>
public class LayoutPartLabelZoomTests
{
    [Theory]
    [InlineData(1f, false)]
    [InlineData(6f, true)]
    public void PartIdShowsOnceThePartCanHoldIt(float scale, bool shown) => StaTestThread.Run(() =>
    {
        using var view = new PlateView(ColorSchemeRegistry.Get("Workshop"));
        view.Matrix = new Matrix(scale, 0, 0, scale, 0, 0);
        var layout = LayoutPart.Create(new Part(new Drawing("Washer", Square(10)), new Vector(1, 1)), view);

        using var withoutId = new Bitmap(80, 80);
        using (var g = Graphics.FromImage(withoutId))
        {
            g.Clear(Color.White);
            layout.Draw(g, muted: false);
        }
        using var withId = new Bitmap(80, 80);
        using (var g = Graphics.FromImage(withId))
        {
            g.Clear(Color.White);
            layout.Draw(g, "47");
        }

        Assert.Equal(shown, !SamePixels(withoutId, withId));
        layout.ReleaseDisplayResources();
    }, TimeSpan.FromMinutes(1), "Part id zoom test timed out.");

    [Theory]
    [InlineData(0.1f, false)]
    [InlineData(1f, true)]
    public void CutOffSegmentIdShowsOnceTheSegmentCanHoldIt(float scale, bool shown) => StaTestThread.Run(() =>
    {
        using var view = new PlateView(ColorSchemeRegistry.Get("Workshop"));
        view.Matrix = new Matrix(scale, 0, 0, scale, 0, 0);
        view.Plate.Size = new OpenNest.Geometry.Size(100, 100);
        view.Plate.Quadrant = 1;
        view.Plate.CutOffs.Add(new CutOff(new Vector(50, 0), CutOffAxis.Vertical));
        view.Plate.RegenerateCutOffs(new CutOffSettings());
        var segment = Assert.Single(view.Plate.Parts, p => p.BaseDrawing.IsCutOff);
        var layout = LayoutPart.Create(segment, view);

        using var image = new Bitmap(120, 120);
        using var g = Graphics.FromImage(image);
        g.Clear(Color.White);
        layout.DrawLabel(g, "12", Brushes.Yellow, Brushes.Black);

        Assert.Equal(shown, !IsBlank(image));
        layout.ReleaseDisplayResources();
    }, TimeSpan.FromMinutes(1), "Cut-off segment id zoom test timed out.");

    private static bool SamePixels(Bitmap a, Bitmap b)
    {
        for (var y = 0; y < a.Height; y++)
            for (var x = 0; x < a.Width; x++)
                if (a.GetPixel(x, y) != b.GetPixel(x, y))
                    return false;
        return true;
    }

    private static bool IsBlank(Bitmap image)
    {
        var white = Color.White.ToArgb();
        for (var y = 0; y < image.Height; y++)
            for (var x = 0; x < image.Width; x++)
                if (image.GetPixel(x, y).ToArgb() != white)
                    return false;
        return true;
    }

    private static OpenNest.CNC.Program Square(double size)
    {
        var program = new OpenNest.CNC.Program();
        program.MoveTo(0, 0);
        program.LineTo(size, 0);
        program.LineTo(size, size);
        program.LineTo(0, size);
        program.LineTo(0, 0);
        return program;
    }
}
