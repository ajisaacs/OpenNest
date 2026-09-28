using OpenNest.CNC;
using OpenNest.CNC.CuttingStrategy;
using OpenNest.Geometry;

namespace OpenNest.Tests.CuttingStrategy;

public class CutoutCornerLeadInTests
{
    private const double LeadLength = 0.125;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ApplyLeadIns_SmallRectangularCutout_PiercesInsideOnBisector(bool reverse)
    {
        // Dimensions of the small slots in 4980 A01 PT07.dxf.
        var vertices = Rectangle();
        var part = MakePart(vertices, reverse);

        part.ApplyLeadIns(Parameters(), Vector.Zero);

        var lead = SingleLeadIn(part);
        var corner = new Vector(2.282, 2.532);
        AssertPoint(corner, lead.EndPoint);
        AssertBisector(lead, corner, new Vector(-1, -1));
        Assert.True(lead.StartPoint.X > 2 && lead.StartPoint.X < 2.282);
        Assert.True(lead.StartPoint.Y > 2 && lead.StartPoint.Y < 2.532);
    }

    public static IEnumerable<object[]> Corners()
    {
        foreach (var reverse in new[] { false, true })
            foreach (var rotation in new[] { 0.0, 0.63 })
                for (var corner = 0; corner < 4; corner++)
                    foreach (var incoming in new[] { false, true })
                        yield return new object[] { reverse, rotation, corner, incoming };
    }

    [Theory]
    [MemberData(nameof(Corners))]
    public void ApplySingleLeadIn_EitherCornerEdge_UsesSameInwardBisector(
        bool reverse, double rotation, int cornerIndex, bool incoming)
    {
        var part = MakePart(Rectangle(), reverse);
        part.Rotate(rotation);
        var cutout = Cutout(part);
        var outgoing = Assert.IsType<Line>(cutout.Entities[cornerIndex]);
        var point = outgoing.StartPoint;
        var entity = incoming
            ? cutout.Entities[(cornerIndex + cutout.Entities.Count - 1) % cutout.Entities.Count]
            : outgoing;
        // Normalize each rectangular axis, not the unequal diagonal lengths.
        var next = outgoing.EndPoint - point;
        var previous = Assert.IsType<Line>(cutout.Entities[(cornerIndex + 3) % 4]).StartPoint - point;
        var direction = next / next.DistanceTo(Vector.Zero) + previous / previous.DistanceTo(Vector.Zero);

        part.ApplySingleLeadIn(Parameters(), point, entity, ContourType.Internal);

        AssertBisector(SingleLeadIn(part), point, direction);
    }

