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
