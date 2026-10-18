using OpenNest.CNC;
using OpenNest.Converters;
using OpenNest.Engine.CirclePacking;
using OpenNest.Engine.Jobs;
using OpenNest.Geometry;
using TestShapes = OpenNest.Engine.Tests.NestingEngines.Shapes;

namespace OpenNest.Engine.Tests.CirclePacking;

public class ItemCreateTests
{
    private const double Allowance = Item.FlattenAllowance;

    [Fact]
    public void Disc_IsItsOwnCircleWithNoHole()
    {
        var item = Create(TestShapes.Disc(5));

        AssertVector(new Vector(0, 0), item.Center, 1e-6);
        Assert.InRange(item.Radius, 5 + Allowance, 5 + 2 * Allowance);
        Assert.InRange(item.FillRatio, 0.999, 1.0);
        Assert.Equal(0, item.HoleRadius);
        Assert.Null(item.Hole);
    }

    [Fact]
    public void Ring_HoleIsConcentric()
    {
        var item = Create(TestShapes.Ring(10, 6));

        var hole = Assert.IsType<Circle>(item.Hole);
        AssertVector(item.Center, hole.Center, 1e-6);
        Assert.Equal(3 - Allowance, hole.Radius, 6);
    }

    [Fact]
    public void PartAwayFromOrigin_RecordsTheOffsetBackToItsOrigin()
    {
        var item = Create(FromEntities(new Circle(50, 20, 4)));

        AssertVector(new Vector(50, 20), item.Center, 1e-6);
        AssertVector(new Vector(-50, -20), item.PartOffset, 1e-6);
    }

    [Fact]
    public void Gear_IsPackedByItsTipCircleAroundItsBore()
    {
        var entities = Gear(teeth: 20, tipRadius: 10, rootRadius: 9);
        entities.Add(new Circle(0, 0, 3));

        var item = Create(FromEntities(entities.ToArray()));

        AssertVector(new Vector(0, 0), item.Center, 1e-6);
        Assert.Equal(10 + Allowance, item.Radius, 6);
        Assert.InRange(item.FillRatio, 0.8, 0.95);
        Assert.Equal(3 - Allowance, item.HoleRadius, 6);
        AssertVector(new Vector(0, 0), item.HoleOffset, 1e-6);
    }

    [Theory]
    [InlineData(4, 2 / System.Math.PI)]
    [InlineData(6, 0.827)]
    public void RegularPolygon_FillRatioReflectsTheWastedCorners(int sides, double expected)
    {
        var item = Create(Polygon(radius: 10, segments: sides));

        Assert.Equal(expected, item.FillRatio, 2);
    }

    [Fact]
    public void Obround_IsEnclosedEndToEnd()
    {
        var item = Create(TestShapes.Obround(20, 10));

        Assert.InRange(item.Radius, 10 + Allowance, 10 + 2 * Allowance);
        Assert.True(item.FillRatio < 0.7);
    }

    [Fact]
    public void BoltHoleFlange_UsesTheCenterBore()
    {
        var entities = new List<Entity> { new Circle(0, 0, 10), new Circle(0, 0, 4) };

        for (var i = 0; i < 4; i++)
        {
            var angle = i * System.Math.PI / 2;
            entities.Add(new Circle(7 * System.Math.Cos(angle), 7 * System.Math.Sin(angle), 0.5));
        }

        var item = Create(FromEntities(entities.ToArray()));

        AssertVector(new Vector(0, 0), item.HoleOffset, 1e-6);
        Assert.Equal(4 - Allowance, item.HoleRadius, 6);
    }

    [Fact]
    public void OffCenterRoundCutout_IsFoundAboutItsOwnCenter()
    {
        var item = Create(FromEntities(new Circle(0, 0, 10), new Circle(2, 0, 3)));

        AssertVector(new Vector(2, 0), item.Center + item.HoleOffset, 2e-3);
        Assert.InRange(item.HoleRadius, 3 - Allowance - 2e-3, 3 - Allowance);
    }

    [Fact]
    public void SquareCutout_GivesItsInscribedDisc()
    {
        var entities = new List<Entity>
        {
            new Circle(0, 0, 10),
            new Line(-3, -3, 3, -3), new Line(3, -3, 3, 3), new Line(3, 3, -3, 3), new Line(-3, 3, -3, -3),
        };

        var item = Create(FromEntities(entities.ToArray()));

        AssertVector(new Vector(0, 0), item.HoleOffset, 1e-6);
        Assert.Equal(3 - Allowance, item.HoleRadius, 6);
    }

    [Fact]
    public void Radii_BoundTheLayoutChecksFlattenedOutlines()
    {
        var geometry = JobPartGeometry.Read(PartGeometrySnapshot.FromProgram(TestShapes.Ring(10, 6)));
        var item = Item.Create(geometry);

        var outer = geometry.Perimeter.ToPolygonWithTolerance(NestTolerances.ValidationOutline, circumscribe: true);
        Assert.All(outer.Vertices, v => Assert.True(v.DistanceTo(item.Center) <= item.Radius));

        var hole = Assert.IsType<Circle>(item.Hole);
        var inner = geometry.Cutouts[0].ToPolygonWithTolerance(NestTolerances.ValidationOutline);
        Assert.All(inner.ToLines(), edge => Assert.True(edge.ClosestPointTo(hole.Center).DistanceTo(hole.Center) >= hole.Radius));
    }

    [Fact]
    public void UnreadableSnapshot_GivesNoItem()
    {
        var open = new Program();
        open.Codes.Add(new RapidMove(0, 0));
        open.Codes.Add(new LinearMove(5, 0));

        Assert.Null(Item.TryCreate(PartGeometrySnapshot.FromProgram(open)));
    }

    private static Item Create(Program program) =>
        Assert.IsType<Item>(Item.TryCreate(PartGeometrySnapshot.FromProgram(program)));

    private static Program FromEntities(params Entity[] entities) => ConvertGeometry.ToProgram(entities.ToList());

    private static Program Polygon(double radius, int segments)
    {
        var points = Enumerable.Range(0, segments)
            .Select(i => 2 * System.Math.PI * i / segments)
            .Select(a => (radius * System.Math.Cos(a), radius * System.Math.Sin(a)))
            .ToArray();

        return TestShapes.Polyline(points);
    }

    /// <summary>Trapezoid teeth: each pitch rises from the root to a flat tip and back.</summary>
    private static List<Entity> Gear(int teeth, double tipRadius, double rootRadius)
    {
        var pitch = 2 * System.Math.PI / teeth;
        var points = new List<Vector>();

        for (var k = 0; k < teeth; k++)
        {
            foreach (var (fraction, radius) in new[] { (0.0, rootRadius), (0.3, tipRadius), (0.5, tipRadius), (0.8, rootRadius) })
            {
                var angle = (k + fraction) * pitch;
                points.Add(new Vector(radius * System.Math.Cos(angle), radius * System.Math.Sin(angle)));
            }
        }

        return points.Select((p, i) => (Entity)new Line(p, points[(i + 1) % points.Count])).ToList();
    }

    private static void AssertVector(Vector expected, Vector actual, double tolerance)
    {
        Assert.True(expected.DistanceTo(actual) <= tolerance, $"Expected {expected}, got {actual}.");
    }
}
