using System.Collections.Generic;
using System.Linq;
using OpenNest.Geometry;
using OpenNest.Math;

namespace OpenNest.Tests.Geometry;

public class ShapeOffsetTests
{
    [Theory]
    [InlineData(false, OffsetSide.Left, 4)] // CCW: center on the left, shrinks.
    [InlineData(false, OffsetSide.Right, 6)]
    [InlineData(true, OffsetSide.Left, 6)] // CW: center on the right, grows.
    [InlineData(true, OffsetSide.Right, 4)]
    public void ArcOffset_GrowsAwayFromCenter(bool reversed, OffsetSide side, double radius)
    {
        var arc = new Arc(0, 0, 5, 0, Angle.HalfPI, reversed);

        var offset = (Arc)arc.OffsetEntity(1, side);

        Assert.Equal(radius, offset.Radius, 9);
        Assert.Equal(reversed, offset.IsReversed);
    }

    [Theory]
    [InlineData(RotationType.CCW, OffsetSide.Left, 4)]
    [InlineData(RotationType.CCW, OffsetSide.Right, 6)]
    [InlineData(RotationType.CW, OffsetSide.Left, 6)]
    [InlineData(RotationType.CW, OffsetSide.Right, 4)]
    public void CircleOffset_GrowsAwayFromCenter(RotationType rotation, OffsetSide side, double radius)
    {
        var circle = new Circle(0, 0, 5) { Rotation = rotation };

        var offset = (Circle)circle.OffsetEntity(1, side);

        Assert.Equal(radius, offset.Radius, 9);
        Assert.Equal(rotation, offset.Rotation);
    }

    [Theory]
    [InlineData(OffsetSide.Left, 1)]
    [InlineData(OffsetSide.Right, -1)]
    public void LineOffset_MovesToSideAndKeepsDirection(OffsetSide side, double y)
    {
        var line = new Line(0, 0, 10, 0);

        var offset = (Line)line.OffsetEntity(1, side);

        Assert.True(offset.StartPoint.DistanceTo(new Vector(0, y)) < 1e-9);
        Assert.True(offset.EndPoint.DistanceTo(new Vector(10, y)) < 1e-9);
    }

    [Fact]
    public void OffsetOutward_NonTangentLineArcCorners_GetRoundJoins()
    {
        // D shape: right half of an r=5 circle closed by the Y axis. Both corners are
        // convex and not tangent, so the offset needs a round join at each.
        var shape = new Shape();
        shape.Entities.Add(new Arc(0, 0, 5, -Angle.HalfPI, Angle.HalfPI));
        shape.Entities.Add(new Line(0, 5, 0, -5));

        var offset = shape.OffsetOutward(1);

        AssertClosedChain(offset.Entities);
        Assert.Equal(2, offset.Entities.OfType<Arc>().Count(a => a.Radius.IsEqualTo(1)));
        Assert.All(Samples(offset.Entities), p => Assert.True(DistanceTo(shape, p) > 1 - 1e-6));
    }

    [Fact]
    public void OffsetOutward_CollapsedFillet_ClosesTheChain()
    {
        // 10x4 part with a 0.2-wide slot down from the top, with a round (r=0.1) bottom.
        // Offsetting outward by 0.25 collapses the slot's end arc.
        var shape = new Shape();
        shape.Entities.Add(new Line(0, 0, 10, 0));
        shape.Entities.Add(new Line(10, 0, 10, 4));
        shape.Entities.Add(new Line(10, 4, 5.1, 4));
        shape.Entities.Add(new Line(5.1, 4, 5.1, 2));
        shape.Entities.Add(new Arc(5, 2, 0.1, 0, System.Math.PI, reversed: true));
        shape.Entities.Add(new Line(4.9, 2, 4.9, 4));
        shape.Entities.Add(new Line(4.9, 4, 0, 4));
        shape.Entities.Add(new Line(0, 4, 0, 0));

        var offset = shape.OffsetOutward(0.25);

        AssertClosedChain(offset.Entities);
        Assert.All(
            Samples(offset.Entities),
            p => Assert.True(DistanceTo(shape, p) > 0.25 - 1e-6 || IsInsideSlot(p))
        );
    }

    // The collapsed slot leaves a line bridging its walls' offsets, which lies inside
    // the offset envelope (closer than the spacing) by design.
    private static bool IsInsideSlot(Vector p) => p.X > 4.8 && p.X < 5.2 && p.Y > 1.7;

    private static void AssertClosedChain(List<Entity> entities)
    {
        for (var i = 0; i < entities.Count; i++)
        {
            var end = End(entities[i]);
            var start = Start(entities[(i + 1) % entities.Count]);

            Assert.True(
                end.DistanceTo(start) < 1e-6,
                $"Gap of {end.DistanceTo(start)} after entity {i} ({entities[i].Type})."
            );
        }
    }

    private static IEnumerable<Vector> Samples(List<Entity> entities)
    {
        foreach (var entity in entities)
        {
            for (var t = 0.0; t <= 1.0; t += 0.1)
            {
                yield return entity switch
                {
                    Line l => l.StartPoint + (l.EndPoint - l.StartPoint) * t,
                    Arc a => ArcPoint(a, t),
                    _ => Start(entity),
                };
            }
        }
    }

    private static Vector ArcPoint(Arc arc, double t)
    {
        var sweep = arc.SweepAngle();
        var angle = arc.StartAngle + (arc.IsReversed ? -sweep : sweep) * t;
        return new Vector(
            arc.Center.X + arc.Radius * System.Math.Cos(angle),
            arc.Center.Y + arc.Radius * System.Math.Sin(angle)
        );
    }

    private static double DistanceTo(Shape shape, Vector p) =>
        shape.Entities.Min(e => e.ClosestPointTo(p).DistanceTo(p));

    private static Vector Start(Entity e) =>
        e switch
        {
            Line l => l.StartPoint,
            Arc a => a.StartPoint(),
            _ => default,
        };

    private static Vector End(Entity e) =>
        e switch
        {
            Line l => l.EndPoint,
            Arc a => a.EndPoint(),
            _ => default,
        };
}
