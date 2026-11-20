using System.Collections.Generic;
using OpenNest.Geometry;
using Xunit;

namespace OpenNest.Tests.TabbedCutouts;

/// <summary>
/// <see cref="NestingMaterial.ClosedAcrossTab"/>: a contour whose cut stops short of its
/// start is closed across that one gap, and nothing else is touched.
/// </summary>
public class TabClosureTests
{
    private static Line L(double x1, double y1, double x2, double y2) =>
        new(new Vector(x1, y1), new Vector(x2, y2)) { Layer = SpecialLayers.Cut };

    private static void AssertClosedLoop(List<Entity> entities)
    {
        var shapes = ShapeBuilder.GetShapes(entities);
        Assert.Single(shapes);
        Assert.True(shapes[0].IsClosed());
        Assert.True(SlideContactGeometry.Prepare(shapes[0].Entities).IsResolved);
    }

    [Fact]
    public void ClosedContourIsReturnedUnchanged()
    {
        var entities = new List<Entity> { L(0, 0, 4, 0), L(4, 0, 4, 3), L(4, 3, 0, 3), L(0, 3, 0, 0) };
        var before = entities.ToArray();

        var closed = entities.ClosedAcrossTab();

        Assert.Same(entities, closed);
        Assert.Equal(before, closed);
    }

    [Fact]
    public void TrimmedLineIsExtendedToTheEntry()
    {
        // Entered mid-edge at (2, 3), cut clockwise, stopping 0.03 short of the entry.
        var entities = new List<Entity>
        {
            L(2, 3, 4, 3), L(4, 3, 4, 0), L(4, 0, 0, 0), L(0, 0, 0, 3), L(0, 3, 1.97, 3),
        };

        var closed = entities.ClosedAcrossTab();

        Assert.Equal(5, closed.Count);
        var last = Assert.IsType<Line>(closed[4]);
        Assert.Equal(new Vector(0, 3), last.StartPoint);
        Assert.Equal(new Vector(2, 3), last.EndPoint);
        Assert.Same(SpecialLayers.Cut, last.Layer);
        AssertClosedLoop(closed);
    }

    [Fact]
    public void TrimmedArcIsExtendedAlongItsCircle()
    {
        // A quarter-round corner entered at 45 degrees and cut clockwise; the cut returns
        // along the same corner and stops at 60 degrees.
        var center = new Vector(3, 2);
        var entry = Angle(45);
        var entities = new List<Entity>
        {
            new Arc(center, 1, entry, 0, true) { Layer = SpecialLayers.Cut },
            L(4, 2, 4, 0), L(4, 0, 0, 0), L(0, 0, 0, 3), L(0, 3, 3, 3),
            new Arc(center, 1, Angle(90), Angle(60), true) { Layer = SpecialLayers.Cut },
        };

        var closed = entities.ClosedAcrossTab();

        Assert.Equal(6, closed.Count);
        var last = Assert.IsType<Arc>(closed[5]);
        Assert.True(last.IsReversed);
        Assert.Equal(Angle(90), last.StartAngle, 12);
        Assert.Equal(entry, last.EndAngle, 12);
        Assert.Equal(1, last.Radius);
        AssertClosedLoop(closed);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RoundOutsideCutAsOneArcBecomesACircle(bool clockwise)
    {
        var arc = new Arc(new Vector(5, 5), 2, 1, clockwise ? 1.02 : 0.98, clockwise) { Layer = SpecialLayers.Cut };
        Assert.True(arc.SweepAngle() > 6);
        var entities = new List<Entity> { arc };

        var closed = entities.ClosedAcrossTab();

        var circle = Assert.IsType<Circle>(Assert.Single(closed));
        Assert.Equal(new Vector(5, 5), circle.Center);
        Assert.Equal(2, circle.Radius);
        Assert.Equal(clockwise ? RotationType.CW : RotationType.CCW, circle.Rotation);
        Assert.Same(SpecialLayers.Cut, circle.Layer);
    }

    [Fact]
    public void TabThatSwallowsACornerIsBridgedByItsChord()
    {
        // Entered 0.01 past the top-left corner: the tab removed the corner's last stub and
        // trimmed the left edge, so the gap is not on the last cut's own line.
        var entities = new List<Entity>
        {
            L(0.01, 3, 4, 3), L(4, 3, 4, 0), L(4, 0, 0, 0), L(0, 0, 0, 2.98),
        };

        var closed = entities.ClosedAcrossTab();

        Assert.Equal(5, closed.Count);
        var bridge = Assert.IsType<Line>(closed[4]);
        Assert.Equal(new Vector(0, 2.98), bridge.StartPoint);
        Assert.Equal(new Vector(0.01, 3), bridge.EndPoint);
        AssertClosedLoop(closed);
    }

    [Fact]
    public void ContourSplitByShapeGroupingIsStillClosed()
    {
        // The contour's first arc listed after the chain it leads into, as regrouping by
        // shape leaves it: two open runs, one gap.
        var center = new Vector(3, 2);
        var entities = new List<Entity>
        {
            L(4, 2, 4, 0), L(4, 0, 0, 0), L(0, 0, 0, 3), L(0, 3, 3, 3),
            new Arc(center, 1, Angle(90), Angle(60), true) { Layer = SpecialLayers.Cut },
            new Arc(center, 1, Angle(45), 0, true) { Layer = SpecialLayers.Cut },
        };

        var closed = entities.ClosedAcrossTab();

        Assert.Equal(6, closed.Count);
        Assert.Equal(Angle(45), Assert.IsType<Arc>(closed[4]).EndAngle, 12);
        AssertClosedLoop(closed);
    }

    [Fact]
    public void OtherClosedContoursAreKept()
    {
        var hole = new Circle(new Vector(2, 1.5), 0.5) { Layer = SpecialLayers.Cut };
        var entities = new List<Entity>
        {
            hole, L(2, 3, 4, 3), L(4, 3, 4, 0), L(4, 0, 0, 0), L(0, 0, 0, 3), L(0, 3, 1.97, 3),
        };

        var closed = entities.ClosedAcrossTab();

        Assert.Equal(6, closed.Count);
        Assert.Same(hole, closed[0]);
        Assert.Equal(new Vector(2, 3), Assert.IsType<Line>(closed[5]).EndPoint);
    }

    [Fact]
    public void ContourCutInPiecesIsLeftAlone()
    {
        // Two gaps: which ends belong together is not a tab's question.
        var entities = new List<Entity>
        {
            L(2, 3, 4, 3), L(4, 3, 4, 1), L(4, 0, 0, 0), L(0, 0, 0, 3), L(0, 3, 1.97, 3),
        };
        var before = entities.ToArray();

        var closed = entities.ClosedAcrossTab();

        Assert.Equal(before, closed);
    }

    [Fact]
    public void LoneLineIsLeftAlone()
    {
        var entities = new List<Entity> { L(0, 0, 4, 0) };

        Assert.Equal(new[] { entities[0] }, entities.ClosedAcrossTab());
    }

    private static double Angle(double degrees) => degrees * System.Math.PI / 180;
}
