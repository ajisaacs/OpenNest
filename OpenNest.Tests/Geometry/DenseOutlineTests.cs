using System.Collections.Generic;
using System.Linq;
using OpenNest.Geometry;
using OpenNest.Math;

namespace OpenNest.Tests.Geometry;

/// <summary>
/// A plate whose top edge is a curve exported as 200 tiny lines, as CAD exports often write
/// splines. The spacing outline must not grow a near-circle at every joint, and the thinned
/// outline must keep at least the spacing from the part everywhere.
/// </summary>
public class DenseOutlineTests
{
    private const double Spacing = 0.125;

    /// <summary>A 3 x 1 plate, counterclockwise, whose top edge sags by <paramref name="bow"/>
    /// (negative: bulges out) in 200 lines of 0.015.</summary>
    private static Shape Plate(double bow)
    {
        var points = new List<Vector> { new(0, 0), new(3, 0), new(3, 1) };
        for (var i = 1; i < 200; i++)
        {
            var x = 3 - 3.0 * i / 200;
            points.Add(new Vector(x, 1 - bow * System.Math.Sin(System.Math.PI * x / 3)));
        }
        points.Add(new Vector(0, 1));
        var shape = new Shape();
        for (var i = 0; i < points.Count; i++)
            shape.Entities.Add(new Line(points[i], points[(i + 1) % points.Count]));
        return shape;
    }

    [Theory]
    [InlineData(0.02)]
    [InlineData(0.0005)]
    public void OffsetOfShallowConcaveJointsHasNoNearCircleArcs(double bow)
    {
        var offset = Plate(bow).OffsetOutward(Spacing);

        Assert.NotNull(offset);
        // The plate's own corners turn a quarter; no joint arc may sweep round its corner.
        Assert.All(offset.Entities.OfType<Arc>(), arc => Assert.True(arc.SweepAngle() < 0.75 * System.Math.PI,
            $"arc sweeps {arc.SweepAngle()}"));
    }

    [Theory]
    [InlineData(0.02)]
    [InlineData(-0.02)]
    [InlineData(0.0005)]
    public void ThinnedOutlineKeepsTheSpacingWithFewEntities(double bow)
    {
        var plate = Plate(bow);
        Assert.True(DenseOutline.HasDenseRun(plate.Entities), "no dense run");

        var thinned = DenseOutline.Thin(plate.OffsetOutward(Spacing + DenseOutline.Pad).Entities);

        Assert.True(thinned.Count < 40, $"{thinned.Count} entities");
        var edges = plate.Entities.Cast<Line>().ToList();
        foreach (var point in Samples(thinned))
        {
            var distance = edges.Min(e => Distance(point, e.StartPoint, e.EndPoint));
            Assert.True(distance >= Spacing - 1e-9, $"{point} is {distance} from the plate");
            Assert.True(distance <= Spacing + 2 * DenseOutline.Pad + 1e-6, $"{point} is {distance} from the plate");
        }
    }

    [Fact]
    public void ThinnedOutlineIsClosed()
    {
        var thinned = DenseOutline.Thin(Plate(0.02).OffsetOutward(Spacing + DenseOutline.Pad).Entities);

        for (var i = 0; i < thinned.Count; i++)
        {
            var end = End(thinned[i]);
            var next = Start(thinned[(i + 1) % thinned.Count]);
            Assert.True(end.DistanceTo(next) < 1e-6, $"gap after entity {i} of {thinned.Count}: {thinned[i]} then {thinned[(i + 1) % thinned.Count]}");
        }
    }

    [Fact]
    public void ContourWithoutADenseRunIsNotThinned()
    {
        var square = new Shape();
        square.Entities.Add(new Line(0, 0, 10, 0));
        square.Entities.Add(new Line(10, 0, 10, 10));
        square.Entities.Add(new Line(10, 10, 0, 10));
        square.Entities.Add(new Line(0, 10, 0, 0));

        Assert.False(DenseOutline.HasDenseRun(square.Entities));
    }

    private static IEnumerable<Vector> Samples(IEnumerable<Entity> entities)
    {
        foreach (var entity in entities)
        {
            if (entity is Arc arc)
            {
                foreach (var p in arc.ToPoints(16))
                    yield return p;
                continue;
            }
            var line = (Line)entity;
            for (var i = 0; i <= 16; i++)
            {
                var t = i / 16.0;
                yield return new Vector(line.StartPoint.X + t * (line.EndPoint.X - line.StartPoint.X),
                    line.StartPoint.Y + t * (line.EndPoint.Y - line.StartPoint.Y));
            }
        }
    }

    private static Vector Start(Entity e) => e is Line l ? l.StartPoint : ((Arc)e).StartPoint();

    private static Vector End(Entity e) => e is Line l ? l.EndPoint : ((Arc)e).EndPoint();

    private static double Distance(Vector p, Vector a, Vector b)
    {
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        var t = System.Math.Clamp(((p.X - a.X) * dx + (p.Y - a.Y) * dy) / (dx * dx + dy * dy), 0, 1);
        return p.DistanceTo(new Vector(a.X + t * dx, a.Y + t * dy));
    }
}
