using OpenNest.Bending;
using OpenNest.Controls;
using OpenNest.Geometry;
using OpenNest.Math;

namespace OpenNest.WinForms.Tests.Controls;

/// <summary>
/// Bends are kept in the drawing's source frame while its program starts at the origin, so a
/// placed part draws them with <c>Source.Offset</c> removed.
/// </summary>
public class BendLinePlacementTests
{
    [Fact]
    public void BendToWorld_RemovesSourceOffsetBeforePlacing()
    {
        var drawing = new Drawing("bent", new CNC.Program());
        drawing.Source.Offset = new Vector(100, 50);
        // In the source frame the bend runs across the part at local x = 5.
        var bend = new Bend { StartPoint = new Vector(105, 50), EndPoint = new Vector(105, 60) };
        var part = new Part(drawing, new Vector(10, 20));

        Assert.Equal(new Vector(15, 20), PlateRenderer.BendToWorld(part, bend.StartPoint));
        Assert.Equal(new Vector(15, 30), PlateRenderer.BendToWorld(part, bend.EndPoint));
    }

    [Fact]
    public void BendToWorld_RotatesAboutTheProgramOrigin()
    {
        var drawing = new Drawing("bent", new CNC.Program());
        drawing.Source.Offset = new Vector(100, 50);
        var part = new Part(drawing, new Vector(10, 20));
        part.Rotate(Angle.HalfPI, part.Location);

        var world = PlateRenderer.BendToWorld(part, new Vector(105, 50));

        Assert.Equal(10, world.X, 9);
        Assert.Equal(25, world.Y, 9);
    }
}
