using System.Drawing;
using System.Drawing.Drawing2D;
using OpenNest.CNC;
using OpenNest.Controls;
using OpenNest.Geometry;
using Program = OpenNest.CNC.Program;

namespace OpenNest.WinForms.Tests.Controls;

/// <summary>
/// A zoom or a move maps a part's cached program-frame paths to the view instead of rebuilding
/// them; the result must match a part built from scratch in the same state.
/// </summary>
public class LayoutPartViewCacheTests
{
    [Fact]
    public void ZoomMoveAndRotateMatchAFreshBuild() => StaTestThread.Run(() =>
    {
        var part = new Part(new Drawing("Holes", PlateWithHoles()), new Vector(5, 7));
        using var view = new PlateView(ColorSchemeRegistry.Get("Workshop"));
        view.Matrix = new Matrix(2, 0, 0, -2, 0, 0);
        var layout = LayoutPart.Create(part, view);
        AssertMatchesFreshBuild(layout, view);

        view.Matrix = new Matrix(3.5f, 0, 0, -3.5f, 0, 0);
        layout.UpdateView(view);
        AssertMatchesFreshBuild(layout, view);

        layout.Offset(12.25, -3.5);
        Assert.True(layout.IsDirty);
        layout.UpdateView(view);
        Assert.False(layout.IsDirty);
        AssertMatchesFreshBuild(layout, view);

        // A rotation of the base part, then a move through the layout, must not reuse stale paths.
        part.Rotate(OpenNest.Math.Angle.ToRadians(30), part.BoundingBox.Center);
        layout.Offset(1, 1);
        layout.UpdateView(view);
        AssertMatchesFreshBuild(layout, view);
    }, TimeSpan.FromMinutes(1), "Layout part view cache test timed out.");

    [Fact]
    public void DirtyRebuildsAfterInPlaceProgramEdit() => StaTestThread.Run(() =>
    {
        var part = new Part(new Drawing("Edited", PlateWithHoles()));
        using var view = new PlateView(ColorSchemeRegistry.Get("Workshop"));
        view.Matrix = new Matrix(2, 0, 0, -2, 0, 0);
        var layout = LayoutPart.Create(part, view);
        var before = layout.Path.PointCount;

        part.Program.MoveTo(5, 25);
        part.Program.LineTo(10, 25);
        layout.IsDirty = true;
        layout.UpdateView(view);

        Assert.True(layout.Path.PointCount > before);
        AssertMatchesFreshBuild(layout, view);
    }, TimeSpan.FromMinutes(1), "Layout part rebuild test timed out.");

    private static void AssertMatchesFreshBuild(LayoutPart layout, PlateView view)
    {
        var fresh = LayoutPart.Create(layout.BasePart, view);
        Assert.Equal(fresh.Path.PathTypes, layout.Path.PathTypes);
        var expected = fresh.Path.PathPoints;
        var actual = layout.Path.PathPoints;
        Assert.Equal(expected.Length, actual.Length);
        for (var i = 0; i < expected.Length; i++)
        {
            Assert.Equal(expected[i].X, actual[i].X, 2);
            Assert.Equal(expected[i].Y, actual[i].Y, 2);
        }
        Assert.Equal(fresh.DisplayBounds.X, layout.DisplayBounds.X, 2);
        Assert.Equal(fresh.DisplayBounds.Y, layout.DisplayBounds.Y, 2);
        Assert.Equal(fresh.DisplayBounds.Width, layout.DisplayBounds.Width, 2);
        Assert.Equal(fresh.DisplayBounds.Height, layout.DisplayBounds.Height, 2);
        fresh.ReleaseDisplayResources();
    }

    private static Program PlateWithHoles()
    {
        var program = new Program();
        program.MoveTo(0, 0);
        program.LineTo(40, 0);
        program.LineTo(40, 30);
        program.LineTo(0, 30);
        program.LineTo(0, 0);
        for (var i = 0; i < 6; i++)
        {
            var center = new Vector(5 + i * 6, 15);
            var start = new Vector(center.X + 2, center.Y);
            program.MoveTo(start);
            program.ArcTo(start, center, RotationType.CCW);
        }
        return program;
    }
}
