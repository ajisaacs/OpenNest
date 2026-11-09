using OpenNest.CNC;
using OpenNest.Engine.Fill;
using OpenNest.Engine.Jobs;
using OpenNest.Engine.Jobs.Placement;
using OpenNest.Geometry;
using OpenNest.Math;

namespace OpenNest.Engine.Tests.Fill;

/// <summary>
/// A reported nest: a 9" angle bracket with 2.5" legs on a 48 x 96 plate. Fill paired brackets
/// at 90°/270° (75); laid by hand at 315°/135°, each row nests its brackets corner into corner
/// along the sheet and the reversed row interlocks with it, a herringbone of 84.
/// </summary>
public class HerringboneFillTests
{
    /// <summary>The reported drawing's program, in absolute coordinates.</summary>
    private static Drawing AngleBracket()
    {
        var program = new Program();
        foreach (var (x, y) in new[] { (0.0, 0.0), (-6.25, 3.75), (-6.25, 6.25), (-2.5, 0.0) })
        {
            program.MoveTo(x, y);
            program.ArcTo(new Vector(x, y), new Vector(x - 0.28125, y), RotationType.CCW);
        }

        program.MoveTo(-7.78125, -1.25);
        program.LineTo(-7.78125, 7.75);
        program.LineTo(-5.28125, 7.75);
        program.LineTo(-5.28125, 2.25);
        program.ArcTo(new Vector(-4.28125, 1.25), new Vector(-4.28125, 2.25), RotationType.CCW);
        program.LineTo(1.21875, 1.25);
        program.LineTo(1.21875, -1.25);
        program.LineTo(-7.78125, -1.25);
        return new Drawing("Angle Bracket", program);
    }

    [Fact]
    public void FillFindsTheHerringboneOfAngleBrackets()
    {
        var plate = new Plate(new Size(48, 96))
        {
            PartSpacing = 0.25,
            EdgeSpacing = new Spacing(0.5, 0.25, 0.5, 0.25),
        };
        var drawing = AngleBracket();

        var parts = PlateFillService.FillItem("Fill", plate,
            new NestItem { Drawing = drawing, Quantity = 0 }, plate.WorkArea(), null,
            CancellationToken.None);

        Assert.Equal(84, parts.Count);
        Assert.Empty(NestLayoutCheck.Validate(new() { (plate, parts) },
            new Dictionary<Drawing, (string, int)> { [drawing] = ("Angle Bracket", 84) }));
    }

    [Fact]
    public void RotationsLayEachHullEdgeAlongTheSheetInEveryQuarterTurn()
    {
        var degrees = HerringbonePatterns.Rotations(AngleBracket())
            .Select(r => System.Math.Round(Angle.ToDegrees(r)))
            .OrderBy(d => d)
            .ToList();

        Assert.Equal(new[] { 0.0, 45, 90, 135, 180, 225, 270, 315 }, degrees);
    }
}
