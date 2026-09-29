using OpenNest.CNC;
using OpenNest.CNC.CuttingStrategy;
using OpenNest.Geometry;

namespace OpenNest.Tests.CuttingStrategy;

/// <summary>
/// Straight lead-ins at outside perimeter corners extend the edge cut first, so the
/// torch enters on that edge's line whichever of the corner's two edges was picked.
/// </summary>
public class PerimeterCornerLeadInTests
{
    private const double LeadLength = 0.25;

    public static IEnumerable<object[]> SquareCorners()
    {
        foreach (var reverse in new[] { false, true })
            foreach (var rotation in new[] { 0.0, 0.63 })
                for (var corner = 0; corner < 4; corner++)
                    yield return new object[] { reverse, rotation, corner };
    }

    [Theory]
    [MemberData(nameof(SquareCorners))]
    public void ApplyLeadIns_ConvexCorner_ExtendsFirstCutEdge(bool reverse, double rotation, int cornerIndex)
    {
        var part = MakePart(Square(), reverse);
        part.Rotate(rotation);
        var perimeter = Perimeter(part);
        var outgoing = Assert.IsType<Line>(perimeter.Entities[cornerIndex]);
        var corner = outgoing.StartPoint;
        var centroid = Centroid(perimeter);
        var approach = corner + (corner - centroid) * 2;

        part.ApplyLeadIns(Parameters(), approach);

        AssertStraightEntry(part, corner, Direction(outgoing));
    }

    public static IEnumerable<object[]> PickedEdges()
    {
        foreach (var args in SquareCorners())
            foreach (var incoming in new[] { false, true })
                yield return args.Append(incoming).ToArray();
    }

