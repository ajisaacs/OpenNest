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

    [Theory]
    [InlineData(BendGrainKind.AcrossGrain)]
    [InlineData(BendGrainKind.WithGrain)]
    [InlineData(BendGrainKind.Other)]
    public void BendGrainColor_GivesEachKindItsOwnColor(BendGrainKind kind)
    {
        var colors = new[]
        {
            PlateRenderer.BendGrainColor(BendGrainKind.AcrossGrain),
            PlateRenderer.BendGrainColor(BendGrainKind.WithGrain),
            PlateRenderer.BendGrainColor(BendGrainKind.Other),
        };

        Assert.Equal(3, colors.Distinct().Count());
        Assert.Equal(colors[(int)kind], PlateRenderer.BendGrainColor(kind));
    }

    [Fact]
    public void BendGrainColor_KeepsTheBendColorAcrossTheGrain_AndTheOthersAreFarFromItInHue()
    {
        Assert.Equal(System.Drawing.Color.Yellow, PlateRenderer.BendGrainColor(BendGrainKind.AcrossGrain));
        Assert.Equal(System.Drawing.Color.Cyan, PlateRenderer.BendGrainColor(BendGrainKind.WithGrain));
        Assert.Equal(System.Drawing.Color.Magenta, PlateRenderer.BendGrainColor(BendGrainKind.Other));

        // No two kinds are closer than a third of the color wheel, so none reads as a shade of another.
        var hues = Enum.GetValues<BendGrainKind>().Select(kind => PlateRenderer.BendGrainColor(kind).GetHue()).ToList();
        for (var a = 0; a < hues.Count; a++)
        {
            for (var b = a + 1; b < hues.Count; b++)
            {
                var apart = System.Math.Abs(hues[a] - hues[b]);
                Assert.True(System.Math.Min(apart, 360 - apart) >= 119, $"{hues[a]} and {hues[b]} are too close.");
            }
        }

        // Each kind also has its own dash pattern.
        Assert.Equal(3, Enum.GetValues<BendGrainKind>().Select(PlateRenderer.BendGrainDash).Distinct().Count());
    }
}
