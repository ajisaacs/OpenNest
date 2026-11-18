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

    // Grain angle 0 runs along X, the long side-to-side direction of a 60x120 sheet.
    // Bends should run across it (perpendicular) to avoid cracking.
    [Theory]
    [InlineData(0, 0, true)] // bend along X, with the grain
    [InlineData(0, 90, false)] // bend along Y, across the grain
    [InlineData(90, 90, true)] // rotated 90°, now with the grain
    [InlineData(0, 45, true)] // diagonal
    [InlineData(0, 87, false)] // inside the 5° tolerance
    [InlineData(0, 270, false)] // direction of the bend line does not matter
    public void HasBendNotAcrossGrain_FlagsBendsThatAreNotPerpendicularToTheGrain(
        double rotationDegrees, double bendDegrees, bool flagged)
    {
        var angle = Angle.ToRadians(bendDegrees);
        var drawing = new Drawing("bent", new CNC.Program());
        drawing.Bends.Add(new Bend
        {
            StartPoint = new Vector(0, 0),
            EndPoint = new Vector(10 * System.Math.Cos(angle), 10 * System.Math.Sin(angle)),
        });
        var part = new Part(drawing, new Vector(0, 0));
        if (rotationDegrees != 0)
            part.Rotate(Angle.ToRadians(rotationDegrees), part.Location);

        Assert.Equal(flagged, PlateRenderer.HasBendNotAcrossGrain(part, grainAngle: 0));
    }

    [Fact]
    public void HasBendNotAcrossGrain_FlagsAPartWhenAnyBendRunsWithTheGrain()
    {
        var drawing = new Drawing("bracket", new CNC.Program());
        drawing.Bends.Add(new Bend { StartPoint = new Vector(5, 0), EndPoint = new Vector(5, 10) });
        drawing.Bends.Add(new Bend { StartPoint = new Vector(0, 5), EndPoint = new Vector(10, 5) });
        var part = new Part(drawing, new Vector(0, 0));

        Assert.True(PlateRenderer.HasBendNotAcrossGrain(part, grainAngle: 0));
    }
}
