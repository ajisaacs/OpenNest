using OpenNest.Converters;
using OpenNest.Engine.BestFit;
using OpenNest.Geometry;
using OpenNest.Math;

namespace OpenNest.Tests.Geometry;

public class CurveContactDistanceTests
{
    public static IEnumerable<object[]> InternalContactCases()
    {
        foreach (var cpu in new[] { false, true })
            foreach (var swap in new[] { false, true })
                foreach (var transform in new[] { 0, 1, 2, 3 })
                    yield return new object[] { cpu, swap, transform };
    }

    [Theory]
    [MemberData(nameof(InternalContactCases))]
    public void InternalContact_RejectsNearRootAndStopsAtFarRoot(bool cpu, bool swap, int transform)
    {
        var moving = new Arc(5.5, 2, 0.125, System.Math.PI, System.Math.PI / 2, true);
        var stationary = new Arc(1.5, 1.5, 0.75, System.Math.PI / 2, 3 * System.Math.PI / 2);
        var direction = new Vector(-1, 0);
        if (swap)
        {
            (moving, stationary) = (stationary, moving);
            direction = new Vector(1, 0);
        }

        // Preserve the same contact under reflection, non-cardinal rotation and translation.
        if (transform == 1)
        {
            moving = ReflectX(moving);
            stationary = ReflectX(stationary);
            direction = new Vector(-direction.X, direction.Y);
        }
        var rotation = transform == 2 ? 0.37 : transform == 3 ? System.Math.PI / 2 : 0;
        moving.Rotate(rotation);
        stationary.Rotate(rotation);
        direction = direction.Rotate(rotation);
        if (transform != 0)
        {
            moving.Offset(17, -23);
            stationary.Offset(17, -23);
        }

        // |.75 - .125| = .625. The roots are 3.625 and 4.375;
        // only the latter has its tangent point in BOTH arcs' spans.
        var distance = Distance(cpu, new() { moving }, new() { stationary }, direction);
        Assert.Equal(4.375, distance, 9);
        Assert.Equal(4.375, Tangency(moving, stationary, direction), 9);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Tangency_RejectsContactOutsideEitherArc(bool restrictMoving)
    {
        var moving = new Arc(5.5, 2, 0.125, Angle.ToRadians(90), Angle.ToRadians(180));
        var stationary = new Arc(1.5, 1.5, 0.75, Angle.ToRadians(90), Angle.ToRadians(270));
        if (restrictMoving)
        {
            moving.StartAngle = Angle.ToRadians(10);
            moving.EndAngle = Angle.ToRadians(20);
        }
        else
        {
            stationary.StartAngle = Angle.ToRadians(200);
            stationary.EndAngle = Angle.ToRadians(250);
        }
        Assert.Equal(double.MaxValue, Tangency(moving, stationary, new Vector(-1, 0)));
    }

    [Fact]
    public void Tangency_InternalNearRootIsAcceptedWhenBothSpansContainIt()
    {
        var moving = new Arc(5.5, 2, 0.125, Angle.ToRadians(30), Angle.ToRadians(70));
        var stationary = new Arc(1.5, 1.5, 0.75, Angle.ToRadians(30), Angle.ToRadians(70));
        Assert.Equal(3.625, Tangency(moving, stationary, new Vector(-1, 0)), 9);
    }

    [Theory]
    [InlineData(1.5, 0.375)]
    [InlineData(1.125, 0)]
    public void Tangency_InternalStartsInsideOrTouching(double movingX, double expected)
    {
        var moving = new Arc(movingX, 2, 0.125, System.Math.PI, System.Math.PI / 2, true);
        var stationary = new Arc(1.5, 1.5, 0.75, System.Math.PI / 2, 3 * System.Math.PI / 2);
        Assert.Equal(expected, Tangency(moving, stationary, new Vector(-1, 0)), 9);
    }

    [Fact]
    public void Tangency_ExternalFarRootIsCheckedAfterNearRootIsOutsideSpans()
    {
        var moving = new Arc(5, 1, 0.5, Angle.ToRadians(300), Angle.ToRadians(350));
        var stationary = new Arc(0, 0, 1.5, Angle.ToRadians(120), Angle.ToRadians(160));
        Assert.Equal(5 + System.Math.Sqrt(3), Tangency(moving, stationary, new Vector(-1, 0)), 9);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void Tangency_CoincidentCentersDoNotInventAnInternalTangent(double radius)
    {
        var moving = new Arc(5, 0, radius, Angle.ToRadians(90), Angle.ToRadians(100));
        var stationary = new Arc(0, 0, radius, Angle.ToRadians(90), Angle.ToRadians(100));
        // Equal radii have no isolated internal tangent. The caller's vertex phases
        // handle coincident arcs (covered separately through both public paths).
        Assert.Equal(double.MaxValue, Tangency(moving, stationary, new Vector(-1, 0)));
    }

    [Fact]
    public void Tangency_ZeroRadiusCurveIsAPointRegardlessOfItsArcAngles()
    {
        var moving = new Arc(5, 1, 0, 0, 0);
        var stationary = new Arc(0, 0, 1.5, 0, System.Math.PI);
        Assert.Equal(5 - System.Math.Sqrt(1.25), Tangency(moving, stationary, new Vector(-1, 0)), 9);
    }

    [Theory]
    [InlineData(false, 0.25)]
    [InlineData(true, 0.25)]
    [InlineData(false, 0)]
    [InlineData(true, 0)]
    public void ClosedU_NativeOffsetStopsAtFirstContact(bool cpu, double spacing)
    {
        var drawing = NativeUFixture.CreateDrawing();
        var stationary = PartGeometry.GetOffsetPerimeterEntities(drawing.Program, spacing / 2);
        var moving = stationary.CloneAll();
        foreach (var entity in moving)
            entity.Rotate(System.Math.PI, new Vector(1.25, 1.5));

        var distance = double.MaxValue;
        if (cpu)
            distance = new CpuDistanceComputer().ComputeDistances(stationary, moving,
                new[] { new SlideOffset(5.5, -1, -1, 0) })[0];
        else
        {
            foreach (var entity in moving)
                entity.Offset(5.5, -1);
            distance = SpatialQuery.DirectionalDistance(moving, stationary, new Vector(-1, 0));
        }
        var contactRadius = 0.875 - spacing;
        var expected = 4 + System.Math.Sqrt(contactRadius * contactRadius - 0.5 * 0.5);
        Assert.Equal(expected, distance, 9);

        // Independent raw tip-to-slot clearance; no offset/distance kernel in the oracle.
        var finalX = 5.5 - distance;
        var clearance = 0.875 - System.Math.Sqrt((finalX - 1.5) * (finalX - 1.5) + 0.5 * 0.5);
        Assert.Equal(spacing, clearance, 9);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExternalCircleContact_RemainsExact(bool cpu)
    {
        var moving = new List<Entity> { new Circle(5, 1, 0.5) };
        var stationary = new List<Entity> { new Circle(0, 0, 1.5) };
        var distance = Distance(cpu, moving, stationary, new Vector(-1, 0));
        Assert.Equal(5 - System.Math.Sqrt(3), distance, 9);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EqualRadiusCoincidentArcs_ReturnZeroWithoutNaN(bool cpu)
    {
        var moving = new List<Entity> { new Arc(0, 0, 1, 0, System.Math.PI) };
        var stationary = moving.CloneAll();
        Assert.Equal(0, Distance(cpu, moving, stationary, new Vector(-1, 0)));
    }

    private static double Tangency(Arc moving, Arc stationary, Vector direction) =>
        SpatialQuery.CurveTangencyDistance(
            moving.Center.X, moving.Center.Y, moving.Radius, moving,
            stationary.Center.X, stationary.Center.Y, stationary.Radius, stationary,
            direction.X, direction.Y);

    private static Arc ReflectX(Arc arc) => new(
        -arc.Center.X, arc.Center.Y, arc.Radius,
        Angle.NormalizeRad(System.Math.PI - arc.StartAngle),
        Angle.NormalizeRad(System.Math.PI - arc.EndAngle), !arc.IsReversed);

    private static double Distance(bool cpu, List<Entity> moving, List<Entity> stationary, Vector direction) =>
        cpu
            ? new CpuDistanceComputer().ComputeDistances(stationary, moving,
                new[] { new SlideOffset(0, 0, direction.X, direction.Y) })[0]
            : SpatialQuery.DirectionalDistance(moving, stationary, direction);
}

internal static class NativeUFixture
{
    internal static Drawing CreateDrawing()
    {
        // Complete closed native outline: a semicircular back and two square-ended tips.
        var entities = new List<Entity>
        {
            new Line(2.5, 0.625, 2.5, 0),
            new Line(2.5, 0, 1.5, 0),
            new Arc(1.5, 1.5, 1.5, 3 * System.Math.PI / 2, System.Math.PI / 2, true),
            new Line(1.5, 3, 2.5, 3),
            new Line(2.5, 3, 2.5, 2.375),
            new Line(2.5, 2.375, 1.5, 2.375),
            new Arc(1.5, 1.5, 0.875, System.Math.PI / 2, 3 * System.Math.PI / 2),
            new Line(1.5, 0.625, 2.5, 0.625),
        };
        var shape = Assert.Single(ShapeBuilder.GetShapes(entities));
        Assert.True(shape.IsClosed());
        var drawing = new Drawing("Native U", ConvertGeometry.ToProgram(shape));
        var profile = new ShapeProfile(ConvertProgram.ToGeometry(drawing.Program)
            .Where(e => SpecialLayers.IsMaterial(e.Layer)).ToList());
        Assert.True(profile.Perimeter.IsClosed());
        Assert.Empty(profile.Cutouts);
        var bounds = drawing.Program.BoundingBox();
        Assert.Equal(2.5, bounds.Length, 9);
        Assert.Equal(3, bounds.Width, 9);
        var exactArea = System.Math.PI * (1.5 * 1.5 - 0.875 * 0.875) / 2 + 2 * 0.625;
        Assert.InRange(profile.Perimeter.ToPolygonWithTolerance(1e-6).Area(), exactArea - 1e-5, exactArea + 1e-5);
        return drawing;
    }
}
