using System.Drawing;
using System.Drawing.Drawing2D;
using OpenNest.Controls;
using OpenNest.Geometry;

namespace OpenNest.WinForms.Tests.Controls;

/// <summary>
/// Each segment of a cut-off is its own part of the one cut-off drawing. Its sequence label must
/// sit on its own cut: labelled from the whole drawing, every segment stacked its number on one
/// point, and the view's culling decided which one showed as the user zoomed.
/// </summary>
public class CutOffSegmentLabelTests
{
    [Fact]
    public void EachSegmentIsLabelledAtTheMiddleOfItsOwnCut() => StaTestThread.Run(() =>
    {
        using var view = new PlateView(ColorSchemeRegistry.Get("Workshop"));
        view.Matrix = new Matrix();
        view.Plate.Size = new OpenNest.Geometry.Size(100, 100);
        view.Plate.Quadrant = 1;
        view.Plate.Parts.Add(new Part(new Drawing("Block", Square(20)), new Vector(40, 40)));
        view.Plate.CutOffs.Add(new CutOff(new Vector(50, 0), CutOffAxis.Vertical));
        view.Plate.RegenerateCutOffs(new CutOffSettings());

        var segments = view.Plate.Parts.Where(p => p.BaseDrawing.IsCutOff).ToList();
        Assert.Equal(2, segments.Count);

        var labels = new List<float>();
        foreach (var segment in segments)
        {
            var layout = LayoutPart.Create(segment, view);
            var extent = CutOffSegments.Extent(segment, vertical: true)!.Value;
            Assert.Equal(50, layout.LabelScreenPoint.X, 3);
            Assert.Equal((extent.Start + extent.End) / 2, layout.LabelScreenPoint.Y, 3);
            labels.Add(layout.LabelScreenPoint.Y);
            layout.ReleaseDisplayResources();
        }

        Assert.NotEqual(labels[0], labels[1]);
    }, TimeSpan.FromMinutes(1), "Cut-off segment label test timed out.");

    [Fact]
    public void CutLineDoesNotStrikeThroughASegmentsId() => StaTestThread.Run(() =>
    {
        // A quadrant-4 sheet under a Y-flipping matrix lands parts and cut-off lines on the same
        // pixels: the cut-off at x=50 runs down graph column 50, rows 0..100, painted from the
        // view's origin as OnPaint does so the culling sees the whole sheet.
        using var view = new PlateView(ColorSchemeRegistry.Get("Workshop")) { Size = new System.Drawing.Size(240, 240) };
        view.Matrix = new Matrix(1, 0, 0, -1, 0, 0);
        view.Plate.Size = new OpenNest.Geometry.Size(100, 100);
        view.Plate.Quadrant = 4;
        view.Plate.Parts.Add(new Part(new Drawing("Block", Square(20)), new Vector(40, -60)));
        view.Plate.CutOffs.Add(new CutOff(new Vector(50, 0), CutOffAxis.Vertical));
        view.Plate.RegenerateCutOffs(new CutOffSettings());

        var viewBounds = view.GetViewBounds();
        var (dx, dy) = ((int)-viewBounds.X, (int)-viewBounds.Y);
        using var image = new Bitmap(view.Width, view.Height);
        using var g = Graphics.FromImage(image);
        g.Clear(Color.White);
        g.TranslateTransform(dx, dy);
        view.Renderer.DrawParts(g);
        view.Renderer.DrawCutOffs(g);

        var labels = view.LayoutParts.Where(p => p.BasePart.BaseDrawing.IsCutOff)
            .Select(p => Point.Round(p.LabelScreenPoint) + new System.Drawing.Size(dx, dy)).ToList();
        Assert.Equal(2, labels.Count);
        Assert.True(CountOrange(image, dx + 48, dx + 52, dy, dy + 99) >= 10, "Expected the cut-off line outside its ids.");
        foreach (var label in labels)
            Assert.Equal(0, CountOrange(image, label.X - 2, label.X + 2, label.Y - 4, label.Y + 4));
    }, TimeSpan.FromMinutes(1), "Cut-off segment id background test timed out.");

    private static int CountOrange(Bitmap image, int left, int right, int top, int bottom)
    {
        var count = 0;
        for (var y = top; y <= bottom; y++)
        {
            for (var x = left; x <= right; x++)
            {
                var c = image.GetPixel(x, y);
                if (c.R >= 200 && c.G <= 120 && c.B <= 80)
                    count++;
            }
        }
        return count;
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
