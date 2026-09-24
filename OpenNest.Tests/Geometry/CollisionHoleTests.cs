using Clipper2Lib;
using OpenNest.Benchmark;
using OpenNest.Geometry;
using OpenNest.Shapes;

namespace OpenNest.Tests.Geometry;

public class CollisionHoleTests
{
    [Theory]
    [InlineData(5.2, 5.4, 0)]
    [InlineData(5.2, 5.4, 0.0003)]
    [InlineData(5.2, 5.4, 0.05)]
    [InlineData(5.2, 5.4, 0.1)]
    [InlineData(-36.8, 5.4, 0)]
    [InlineData(-36.8, 5.4, 0.0003)]
    [InlineData(-36.8, 5.4, 0.05)]
    [InlineData(-36.8, 5.4, 0.1)]
    public void Check_CircularInsertClearsShrunkHole(double x, double y, double offset)
    {
        var ring = ClipperBridge.OffsetForValidation(Profile(5, 3.5), 0.175, 0.001);
        var insert = ClipperBridge.OffsetForValidation(Profile(3), 0, 0.001);
        var outer = ring.LargestOuter();
        var hole = Assert.Single(ring.Holes);
        var disk = insert.LargestOuter();
        Move(outer, x, y);
        Move(hole, x, y);
        Move(disk, x + offset, y);

        // Check both operands and windings: neither translation nor triangulation
        // order should change the material represented by these polygons.
        for (var winding = 0; winding < 2; winding++)
        {
            var result = Collision.Check(outer, disk, ring.Holes);
            Assert.False(result.Overlaps, $"Unexpected overlap area: {result.OverlapArea:R}");
            Assert.False(Collision.HasOverlap(disk, outer, holesB: ring.Holes));
            outer.Reverse();
            hole.Reverse();
            disk.Reverse();
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void NestValidator_RingInsertAcceptsClearanceAndRejectsViolations(int quadrant)
    {
        var ring = new RingShape { OuterDiameter = 10, InnerDiameter = 7 }.GetDrawing();
        var insert = new CircleShape { Diameter = 6 }.GetDrawing();
        var plate = new Plate(24, 42) { Quadrant = quadrant, PartSpacing = 0.175 };
        var x = (quadrant is 1 or 4 ? 0 : -42) + 5.2;
        var y = (quadrant is 1 or 2 ? 0 : -24) + 5.4;
        foreach (var offset in new[] { 0.0, 0.0003, 0.05, 0.1, 0.32, 0.34, 0.6 })
        {
            // Analytic clearance is 3.5 - 3 - offset. The last two positions
            // violate spacing; the last also physically overlaps the ring.
            var a = new Part(ring) { Location = new Vector(x, y) };
            var b = new Part(insert) { Location = new Vector(x + offset, y) };
            var result = NestValidator.Validate(
                new List<(Plate, List<Part>)> { (plate, new List<Part> { a, b }) },
                new Dictionary<Drawing, (string, int)> { [ring] = ("ring", 1), [insert] = ("insert", 1) });

            Assert.True(result.Valid == (offset <= 0.32),
                $"Quadrant {quadrant}, offset {offset}: {string.Join("; ", result.Violations)}");
            if (offset > 0.32)
                Assert.Contains(result.Violations, v => v.Contains("required spacing"));
        }
    }

    [Fact]
    public void Check_HoleSubtractionDoesNotDuplicateSurvivingArea()
    {
        var outer = Polygon((0, 0), (10, 0), (10, 10), (0, 10));
        var hole = Polygon((2, 2), (8, 2), (5, 8));

        var result = Collision.Check(outer, outer, new List<Polygon> { hole });

        Assert.True(result.Overlaps);
        Assert.Equal(82, result.OverlapArea, 6);
    }

    [Fact]
    public void Check_MultipleConcaveHolesMatchesIndependentBooleanArea()
    {
        var random = new Random(240924);
        for (var trial = 0; trial < 80; trial++)
        {
            var outer = Polygon((0, 0), (20, 0), (20, 20), (0, 20));
            var holes = new List<Polygon>
            {
                Polygon((2, 5), (5, 2), (8, 5), (5, 8)),
                Polygon((9, 4), (16, 4), (16, 11), (13, 11), (13, 7), (9, 7))
            };
            var x = random.NextDouble() * 19 - 2;
            var y = random.NextDouble() * 19 - 2;
            var w = 3 + random.NextDouble() * 6;
            var h = 3 + random.NextDouble() * 6;
            var other = Polygon((x, y), (x + w, y), (x + w, y + h), (x, y + h));
            var otherHoles = new List<Polygon>
            {
                Polygon((x + 1, y + 1), (x + w - 1, y + 1), (x + w / 2, y + h - 1))
            };
            foreach (var polygon in new[] { outer, other }.Concat(holes).Concat(otherHoles))
            {
                Move(polygon, trial % 2 == 0 ? -42.123456 : 5.2, trial % 3 == 0 ? -24.654321 : 5.4);
                if (trial % 2 == 0)
                    polygon.Reverse();
            }

            var materialA = new PathsD { ClipperBridge.ToPath(outer, true) };
            materialA.AddRange(holes.Select(p => ClipperBridge.ToPath(p, false)));
            var materialB = new PathsD { ClipperBridge.ToPath(other, true) };
            materialB.AddRange(otherHoles.Select(p => ClipperBridge.ToPath(p, false)));
            var expected = System.Math.Abs(Clipper.Area(Clipper.Intersect(materialA, materialB, FillRule.NonZero, 6)));
            var result = Collision.Check(outer, other, holes, otherHoles);
            var swapped = Collision.Check(other, outer, otherHoles, holes);

            Assert.True(System.Math.Abs(result.OverlapArea - expected) < 0.001,
                $"Trial {trial}: expected area {expected:R}, got {result.OverlapArea:R}");
            Assert.True(System.Math.Abs(swapped.OverlapArea - expected) < 0.001,
                $"Swapped trial {trial}: expected area {expected:R}, got {swapped.OverlapArea:R}");
            Assert.Equal(expected > 0.001, result.Overlaps);
            Assert.Equal(result.Overlaps, swapped.Overlaps);
        }
    }

    private static ShapeProfile Profile(params double[] radii) =>
        new(radii.Select(r => (Entity)new Circle(0, 0, r)).ToList());

    private static void Move(Polygon polygon, double x, double y)
    {
        polygon.Offset(x, y);
        polygon.UpdateBounds();
    }

    private static Polygon Polygon(params (double X, double Y)[] points)
    {
        var polygon = new Polygon();
        polygon.Vertices.AddRange(points.Select(p => new Vector(p.X, p.Y)));
        polygon.Close();
        polygon.UpdateBounds();
        return polygon;
    }
}