    [Theory]
    [InlineData(30, false)]
    [InlineData(30, true)]
    [InlineData(90, false)]
    [InlineData(90, true)]
    [InlineData(140, false)]
    [InlineData(140, true)]
    public void ApplySingleLeadIn_UnequalEdgeLengths_BisectsAngle(double degrees, bool reverse)
    {
        var angle = degrees * System.Math.PI / 180;
        var point = new Vector(3, 3);
        var vertices = new[]
        {
            point,
            point + new Vector(4, 0),
            point + new Vector(2 * System.Math.Cos(angle), 2 * System.Math.Sin(angle)),
        };
        var part = MakePart(vertices, reverse);
        var cutout = Cutout(part);
        var entity = cutout.Entities.OfType<Line>().First(e => e.StartPoint == point);

        part.ApplySingleLeadIn(Parameters(), point, entity, ContourType.Internal);

        AssertBisector(SingleLeadIn(part), point,
            new Vector(System.Math.Cos(angle / 2), System.Math.Sin(angle / 2)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ApplySingleLeadIn_ReflexCorner_BisectorPointsIntoScrap(bool reverse)
    {
        var vertices = new[]
        {
            new Vector(2, 2), new Vector(6, 2), new Vector(6, 4),
            new Vector(4, 4), new Vector(4, 6), new Vector(2, 6),
        };
        var part = MakePart(vertices, reverse);
        var point = new Vector(4, 4);
        var entity = Cutout(part).Entities.OfType<Line>().First(e => e.StartPoint == point);

        part.ApplySingleLeadIn(Parameters(), point, entity, ContourType.Internal);

        AssertBisector(SingleLeadIn(part), point, new Vector(-1, -1));
    }

    [Fact]
    public void ApplySingleLeadIn_MidEdge_KeepsPerpendicularApproach()
    {
        var part = MakePart(Rectangle());
        var entity = Assert.IsType<Line>(Cutout(part).Entities[0]);
        var point = entity.MidPoint;
        var normal = ContourCuttingStrategy.ComputeNormal(point, entity, ContourType.Internal, RotationType.CCW);
        var expected = Parameters().InternalLeadIn.GetPiercePoint(point, normal);

        part.ApplySingleLeadIn(Parameters(), point, entity, ContourType.Internal);

        AssertPoint(expected, SingleLeadIn(part).StartPoint);
    }

    [Fact]
    public void ApplySingleLeadIn_ExternalCorner_KeepsEntityNormal()
    {
        var part = MakePart(Rectangle());
        var profile = Profile(part);
        var entity = Assert.IsType<Line>(profile.Perimeter.Entities[0]);
        var point = entity.StartPoint;
        var parameters = Parameters();
        parameters.ExternalLeadIn = parameters.InternalLeadIn;
        var normal = ContourCuttingStrategy.ComputeNormal(point, entity, ContourType.External,
            ContourCuttingStrategy.DetermineWinding(profile.Perimeter));

        part.ApplySingleLeadIn(parameters, point, entity, ContourType.External);

        AssertPoint(parameters.ExternalLeadIn.GetPiercePoint(point, normal), SingleLeadIn(part).StartPoint);
    }

    [Fact]
    public void ApplySingleLeadIn_Corner_DoesNotChangeLeadOutDirection()
    {
        var part = MakePart(Rectangle());
        var cutout = Cutout(part);
        var entity = Assert.IsType<Line>(cutout.Entities[0]);
        var point = entity.StartPoint;
        var parameters = Parameters();
        parameters.InternalLeadOut = new LineLeadOut { Length = 0.05 };
        var normal = ContourCuttingStrategy.ComputeNormal(point, entity, ContourType.Internal,
            ContourCuttingStrategy.DetermineWinding(cutout));
        var expected = Assert.IsType<LinearMove>(Assert.Single(parameters.InternalLeadOut.Generate(point, normal)));

        part.ApplySingleLeadIn(parameters, point, entity, ContourType.Internal);

        var leadOut = Assert.IsType<Line>(Assert.Single(part.Program.ToGeometry().Where(e => e.Layer == SpecialLayers.Leadout)));
        AssertPoint(expected.EndPoint, leadOut.EndPoint);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void ApplySingleLeadIn_LineArcCorner_UsesTangentBisector(bool reverse, bool selectArc)
    {
        var part = MakePart(Rectangle());
        var program = part.BaseDrawing.Program.Clone() as Program;
        // Replace the rectangular hole with a right half-circle, closed by a line.
        program!.Codes.RemoveRange(5, program.Codes.Count - 5);
        var point = new Vector(3, 3);
        var top = new Vector(3, 7);
        program.Codes.Add(new RapidMove(point));
        if (reverse)
        {
            program.Codes.Add(new LinearMove(top));
            program.Codes.Add(new ArcMove(point, new Vector(3, 5), RotationType.CW));
        }
        else
        {
            program.Codes.Add(new ArcMove(top, new Vector(3, 5), RotationType.CCW));
            program.Codes.Add(new LinearMove(point));
        }
        part = new Part(new Drawing("line-arc-corner", program));
        var shape = Cutout(part);
        Assert.True(shape.IsClosed());
        var entity = shape.Entities.Single(e => selectArc ? e is Arc : e is Line);
        var parameters = Parameters();
        var previewNormal = ContourCuttingStrategy.ComputeLeadInNormal(shape, point, entity,
            ContourType.Internal, parameters.InternalLeadIn, ContourCuttingStrategy.DetermineWinding(shape));

        part.ApplySingleLeadIn(parameters, point, entity, ContourType.Internal);

        var lead = SingleLeadIn(part);
        AssertBisector(lead, point, new Vector(1, 1));
        AssertPoint(parameters.InternalLeadIn.GetPiercePoint(point, previewNormal), lead.StartPoint);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void ComputeLeadInNormal_NonStraightStyles_KeepEntityNormal(int style)
    {
        var shape = Cutout(MakePart(Rectangle()));
        var entity = Assert.IsType<Line>(shape.Entities[0]);
        var leadIn = style switch
        {
            0 => (LeadIn)new ArcLeadIn { Radius = 0.05 },
            1 => new LineArcLeadIn { ArcRadius = 0.05, LineLength = 0.1 },
            2 => new LineLineLeadIn { Length1 = 0.05, Length2 = 0.1 },
            _ => new NoLeadIn(),
        };
        var winding = ContourCuttingStrategy.DetermineWinding(shape);
        var expected = ContourCuttingStrategy.ComputeNormal(entity.StartPoint, entity, ContourType.Internal, winding);

        var actual = ContourCuttingStrategy.ComputeLeadInNormal(shape, entity.StartPoint, entity,
            ContourType.Internal, leadIn, winding);

        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("open")]
    [InlineData("zero-length")]
    [InlineData("cusp")]
    [InlineData("endpoint-gap")]
    public void ComputeLeadInNormal_AmbiguousCorner_FallsBackToFiniteEntityNormal(string kind)
    {
        var shape = Cutout(MakePart(Rectangle()));
        var entity = Assert.IsType<Line>(shape.Entities[0]);
        var point = entity.StartPoint;
        switch (kind)
        {
            case "open":
                shape.Entities.RemoveAt(3);
                break;
            case "zero-length":
                shape.Entities.Insert(0, new Line(point, point));
                break;
            case "cusp":
                shape.Entities.Clear();
                shape.Entities.Add(entity);
                shape.Entities.Add(new Line(entity.EndPoint, entity.StartPoint));
                break;
            case "endpoint-gap":
                // A chained contour is not necessarily an exact shared vertex.
                var last = Assert.IsType<Line>(shape.Entities[3]);
                last.EndPoint = point + new Vector(0, OpenNest.Math.Tolerance.ChainTolerance / 2);
                break;
        }
        var expected = ContourCuttingStrategy.ComputeNormal(point, entity, ContourType.Internal, RotationType.CCW);

        var actual = ContourCuttingStrategy.ComputeLeadInNormal(shape, point, entity,
            ContourType.Internal, Parameters().InternalLeadIn, RotationType.CCW);

        Assert.True(double.IsFinite(actual));
        Assert.Equal(expected, actual);
    }

    private static Vector[] Rectangle() => new[]
    {
        new Vector(2, 2), new Vector(2.282, 2),
        new Vector(2.282, 2.532), new Vector(2, 2.532),
    };

    private static CuttingParameters Parameters() => new()
    {
        InternalLeadIn = new LineLeadIn { Length = LeadLength, ApproachAngle = 90 },
    };

    private static Part MakePart(Vector[] hole, bool reverse = false)
    {
        var program = new Program(Mode.Absolute);
        AddContour(program, new[] { new Vector(0, 0), new Vector(10, 0), new Vector(10, 10), new Vector(0, 10) });
        AddContour(program, reverse ? hole.Reverse().ToArray() : hole);
        return new Part(new Drawing("corner-test", program));
    }

    private static void AddContour(Program program, Vector[] vertices)
    {
        program.Codes.Add(new RapidMove(vertices[0]));
        foreach (var point in vertices.Skip(1).Append(vertices[0]))
            program.Codes.Add(new LinearMove(point));
    }

    private static ShapeProfile Profile(Part part) => new(part.Program.ToGeometry()
        .Where(e => SpecialLayers.IsMaterial(e.Layer)).ToList());

    private static Shape Cutout(Part part) => Assert.Single(Profile(part).Cutouts);

    private static Line SingleLeadIn(Part part) => Assert.IsType<Line>(Assert.Single(
        part.Program.ToGeometry().Where(e => e.Layer == SpecialLayers.Leadin)));

    private static void AssertBisector(Line lead, Vector corner, Vector direction)
    {
        AssertPoint(corner, lead.EndPoint);
        AssertPoint(corner + direction / direction.DistanceTo(Vector.Zero) * LeadLength, lead.StartPoint);
        Assert.Equal(LeadLength, lead.Length, 8);
    }

    private static void AssertPoint(Vector expected, Vector actual)
    {
        Assert.Equal(expected.X, actual.X, 8);
        Assert.Equal(expected.Y, actual.Y, 8);
    }
}
