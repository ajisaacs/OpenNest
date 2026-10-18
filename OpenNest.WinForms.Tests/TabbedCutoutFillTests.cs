using System.Drawing;
using System.Drawing.Drawing2D;
using OpenNest.CNC;
using OpenNest.Geometry;

namespace OpenNest.WinForms.Tests;

public class TabbedCutoutFillTests
{
    [Theory]
    [InlineData(Mode.Absolute)]
    [InlineData(Mode.Incremental)]
    public void CutoutCutAroundTabsFillsAsOneHole(Mode mode)
    {
        // A 4x4 cutout cut in two pieces with lead-ins/outs, leaving 0.2 tabs on its left and
        // right edges, inside a closed 10x10 perimeter.
        var program = new CNC.Program(Mode.Absolute);
        program.Codes.AddRange(new ICode[]
        {
            new RapidMove(3.2, 5.3),
            new LinearMove(3, 5.1) { Layer = LayerType.Leadin },
            new LinearMove(3, 7),
            new LinearMove(7, 7),
            new LinearMove(7, 5.1),
            new LinearMove(6.8, 5.3) { Layer = LayerType.Leadout },
            new RapidMove(6.8, 4.7),
            new LinearMove(7, 4.9) { Layer = LayerType.Leadin },
            new LinearMove(7, 3),
            new LinearMove(3, 3),
            new LinearMove(3, 4.9),
            new LinearMove(3.2, 4.7) { Layer = LayerType.Leadout },
            new RapidMove(0, 0),
            new LinearMove(10, 0),
            new LinearMove(10, 10),
            new LinearMove(0, 10),
            new LinearMove(0, 0),
        });
        program.Mode = mode;

        program.GetDisplayPaths(Vector.Zero, out var cut, out var lead, out var etch, out var hasOpenCuts);
        using (cut)
        using (lead)
        using (etch)
        using (var fill = program.GetFillPath(Vector.Zero))
        {
            Assert.True(hasOpenCuts);
            // Filling the cut outline closes each piece with a chord and fills the band between them.
            Assert.True(Covers(cut, 5, 5));
            Assert.False(Covers(fill, 5, 5));
            Assert.False(Covers(fill, 4, 6.5));
            Assert.False(Covers(fill, 6, 3.5));
            Assert.True(Covers(fill, 1, 5));
            Assert.True(Covers(fill, 8.5, 8.5));
        }
    }

    [Fact]
    public void ClosedContoursReportNoOpenCuts()
    {
        var program = new CNC.Program(Mode.Absolute);
        program.Codes.AddRange(new ICode[]
        {
            new RapidMove(0, 0),
            new LinearMove(10, 0),
            new LinearMove(10, 10),
            new LinearMove(0, 0),
            new RapidMove(6, 3),
            new ArcMove(6, 3, 5, 3),
        });

        program.GetDisplayPaths(Vector.Zero, out var cut, out var lead, out var etch, out var hasOpenCuts);
        cut.Dispose();
        lead.Dispose();
        etch.Dispose();

        Assert.False(hasOpenCuts);
    }

    // GraphicsPath.IsVisible tests on a one-unit pixel grid, so scale up before hit-testing.
    private static bool Covers(GraphicsPath path, double x, double y)
    {
        using var scaled = (GraphicsPath)path.Clone();
        using var scale = new Matrix();
        scale.Scale(1000, 1000);
        scaled.Transform(scale);
        return scaled.IsVisible((float)(x * 1000), (float)(y * 1000));
    }
}
