using OpenNest.CNC;
using OpenNest.CNC.CuttingStrategy;
using OpenNest.Geometry;
using OpenNest.Math;

namespace OpenNest.Tests.CuttingStrategy;

/// <summary>
/// Curved leads meet the cut tangentially and in the cut direction, on outside perimeters and
/// cutouts under either winding, and at an outside corner whichever of its edges was picked.
/// </summary>
public class ArcLeadInTangentTests
{
    private const double Radius = 0.25;

    public static IEnumerable<object[]> Contours()
    {
        foreach (var cutout in new[] { false, true })
            foreach (var reverse in new[] { false, true })
                yield return new object[] { cutout, reverse };
    }

    [Theory]
    [MemberData(nameof(Contours))]
    public void ArcLeadIn_MidEdge_MeetsCutTangentially(bool cutout, bool reverse)
    {
        foreach (var sweep in new[] { 45.0, 90, 180 })
        {
            var (part, entity, type) = MidEdge(cutout, reverse);
            part.ApplySingleLeadIn(Parameters(new ArcLeadIn { Radius = Radius, SweepAngle = sweep }),
                entity.MidPoint, entity, type);

            var arc = Assert.Single(Leads(part, SpecialLayers.Leadin).OfType<Arc>());
            AssertPoint(entity.MidPoint, arc.EndPoint());
            AssertPoint(Direction(entity), Travel(arc, arc.EndPoint()));
            Assert.Equal(sweep, Angle.ToDegrees(arc.SweepAngle()), 6);
            Assert.Equal(Radius, arc.Radius, 8);
        }
    }

    [Theory]
    [MemberData(nameof(Contours))]
    public void LineArcLeadIn_MidEdge_TurnsTheShortWayIntoTheCut(bool cutout, bool reverse)
    {
        var (part, entity, type) = MidEdge(cutout, reverse);
        part.ApplySingleLeadIn(Parameters(new LineArcLeadIn { ArcRadius = Radius, LineLength = 0.1, ApproachAngle = 135 }),
            entity.MidPoint, entity, type);

        var arc = Assert.Single(Leads(part, SpecialLayers.Leadin).OfType<Arc>());
        AssertPoint(Direction(entity), Travel(arc, arc.EndPoint()));
        Assert.Equal(45, Angle.ToDegrees(arc.SweepAngle()), 6);
    }

    [Theory]
    [MemberData(nameof(Contours))]
    public void ArcLeadOut_MidEdge_LeavesCutTangentially(bool cutout, bool reverse)
    {
        var (part, entity, type) = MidEdge(cutout, reverse);
        var parameters = Parameters(new NoLeadIn());
        parameters.ExternalLeadOut = parameters.InternalLeadOut = new ArcLeadOut { Radius = Radius };
        part.ApplySingleLeadIn(parameters, entity.MidPoint, entity, type);

        var arc = Assert.Single(Leads(part, SpecialLayers.Leadout).OfType<Arc>());
        AssertPoint(entity.MidPoint, arc.StartPoint());
        AssertPoint(Direction(entity), Travel(arc, arc.StartPoint()));
        Assert.Equal(90, Angle.ToDegrees(arc.SweepAngle()), 6);
    }

    public static IEnumerable<object[]> PickedCorners()
    {
        foreach (var reverse in new[] { false, true })
            foreach (var rotation in new[] { 0.0, 0.63 })
                for (var corner = 0; corner < 4; corner++)
                    foreach (var incoming in new[] { false, true })
                        foreach (var sweep in new[] { 90.0, 180 })
                            yield return new object[] { reverse, rotation, corner, incoming, sweep };
    }

