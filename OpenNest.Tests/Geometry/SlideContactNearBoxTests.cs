using OpenNest.Geometry;
using OpenNest.Math;

namespace OpenNest.Tests.Geometry;

/// <summary>
/// <see cref="SlideContactGeometry.GetMaterialSector"/> skips the entities whose near box misses
/// the contact point. That leaves every answer as it was only while no point within the
/// incidence tolerance of an entity lies outside the entity's box.
/// </summary>
public class SlideContactNearBoxTests
{
    private const double Tolerance = SlideContactGeometry.IncidenceTolerance;

    [Fact]
    public void Lines_HoldEveryPointWithinTolerance()
    {
        var random = new Random(1);
        for (var i = 0; i < 2000; i++)
        {
            var line = new Line(Coordinate(random), Coordinate(random), Coordinate(random), Coordinate(random));
            if (i % 5 == 0)
                line = new Line(line.pt1.X, line.pt1.Y, line.pt1.X, line.pt2.Y); // Vertical.
            if (i % 7 == 0)
                line = new Line(line.pt1.X, line.pt1.Y, line.pt2.X, line.pt1.Y); // Horizontal.
            foreach (var point in Around(line, random))
                AssertHeldWhenNear(line, point);
        }
    }

    [Fact]
    public void Arcs_HoldEveryPointWithinTolerance()
    {
        var random = new Random(2);
        var radii = new[] { 1e-3, 0.05, 0.25, 1, 7.5, 120, 5000 };
        for (var i = 0; i < 4000; i++)
        {
            var radius = radii[i % radii.Length];
            var start = random.NextDouble() * Angle.TwoPI;
            // Sweeps from a sliver to the whole circle, and ends on the axis extremes.
            var sweep = (i % 6) switch
            {
                0 => 1e-6,
                1 => Angle.TwoPI - 1e-6,
                2 => Angle.HalfPI,
                _ => random.NextDouble() * Angle.TwoPI,
            };
            if (i % 4 == 0)
                start = (i / 4 % 4) * Angle.HalfPI;
            var arc = new Arc(Coordinate(random), Coordinate(random), radius, start, start + sweep, i % 2 == 0);
            foreach (var point in Around(arc, random))
                AssertHeldWhenNear(arc, point);
        }
    }

    [Fact]
    public void Circles_HoldEveryPointWithinTolerance()
    {
        var random = new Random(3);
        for (var i = 0; i < 500; i++)
        {
            var circle = new Circle(Coordinate(random), Coordinate(random), 0.01 + random.NextDouble() * 50);
            for (var k = 0; k < 64; k++)
            {
                var angle = k * Angle.TwoPI / 64;
                foreach (var radial in new[] { -Tolerance, 0, Tolerance })
                    AssertHeldWhenNear(circle, new Vector(
                        circle.Center.X + (circle.Radius + radial) * System.Math.Cos(angle),
                        circle.Center.Y + (circle.Radius + radial) * System.Math.Sin(angle)));
            }
        }
    }

    [Fact]
    public void FarPointsAreRejected()
    {
        // The box is worth having only if it rejects: a point well away from each entity is out.
        var entities = new Entity[]
        {
            new Line(0, 0, 10, 0),
            new Arc(0, 0, 5, 0, Angle.HalfPI),
            new Circle(0, 0, 5),
        };
        foreach (var entity in entities)
        {
            SlideContactGeometry.NearBox(entity, out var minX, out var minY, out var maxX, out var maxY);
            Assert.False(Holds(minX, minY, maxX, maxY, new Vector(-20, -20)));
            Assert.False(Holds(minX, minY, maxX, maxY, new Vector(3, 30)));
        }
    }

    private static double Coordinate(Random random) => (random.NextDouble() - 0.5) * 400;

    // Points on the line, at and just past its ends, and a tolerance off to either side.
    private static IEnumerable<Vector> Around(Line line, Random random)
    {
        var dx = line.pt2.X - line.pt1.X;
        var dy = line.pt2.Y - line.pt1.Y;
        var length = System.Math.Sqrt(dx * dx + dy * dy);
        if (length == 0)
            yield break;
        var ux = dx / length;
        var uy = dy / length;
        var along = new[]
        {
            -2e-5, -1e-5, -Tolerance, 0, Tolerance, length / 3, length - Tolerance, length, length + Tolerance,
            length + 1e-5, length + 2e-5, random.NextDouble() * length,
        };
        foreach (var t in along)
            foreach (var off in new[] { -Tolerance, -Tolerance / 2, 0, Tolerance / 2, Tolerance })
                yield return new Vector(line.pt1.X + ux * t - uy * off, line.pt1.Y + uy * t + ux * off);
    }

    // Points on the arc, at and just past its ends, at the axis extremes, and a tolerance in or out.
    private static IEnumerable<Vector> Around(Arc arc, Random random)
    {
        var sign = arc.IsReversed ? -1.0 : 1.0;
        var sweep = arc.SweepAngle();
        var angles = new List<double> { 0, Angle.HalfPI, System.Math.PI, 3 * Angle.HalfPI };
        foreach (var t in new[] { -3e-5, -1e-5, -1e-6, -1e-9, 0, 1e-9, 1e-6 })
        {
            angles.Add(arc.StartAngle + sign * t);
            angles.Add(arc.EndAngle + sign * -t);
        }
        for (var k = 0; k < 12; k++)
            angles.Add(arc.StartAngle + sign * sweep * random.NextDouble());
        foreach (var angle in angles)
            foreach (var radial in new[] { -Tolerance, -Tolerance / 2, 0, Tolerance / 2, Tolerance })
                yield return new Vector(
                    arc.Center.X + (arc.Radius + radial) * System.Math.Cos(angle),
                    arc.Center.Y + (arc.Radius + radial) * System.Math.Sin(angle));
    }

    private static void AssertHeldWhenNear(Entity entity, Vector point)
    {
        if (SlideContactGeometry.DistanceTo(entity, point) > Tolerance)
            return;
        SlideContactGeometry.NearBox(entity, out var minX, out var minY, out var maxX, out var maxY);
        Assert.True(Holds(minX, minY, maxX, maxY, point),
            $"{entity} is within tolerance of {point} but its near box [{minX}, {minY}] - [{maxX}, {maxY}] misses it.");
    }

    private static bool Holds(double minX, double minY, double maxX, double maxY, Vector point) =>
        !(point.X < minX || point.X > maxX || point.Y < minY || point.Y > maxY);
}
