using System.Collections.Generic;
using System.Linq;
using OpenNest.CNC;
using OpenNest.Engine.Fill;
using OpenNest.Geometry;
using Xunit;

namespace OpenNest.Engine.Tests.Fill;

/// <summary>
/// Slice 4: interactive pushes and spacing expansion never target poses inside a
/// clamp keep-out. The plate's zones act as static obstacles for Compactor.Push,
/// and as static walls for Expander — including the Expander edge inputs (clusters
/// that already overlap or are pinned to an edge), which must degrade to a
/// violation report instead of a crash.
/// </summary>
public class ClampZonePushTests
{
    // Plate: 24 x 24, 0.5 uniform edge spacing, part spacing 1.0. Zones on the
    // front edge; validation clearance (bottom edge spacing) is 0.5.
    private const double Edge = 0.5;

    private static readonly Box ZoneA = new(3, 0, 1.5, 0.625);
    private static readonly Box ZoneB = new(8, 0, 1.5, 0.625);

    private static Plate MakePlate(params ClampZone[] zones)
    {
        var plate = new Plate(new Size(24, 24));
        plate.EdgeSpacing = new Spacing(Edge, Edge);
        plate.PartSpacing = 1.0;
        foreach (var zone in zones)
            plate.ClampZones.Add(zone);
        return plate;
    }

    private static Program SquareProgram(double size)
    {
        var program = new Program();
        program.MoveTo(0, 0);
        program.LineTo(size, 0);
        program.LineTo(size, size);
        program.LineTo(0, size);
        program.LineTo(0, size);
        program.LineTo(0, 0);
        return program;
    }

    private static Part AddSquare(Plate plate, double x, double y, double size = 3)
    {
        var part = new Part(new Drawing($"sq{plate.Parts.Count}", SquareProgram(size)), new Vector(x, y));
        plate.Parts.Add(part);
        return part;
    }

    private static Box Inflated(Box zone, double clearance) =>
        new(zone.X - clearance, zone.Y - clearance, zone.Length + 2 * clearance, zone.Width + 2 * clearance);

    private static bool Overlaps(Box a, Box b) =>
        a.Left < b.Right - 1e-6 && b.Left < a.Right - 1e-6 && a.Bottom < b.Top - 1e-6 && b.Bottom < a.Top - 1e-6;

    private static void AssertClear(Plate plate, params Part[] parts)
    {
        foreach (var part in parts)
            foreach (var zone in plate.ClampZones)
                Assert.False(
                    Overlaps(part.BoundingBox, Inflated(zone.Bounds, Edge)),
                    $"'{part.BaseDrawing.Name}' at ({part.Location.X:R},{part.Location.Y:R}) enters clamp '{zone.Id}'"
                );
    }

    [Fact]
    public void PushDownTowardTheClampEdgeStopsClearOfTheKeepOut()
    {
        var plate = MakePlate(new ClampZone("front-1", ZoneA), new ClampZone("front-2", ZoneB));
        var mover = AddSquare(plate, 3, 8); // column crosses zone front-1

        var moved = Compactor.Push(new List<Part> { mover }, plate, PushDirection.Down);

        Assert.True(moved > 0.1, $"expected a downward move, got {moved}");
        AssertClear(plate, mover);
    }

    [Fact]
    public void PushDownBetweenTheClampsStillReachesTheEdge()
    {
        // Material between clamps stays usable (D6): the free column pushes to the edge.
        var plate = MakePlate(new ClampZone("front-1", ZoneA), new ClampZone("front-2", ZoneB));
        var mover = AddSquare(plate, 14, 8);

        var moved = Compactor.Push(new List<Part> { mover }, plate, PushDirection.Down);

        Assert.True(moved > 7, $"free column should reach near the edge, moved {moved}");
        AssertClear(plate, mover);
    }

    [Fact]
    public void SettleKeepsGroupOutsideClampsInsideItsFillArea()
    {
        var plate = MakePlate(new ClampZone("front-1", ZoneA));
        var first = AddSquare(plate, 3, 8);
        var second = AddSquare(plate, 12, 8);
        Compactor.Settle(new List<Part> { first, second }, plate, plate.WorkArea());
        AssertClear(plate, first, second);
    }

    [Fact]
    public void PushWithAlreadyOverlappingMemberDoesNotMoveClearMemberIntoClamp()
    {
        var plate = MakePlate(new ClampZone("front-1", ZoneA));
        var straddler = AddSquare(plate, 3, 0.5);
        var clear = AddSquare(plate, 3, 8);
        Compactor.Push(new List<Part> { straddler, clear }, plate, PushDirection.Down);
        AssertClear(plate, clear);
    }

    [Fact]
    public void ExpanderNeverTargetsAPoseInsideAClampWhenGrowthWouldEnterOne()
    {
        var plate = MakePlate(new ClampZone("front-1", ZoneA), new ClampZone("front-2", ZoneB));
        // Overlapping pair directly above zone front-2 (validation keep-out top y = 1.125):
        // separating them drives the lower square down through the keep-out boundary.
        var upper = AddSquare(plate, 8, 4.0, 3);
        var lower = AddSquare(plate, 8, 1.2, 3);

        var result = Expander.Expand(new List<Part> { upper, lower }, plate);

        Assert.False(result.Cancelled);
        AssertClear(plate, upper, lower);
    }

    [Fact]
    public void ExpanderDegradesToAReportWhenAMemberAlreadyOverlapsAClamp()
    {
        var plate = MakePlate(new ClampZone("front-1", ZoneA), new ClampZone("front-2", ZoneB));
        var straddler = AddSquare(plate, 3, 0.3, 3); // starts inside the clamp keep-out
        var clear = AddSquare(plate, 15, 0.3, 3);

        var result = Expander.Expand(new List<Part> { straddler, clear }, plate);

        Assert.False(result.Cancelled);
        // The clear member must not be driven into any clamp while its partner is stuck.
        AssertClear(plate, clear);
    }

    [Fact]
    public void ExpanderOnEdgePinnedClusterWithClampsReportsInsteadOfCrashing()
    {
        var plate = MakePlate(new ClampZone("front-1", ZoneA), new ClampZone("front-2", ZoneB));
        // Three tight squares away from the clamp edge (the Expander edge-input class:
        // an overlapping, mutually blocked cluster). All start clear of the zones.
        var a = AddSquare(plate, 15, 0.5, 3);
        var b = AddSquare(plate, 17.75, 0.5, 3);
        var c = AddSquare(plate, 15, 3.25, 3);

        var result = Expander.Expand(new List<Part> { a, b, c }, plate);

        Assert.False(result.Cancelled);
        // Whatever the achieved spacing or reported violations, no clear member may
        // end inside a clamp keep-out.
        AssertClear(plate, a, b, c);
    }
}