    [Theory]
    [MemberData(nameof(PickedCorners))]
    public void ArcLeadIn_ConvexPerimeterCorner_TangentToFirstCutEdge(
        bool reverse, double rotation, int cornerIndex, bool incoming, double sweep)
    {
        var part = MakePart(reverse);
        part.Rotate(rotation);
        var perimeter = Profile(part).Perimeter;
        var count = perimeter.Entities.Count;
        var outgoing = Assert.IsType<Line>(perimeter.Entities[cornerIndex]);
        var corner = outgoing.StartPoint;
        var entity = incoming ? perimeter.Entities[(cornerIndex + count - 1) % count] : outgoing;
        var parameters = Parameters(new ArcLeadIn { Radius = Radius, SweepAngle = sweep });
        var winding = ContourCuttingStrategy.DetermineWinding(perimeter);
        var preview = ContourCuttingStrategy.ResolveLeadIn(perimeter, corner, entity, ContourType.External,
            parameters.ExternalLeadIn, winding, parameters.PierceClearance, out var normal)
            .GetPiercePoint(corner, normal, ContourCuttingStrategy.LeadTurn(ContourType.External, winding));

        part.ApplySingleLeadIn(parameters, corner, entity, ContourType.External);

        var arc = Assert.Single(Leads(part, SpecialLayers.Leadin).OfType<Arc>());
        AssertPoint(corner, arc.EndPoint());
        AssertPoint(Direction(outgoing), Travel(arc, arc.EndPoint()));
        Assert.Equal(sweep, Angle.ToDegrees(arc.SweepAngle()), 6);
        AssertPoint(preview, arc.StartPoint());
        // The whole arc stays in the scrap: nothing of it but the corner touches the part.
        var polygon = perimeter.ToPolygon();
        Assert.False(polygon.ContainsPoint(arc.StartPoint()));
        Assert.False(polygon.ContainsPoint(arc.MidPoint()));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ArcLeadOut_ConvexPerimeterCorner_TangentToLastCutEdge(bool incoming)
    {
        var part = MakePart();
        var perimeter = Profile(part).Perimeter;
        var outgoing = Assert.IsType<Line>(perimeter.Entities[2]);
        var lastCut = Assert.IsType<Line>(perimeter.Entities[1]);
        var corner = outgoing.StartPoint;
        var parameters = Parameters(new NoLeadIn());
        parameters.ExternalLeadOut = new ArcLeadOut { Radius = Radius };

        part.ApplySingleLeadIn(parameters, corner, incoming ? lastCut : outgoing, ContourType.External);

        var arc = Assert.Single(Leads(part, SpecialLayers.Leadout).OfType<Arc>());
        AssertPoint(corner, arc.StartPoint());
        AssertPoint(Direction(lastCut), Travel(arc, arc.StartPoint()));
        Assert.False(perimeter.ToPolygon().ContainsPoint(arc.MidPoint()));
    }

    [Fact]
    public void ArcLeadIn_Scale_KeepsSweep()
    {
        var scaled = Assert.IsType<ArcLeadIn>(new ArcLeadIn { Radius = 0.2, SweepAngle = 90 }.Scale(0.5));

        Assert.Equal(0.1, scaled.Radius, 12);
        Assert.Equal(90, scaled.SweepAngle);
    }

    private static (Part Part, Line Entity, ContourType Type) MidEdge(bool cutout, bool reverse)
    {
        var part = MakePart(reverse);
        var profile = Profile(part);
        var shape = cutout ? Assert.Single(profile.Cutouts) : profile.Perimeter;
        return (part, Assert.IsType<Line>(shape.Entities[0]), cutout ? ContourType.Internal : ContourType.External);
    }

    private static CuttingParameters Parameters(LeadIn leadIn) => new()
    {
        ExternalLeadIn = leadIn,
        InternalLeadIn = leadIn,
    };

    private static Part MakePart(bool reverse = false)
    {
        var program = new Program(Mode.Absolute);
        AddContour(program, new[] { new Vector(0, 0), new Vector(10, 0), new Vector(10, 10), new Vector(0, 10) }, reverse);
        AddContour(program, new[] { new Vector(3, 3), new Vector(7, 3), new Vector(7, 7), new Vector(3, 7) }, reverse);
        return new Part(new Drawing("arc-lead-in-test", program));
    }

    private static void AddContour(Program program, Vector[] vertices, bool reverse)
    {
        if (reverse)
            vertices = vertices.Reverse().ToArray();
        program.Codes.Add(new RapidMove(vertices[0]));
        foreach (var point in vertices.Skip(1).Append(vertices[0]))
            program.Codes.Add(new LinearMove(point));
    }

    private static ShapeProfile Profile(Part part) => new(part.Program.ToGeometry()
        .Where(e => SpecialLayers.IsMaterial(e.Layer)).ToList());

    private static IEnumerable<Entity> Leads(Part part, Layer layer) =>
        part.Program.ToGeometry().Where(e => e.Layer == layer);

    private static Vector Direction(Line line) => (line.EndPoint - line.StartPoint).Normalize();

    /// <summary>Unit direction of travel along <paramref name="arc"/> at a point on it.</summary>
    private static Vector Travel(Arc arc, Vector point)
    {
        var radial = (point - arc.Center).Normalize();
        return arc.IsReversed ? new Vector(radial.Y, -radial.X) : new Vector(-radial.Y, radial.X);
    }

    private static void AssertPoint(Vector expected, Vector actual)
    {
        Assert.Equal(expected.X, actual.X, 8);
        Assert.Equal(expected.Y, actual.Y, 8);
    }
}
