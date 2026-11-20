using System.Collections.Generic;
using System.Linq;
using OpenNest.CNC;
using OpenNest.Engine.Fill;
using OpenNest.Geometry;
using Xunit;

namespace OpenNest.Tests.Fill;

/// <summary>
/// A part whose planned outside stops short of its entry (a perimeter tab) pushes like any
/// other part. Its cut is an open chain; unless the contour is closed across the tab, its
/// spacing-offset contour does not close and the slide kernel blocks every contact, so the
/// part cannot leave a neighbor it rests against at the part spacing.
/// </summary>
public class CompactorTabbedPartTests
{
    private const double Spacing = 0.25;
    private const double Width = 3.4375;
    private const double Height = 5.5;
    private const double Radius = 0.1875;
    private const double Tab = 0.03;

    private static readonly Box WorkArea = new(0, 0, 40, 20);

    // A rounded rectangle cut clockwise from the middle of its top-right corner, with a
    // lead-in, a lead-out and a tab: the cut ends a tab short of where it started.
    private static Part TabbedPart(double x, double y, bool tabbed = true)
    {
        var corner = new Vector(Width - Radius, Height - Radius);
        var entryAngle = System.Math.PI / 4;
        var endAngle = tabbed ? entryAngle + 2 * System.Math.Asin(Tab / (2 * Radius)) : entryAngle;
        var entry = corner + new Vector(System.Math.Cos(entryAngle), System.Math.Sin(entryAngle)) * Radius;
        var end = corner + new Vector(System.Math.Cos(endAngle), System.Math.Sin(endAngle)) * Radius;

        var program = new Program();
        program.Codes.Add(new RapidMove(entry + new Vector(0.15, 0.15)));
        program.Codes.Add(new LinearMove(entry) { Layer = LayerType.Leadin });
        program.Codes.Add(new ArcMove(new Vector(Width, Height - Radius), corner, RotationType.CW));
        program.Codes.Add(new LinearMove(Width, Radius));
        program.Codes.Add(new ArcMove(new Vector(Width - Radius, 0), new Vector(Width - Radius, Radius), RotationType.CW));
        program.Codes.Add(new LinearMove(Radius, 0));
        program.Codes.Add(new ArcMove(new Vector(0, Radius), new Vector(Radius, Radius), RotationType.CW));
        program.Codes.Add(new LinearMove(0, Height - Radius));
        program.Codes.Add(new ArcMove(new Vector(Radius, Height), new Vector(Radius, Height - Radius), RotationType.CW));
        program.Codes.Add(new LinearMove(Width - Radius, Height));
        program.Codes.Add(new ArcMove(end, corner, RotationType.CW));
        program.Codes.Add(new LinearMove(end + new Vector(0.02, 0.1)) { Layer = LayerType.Leadout });

        var part = new Part(new Drawing("tabbed", program)) { Location = new Vector(x, y) };
        part.UpdateBounds();
        return part;
    }

    // A C opening to the left: its body's left edge is at x = 10 and its ears reach left to
    // x = 4 above and below the notch (y 2..12), so its bounds wrap a part resting in the notch.
    private static Part EaredNeighbor()
    {
        var program = new Program();
        program.Codes.Add(new RapidMove(4, 0));
        program.Codes.Add(new LinearMove(14, 0));
        program.Codes.Add(new LinearMove(14, 14));
        program.Codes.Add(new LinearMove(4, 14));
        program.Codes.Add(new LinearMove(4, 12));
        program.Codes.Add(new LinearMove(10, 12));
        program.Codes.Add(new LinearMove(10, 2));
        program.Codes.Add(new LinearMove(4, 2));
        program.Codes.Add(new LinearMove(4, 0));

        var part = new Part(new Drawing("eared", program));
        part.UpdateBounds();
        return part;
    }

    /// <summary>The tabbed part in the notch, its right edge one spacing from the body.</summary>
    private static Part RestingPart() => TabbedPart(10 - Spacing - Width, 4.25);

    private static double Push(Part moving, Part obstacle, PushDirection direction) =>
        Compactor.Push(new List<Part> { moving }, new List<Part> { obstacle }, WorkArea, Spacing, direction);

    private static double MaterialGap(Part a, Part b)
    {
        var first = NativeContours.Of(new ShapeProfile(PartGeometry.GetPartEntities(a)), new Vector(0, 0));
        var second = NativeContours.Of(new ShapeProfile(PartGeometry.GetPartEntities(b)), new Vector(0, 0));
        Assert.False(first.TooClose(second, Spacing));
        return PartGeometry.GetPartLines(a).Min(line =>
            PartGeometry.GetPartLines(b).Min(other => System.Math.Min(
                System.Math.Min(line.StartPoint.DistanceTo(other.ClosestPointTo(line.StartPoint)),
                    line.EndPoint.DistanceTo(other.ClosestPointTo(line.EndPoint))),
                System.Math.Min(other.StartPoint.DistanceTo(line.ClosestPointTo(other.StartPoint)),
                    other.EndPoint.DistanceTo(line.ClosestPointTo(other.EndPoint))))));
    }

