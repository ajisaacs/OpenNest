using System.Collections.Generic;
using OpenNest.CNC;
using OpenNest.Engine.Fill;
using OpenNest.Engine.Jobs;
using OpenNest.Engine.Jobs.Adapters;
using OpenNest.Geometry;
using Xunit;

namespace OpenNest.Engine.Tests.Jobs;

/// <summary>
/// A clamp rail's own clearance replaces the bottom edge spacing as the gap parts keep from
/// clamps, in the stock engines read, the layout check and the interactive push; part spacing
/// never applies to clamps.
/// </summary>
public class ClampClearanceTests
{
    // 24 x 24 sheet, 0.5 edge spacing, 1.0 part spacing; one clamp at X 6 on the front edge.
    private const double Edge = 0.5;
    private const double ClampTop = 0.625;

    private static Plate RailPlate(double? clearance)
    {
        var plate = new Plate(new Size(24, 24))
        {
            EdgeSpacing = new Spacing(Edge, Edge),
            PartSpacing = 1.0,
        };
        plate.SetClampRail(new ClampRail(1.5, ClampTop, new[] { 6.0 }, clearance: clearance));
        return plate;
    }

    private static Drawing Square(double size)
    {
        var program = new Program();
        program.MoveTo(0, 0);
        program.LineTo(size, 0);
        program.LineTo(size, size);
        program.LineTo(0, size);
        program.LineTo(0, 0);
        return new Drawing("square", program);
    }

    private static List<string> Violations(Plate plate, Part part) =>
        NestLayoutCheck.Validate(new() { (plate, new List<Part> { part }) },
            new Dictionary<Drawing, (string, int)> { [part.BaseDrawing] = ("square", 1) });

    [Fact]
    public void WithoutItsOwnClearanceTheRailKeepsTheBottomEdgeSpacing()
    {
        var plate = RailPlate(clearance: null);

        Assert.Equal(Edge, plate.ClampClearance);
        Assert.Equal(Edge, DrawingJobMapper.FromPlate("s", plate, 1).ClampClearance);
    }

    [Fact]
    public void StockCarriesTheRailsOwnClearance()
    {
        var plate = RailPlate(clearance: 0.05);

        Assert.Equal(0.05, plate.ClampClearance);
        Assert.Equal(0.05, DrawingJobMapper.FromPlate("s", plate, 1).ClampClearance);
    }

    [Fact]
    public void LayoutCheckEnforcesTheClampClearanceNotTheEdgeSpacing()
    {
        // A part 0.1 above the clamp, straddling it in X.
        var part = new Part(Square(3), new Vector(5, ClampTop + 0.1));

        Assert.Contains(Violations(RailPlate(clearance: null), part), v => v.Contains("clamp zone"));
        Assert.DoesNotContain(Violations(RailPlate(clearance: 0.05), part), v => v.Contains("clamp zone"));
        Assert.Contains(Violations(RailPlate(clearance: 0.2), part), v => v.Contains("clamp zone"));
    }

    [Fact]
    public void PushStopsAtTheClampClearanceRatherThanThePartSpacing()
    {
        var plate = RailPlate(clearance: 0);
        var mover = new Part(Square(3), new Vector(5, 8));
        plate.Parts.Add(mover);

        Compactor.Push(new List<Part> { mover }, plate, PushDirection.Down);

        // Part spacing (1.0) and edge spacing (0.5) would both stop it higher.
        Assert.InRange(mover.BoundingBox.Bottom, ClampTop - 1e-6, ClampTop + 0.01);
    }

    [Fact]
    public void PushStillKeepsTheClampClearanceWhenItExceedsThePartSpacing()
    {
        var plate = RailPlate(clearance: 2);
        var mover = new Part(Square(3), new Vector(5, 8));
        plate.Parts.Add(mover);

        Compactor.Push(new List<Part> { mover }, plate, PushDirection.Down);

        Assert.InRange(mover.BoundingBox.Bottom, ClampTop + 2 - 0.01, ClampTop + 2 + 0.01);
    }
}