    [Theory]
    [MemberData(nameof(PickedEdges))]
    public void ApplySingleLeadIn_ConvexCorner_SameStraightLeadForEitherEdge(
        bool reverse, double rotation, int cornerIndex, bool incoming)
    {
        var part = MakePart(Square(), reverse);
        part.Rotate(rotation);
        var perimeter = Perimeter(part);
        var count = perimeter.Entities.Count;
        var outgoing = Assert.IsType<Line>(perimeter.Entities[cornerIndex]);
        var corner = outgoing.StartPoint;
        var entity = incoming ? perimeter.Entities[(cornerIndex + count - 1) % count] : outgoing;
        var parameters = Parameters();
        var preview = PreviewPierce(perimeter, corner, entity, parameters);

        part.ApplySingleLeadIn(parameters, corner, entity, ContourType.External);

        AssertStraightEntry(part, corner, Direction(outgoing));
        AssertPoint(preview, SingleLead(part, SpecialLayers.Leadin).StartPoint);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ApplySingleLeadIn_ConvexCorner_IgnoresApproachAngle(bool incoming)
    {
        var part = MakePart(Square());
        var perimeter = Perimeter(part);
        var outgoing = Assert.IsType<Line>(perimeter.Entities[1]);
        var entity = incoming ? perimeter.Entities[0] : outgoing;
        var parameters = Parameters(approachAngle: 60);

        part.ApplySingleLeadIn(parameters, outgoing.StartPoint, entity, ContourType.External);

        AssertStraightEntry(part, outgoing.StartPoint, Direction(outgoing));
    }

    [Fact]
    public void ApplySingleLeadIn_MidEdge_KeepsApproachAngle()
    {
        var part = MakePart(Square());
        var perimeter = Perimeter(part);
        var entity = Assert.IsType<Line>(perimeter.Entities[0]);
        var point = entity.MidPoint;
        var parameters = Parameters(approachAngle: 60);
        var normal = ContourCuttingStrategy.ComputeNormal(point, entity, ContourType.External,
            ContourCuttingStrategy.DetermineWinding(perimeter));

        part.ApplySingleLeadIn(parameters, point, entity, ContourType.External);

        AssertPoint(parameters.ExternalLeadIn.GetPiercePoint(point, normal),
            SingleLead(part, SpecialLayers.Leadin).StartPoint);
    }

    [Theory]
    [InlineData(90, true, false)]
    [InlineData(150, true, false)]
    [InlineData(160, true, true)]
    [InlineData(170, false, false)]
    [InlineData(170, false, true)]
    [InlineData(179, false, true)]
    public void ApplySingleLeadIn_FlatCorner_FallsBackToPerpendicularWithoutPierceClearance(
        double interiorDegrees, bool straight, bool incoming)
    {
        // Corner at (10, 0): cut along +X, then turn left by 180 - interior degrees.
        var turn = System.Math.PI - interiorDegrees * System.Math.PI / 180;
        var corner = new Vector(10, 0);
        var next = corner + new Vector(System.Math.Cos(turn), System.Math.Sin(turn)) * 5;
        var part = MakePart(new[] { new Vector(0, 0), corner, next, new Vector(0, next.Y) });
        var perimeter = Perimeter(part);
        var outgoing = perimeter.Entities.OfType<Line>().Single(e => e.StartPoint == corner);
        var entity = incoming ? perimeter.Entities.OfType<Line>().Single(e => e.EndPoint == corner) : outgoing;
        var parameters = Parameters();
        Assert.Equal(0.0625, parameters.PierceClearance);

        part.ApplySingleLeadIn(parameters, corner, entity, ContourType.External);

        if (straight)
        {
            AssertStraightEntry(part, corner, Direction(outgoing));
            return;
        }

        var normal = ContourCuttingStrategy.ComputeNormal(corner, outgoing, ContourType.External,
            ContourCuttingStrategy.DetermineWinding(perimeter));
        AssertPoint(parameters.ExternalLeadIn.GetPiercePoint(corner, normal),
            SingleLead(part, SpecialLayers.Leadin).StartPoint);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void ApplySingleLeadIn_ReflexCorner_BisectsNotch(bool reverse, bool pickFirst)
    {
        var notch = new Vector(4, 4);
        var part = MakePart(LShape(), reverse);
        var perimeter = Perimeter(part);
        var touching = perimeter.Entities.OfType<Line>()
            .Where(e => e.StartPoint == notch || e.EndPoint == notch).ToList();
        Assert.Equal(2, touching.Count);
        var entity = pickFirst ? touching[0] : touching[1];
        var parameters = Parameters();
        var preview = PreviewPierce(perimeter, notch, entity, parameters);

        part.ApplySingleLeadIn(parameters, notch, entity, ContourType.External);

        var lead = SingleLead(part, SpecialLayers.Leadin);
        AssertPoint(notch, lead.EndPoint);
        AssertPoint(notch + new Vector(1, 1).Normalize() * LeadLength, lead.StartPoint);
        AssertPoint(preview, lead.StartPoint);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void ApplySingleLeadIn_ConvexCorner_LineLeadOutRunsOnAlongLastCutEdge(bool reverse, bool incoming)
    {
        var part = MakePart(Square(), reverse);
        var perimeter = Perimeter(part);
        var count = perimeter.Entities.Count;
        var outgoing = Assert.IsType<Line>(perimeter.Entities[2]);
        var lastCut = Assert.IsType<Line>(perimeter.Entities[1]);
        var corner = outgoing.StartPoint;
        var entity = incoming ? lastCut : outgoing;
        var parameters = Parameters();
        parameters.ExternalLeadOut = new LineLeadOut { Length = 0.1, ApproachAngle = 60 };

        part.ApplySingleLeadIn(parameters, corner, entity, ContourType.External);

        var leadOut = SingleLead(part, SpecialLayers.Leadout);
        AssertPoint(corner, leadOut.StartPoint);
        AssertPoint(corner + Direction(lastCut) * 0.1, leadOut.EndPoint);
    }

    [Fact]
    public void ApplySingleLeadIn_CutoutCorner_KeepsBisector()
    {
        // Outside perimeter handling must not leak into cutouts.
        var program = new Program(Mode.Absolute);
        AddContour(program, Square());
        AddContour(program, new[] { new Vector(2, 2), new Vector(4, 2), new Vector(4, 4), new Vector(2, 4) });
        var part = new Part(new Drawing("cutout", program));
        var cutout = Assert.Single(Profile(part).Cutouts);
        var entity = cutout.Entities.OfType<Line>().First(e => e.StartPoint == new Vector(4, 4));
        var parameters = Parameters();
        parameters.InternalLeadIn = new LineLeadIn { Length = LeadLength, ApproachAngle = 90 };

        part.ApplySingleLeadIn(parameters, entity.StartPoint, entity, ContourType.Internal);

        var lead = SingleLead(part, SpecialLayers.Leadin);
        AssertPoint(new Vector(4, 4) + new Vector(-1, -1).Normalize() * LeadLength, lead.StartPoint);
    }

    private static Vector PreviewPierce(Shape shape, Vector point, Entity entity, CuttingParameters parameters)
    {
        var leadIn = ContourCuttingStrategy.ResolveLeadIn(shape, point, entity, ContourType.External,
            parameters.ExternalLeadIn, ContourCuttingStrategy.DetermineWinding(shape),
            parameters.PierceClearance, out var normal);
        return leadIn.GetPiercePoint(point, normal);
    }

    /// <summary>The lead-in runs on the first-cut edge's line and cutting continues along it.</summary>
    private static void AssertStraightEntry(Part part, Vector corner, Vector direction)
    {
        var lead = SingleLead(part, SpecialLayers.Leadin);
        AssertPoint(corner, lead.EndPoint);
        AssertPoint(corner - direction * LeadLength, lead.StartPoint);

        var geometry = part.Program.ToGeometry();
        var firstCut = Assert.IsType<Line>(geometry
            .SkipWhile(e => e.Layer != SpecialLayers.Leadin).Skip(1)
            .First(e => SpecialLayers.IsMaterial(e.Layer)));
        AssertPoint(corner, firstCut.StartPoint);
        AssertPoint(direction, Direction(firstCut));
    }

    private static CuttingParameters Parameters(double approachAngle = 90) => new()
    {
        ExternalLeadIn = new LineLeadIn { Length = LeadLength, ApproachAngle = approachAngle },
    };

    private static Vector[] Square() => new[]
    {
        new Vector(0, 0), new Vector(10, 0), new Vector(10, 10), new Vector(0, 10),
    };

    private static Vector[] LShape() => new[]
    {
        new Vector(0, 0), new Vector(10, 0), new Vector(10, 4),
        new Vector(4, 4), new Vector(4, 10), new Vector(0, 10),
    };

    private static Part MakePart(Vector[] perimeter, bool reverse = false)
    {
        var program = new Program(Mode.Absolute);
        AddContour(program, reverse ? perimeter.Reverse().ToArray() : perimeter);
        return new Part(new Drawing("perimeter-corner-test", program));
    }

    private static void AddContour(Program program, Vector[] vertices)
    {
        program.Codes.Add(new RapidMove(vertices[0]));
        foreach (var point in vertices.Skip(1).Append(vertices[0]))
            program.Codes.Add(new LinearMove(point));
    }

    private static ShapeProfile Profile(Part part) => new(part.Program.ToGeometry()
        .Where(e => SpecialLayers.IsMaterial(e.Layer)).ToList());

    private static Shape Perimeter(Part part)
    {
        var perimeter = Profile(part).Perimeter;
        Assert.True(perimeter.IsClosed());
        return perimeter;
    }

    private static Vector Centroid(Shape shape)
    {
        var lines = shape.Entities.OfType<Line>().ToList();
        var sum = lines.Aggregate(Vector.Zero, (total, line) => total + line.StartPoint);
        return sum / lines.Count;
    }

    private static Vector Direction(Line line) => (line.EndPoint - line.StartPoint).Normalize();

    private static Line SingleLead(Part part, Layer layer) => Assert.IsType<Line>(Assert.Single(
        part.Program.ToGeometry().Where(e => e.Layer == layer)));

    private static void AssertPoint(Vector expected, Vector actual)
    {
        Assert.Equal(expected.X, actual.X, 8);
        Assert.Equal(expected.Y, actual.Y, 8);
    }
}