    [Fact]
    public void TabbedPartHasAClosedSpacingContour()
    {
        var part = RestingPart();

        var offset = PartGeometry.GetOffsetPerimeterEntities(part, Spacing / 2);

        Assert.True(SlideContactGeometry.Prepare(offset).IsResolved);
        var all = PartGeometry.GetOffsetPartEntities(part, Spacing / 2);
        Assert.Equal(offset.Count, all.Count);
        Assert.True(SlideContactGeometry.Prepare(all).IsResolved);
    }

    [Fact]
    public void PushAwayFromANeighborAtTheSpacingMoves()
    {
        var neighbor = EaredNeighbor();
        var part = RestingPart();
        Assert.Equal(Spacing, MaterialGap(part, neighbor), 6);
        // The neighbor's bounds wrap the part, so the broad phase cannot skip it as behind.
        Assert.True(neighbor.BoundingBox.Left < part.BoundingBox.Left);
        var expected = part.BoundingBox.Left - WorkArea.Left;

        var moved = Push(part, neighbor, PushDirection.Left);

        Assert.Equal(expected, moved, 6);
    }

    [Fact]
    public void NeighborPushesAwayFromATabbedPartAtTheSpacing()
    {
        var neighbor = EaredNeighbor();
        var part = RestingPart();
        var expected = WorkArea.Right - neighbor.BoundingBox.Right;

        var moved = Push(neighbor, part, PushDirection.Right);

        Assert.Equal(expected, moved, 6);
    }

    [Theory]
    [InlineData(PushDirection.Up)]
    [InlineData(PushDirection.Down)]
    public void PushAlongANeighborAtTheSpacingSlidesToItsEar(PushDirection direction)
    {
        var neighbor = EaredNeighbor();
        var part = RestingPart();

        var moved = Push(part, neighbor, direction);

        // 2.25 of room to each ear, less the spacing.
        Assert.Equal(2.0, moved, 6);
        Assert.Equal(Spacing, MaterialGap(part, neighbor), 6);
    }

    [Fact]
    public void PushIntoANeighborAtTheSpacingStaysPut()
    {
        var neighbor = EaredNeighbor();
        var part = RestingPart();

        Assert.Equal(0, Push(part, neighbor, PushDirection.Right));
        Assert.Equal(0, Push(neighbor, part, PushDirection.Left));
    }

    [Fact]
    public void PushTowardANeighborStopsAtTheSpacing()
    {
        var neighbor = EaredNeighbor();
        var part = RestingPart();
        part.Offset(-2, 0);

        var moved = Push(part, neighbor, PushDirection.Right);

        Assert.Equal(2.0, moved, 6);
        Assert.Equal(Spacing, MaterialGap(part, neighbor), 6);
    }

    [Theory]
    [InlineData(PushDirection.Left)]
    [InlineData(PushDirection.Right)]
    [InlineData(PushDirection.Up)]
    [InlineData(PushDirection.Down)]
    public void OverlappingTabbedPartPushesLikeAnUntabbedOne(PushDirection direction)
    {
        var neighbor = EaredNeighbor();
        // Material in material: the part's right side is inside the neighbor's body.
        var tabbed = TabbedPart(10 - Width + 1, 4.25);
        var untabbed = TabbedPart(10 - Width + 1, 4.25, tabbed: false);

        Assert.Equal(Push(untabbed, neighbor, direction), Push(tabbed, neighbor, direction), 6);
    }

    [Fact]
    public void MaterialAcrossTheTabStopsAPush()
    {
        // A rectangle entered on its right edge with a half-inch tab there, and a small block
        // level with the tab: the uncut span is still material and must keep the spacing.
        var program = new Program();
        program.Codes.Add(new RapidMove(3, 2.25));
        program.Codes.Add(new LinearMove(3, 0));
        program.Codes.Add(new LinearMove(0, 0));
        program.Codes.Add(new LinearMove(0, 5));
        program.Codes.Add(new LinearMove(3, 5));
        program.Codes.Add(new LinearMove(3, 2.75));
        var part = new Part(new Drawing("wide tab", program));
        part.UpdateBounds();
        var block = new Part(TestHelpers.MakeRectDrawing(0.2, 0.2)) { Location = new Vector(8, 2.4) };
        block.UpdateBounds();

        var moved = Push(part, block, PushDirection.Right);

        Assert.Equal(8 - 3 - Spacing, moved, 6);
    }
}
