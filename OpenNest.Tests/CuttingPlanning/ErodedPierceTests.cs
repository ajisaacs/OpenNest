using OpenNest.CNC;
using OpenNest.CNC.CuttingPlanning;
using OpenNest.CNC.CuttingStrategy;
using OpenNest.Engine.CuttingPlanning;
using OpenNest.Geometry;

namespace OpenNest.Tests.CuttingPlanning;

public class ErodedPierceTests
{
    private static readonly Vector Start = new(-2, 1.5);

    [Fact]
    public void Find_SlotIsPiercedOnItsCentreLineNearEachTarget()
    {
        var slot = Shape(Rectangle(2, 1, 2.4, 1.05));

        var found = ErodedPierce.Find(slot, 0.0625, [new Vector(2.1, 1), new Vector(2.4, 1.02)], 4);

        Assert.Equal(2, found.Pierces.Count);
        // 0.025 is all the slot allows; the erosion keeps nine tenths of it.
        Assert.Equal(0.025 * ErodedPierce.DepthFraction, found.Clearance, 6);
        Assert.All(found.Pierces, p => Assert.InRange(p.Y, 1.0225, 1.0275));
        Assert.Equal(2.1, found.Pierces[0].X, 2);
        Assert.InRange(found.Pierces[1].X, 2.37, 2.38);
    }

    [Fact]
    public void Find_RoomForTheClearanceErodesOnlyThatDeep()
    {
        var hole = Shape(Rectangle(0, 0, 1, 1));

        var found = ErodedPierce.Find(hole, 0.0625, [new Vector(0.5, 0)], 1);

        Assert.Equal(0.0625, found.Clearance);
        Assert.Equal(0.5, Assert.Single(found.Pierces).X, 3);
        Assert.InRange(found.Pierces[0].Y, 0.0625, 0.066);
    }

    [Fact]
    public void Planner_CutoutsTooSmallForTheLeadArePiercedByErosion()
    {
        var program = new Program();
        program.Merge(Rectangle(2, 1, 2.4, 1.05));
        program.Merge(Circle(5, 1.5, 0.03));
        program.Merge(Rectangle(0, 0, 10, 3));
        var part = new Part(new Drawing("tag", program));
        var parameters = new CuttingParameters
        {
            InternalLeadIn = new LineLeadIn { Length = 0.5, ApproachAngle = 90 },
            InternalLeadOut = new LineLeadOut { Length = 0.1 },
        };

        var (snapshot, result) = Plan(parameters, part);

        Assert.True(result.Status == CuttingPlanStatus.Ready, Describe(result));
        var proposal = Assert.Single(result.ProposedOrder);
        var holes = proposal.ContourChoices.Where(c => c.ContourOrdinal != snapshot.Placements[0].Prepared.PerimeterOrdinal).ToArray();
        Assert.Equal(2, holes.Length);
        Assert.All(holes, c => Assert.NotNull(c.Pierce));
        // Each lead runs from the centre of its cutout to the nearest wall: no longer than the
        // cutout is deep, and no lead-out follows.
        var leads = proposal.Execution.Motions.Where(m => m.Layer == LayerType.Leadin).ToArray();
        Assert.Equal(2, leads.Count(m => m.Length < 0.031));
        Assert.DoesNotContain(proposal.Execution.Motions, m => m.Layer == LayerType.Leadout);
        var circlePierce = holes.Single(c => c.Pierce!.Value.X > 4).Pierce!.Value;
        Assert.True(circlePierce.DistanceTo(new Vector(5, 1.5)) < 0.004);
    }

    [Fact]
    public void Planner_CutoutWithRoomForItsLeadKeepsIt()
    {
        var program = new Program();
        program.Merge(Rectangle(2, 1, 4, 2));
        program.Merge(Rectangle(0, 0, 10, 3));
        var part = new Part(new Drawing("plate", program));

        var (_, result) = Plan(new CuttingParameters(), part);

        Assert.True(result.Status == CuttingPlanStatus.Ready, Describe(result));
        Assert.All(Assert.Single(result.ProposedOrder).ContourChoices, c => Assert.Null(c.Pierce));
    }

    [Fact]
    public void Capture_CutoutJustBelowAnotherSeamVertexIsNotNestedInIt()
    {
        // Text baselines a micro-inch apart: the parity ray from the left cutout's start ran
        // exactly through the right cutout's seam vertex, whose two copies differ in the last
        // bit, and counted one of its edges, so the left cutout read as nested in it.
        var program = new Program();
        program.MoveTo(4.393012, 0.8892639999999999);
        program.LineTo(4.242387, 0.8892639999999999);
        program.LineTo(4.242387, 0.911497);
        program.LineTo(4.393012, 0.911497);
        program.LineTo(4.393012, 0.889264);
        program.MoveTo(1.834001, 0.8892629999999999);
        program.LineTo(1.834001, 0.95);
        program.LineTo(1.9, 0.95);
        program.LineTo(1.9, 0.8892629999999999);
        program.LineTo(1.834001, 0.8892629999999999);
        program.Merge(Rectangle(0, 0, 10, 2));

        var material = LeadMaterialSnapshot.Capture(program, Vector.Zero);

        Assert.True(material.IsComplete, material.Reason);
    }

    private static (CuttingPlanSnapshot, CuttingPlanResult) Plan(CuttingParameters parameters, params Part[] parts)
    {
        var snapshot = CuttingPlanService.Capture(new CuttingPlanRequest(parts, Start,
            confirmedParameters: parameters, preservePartOrder: true));
        return (snapshot, CuttingPlanService.Plan(snapshot));
    }

    private static Shape Shape(Program program) =>
        ShapeBuilder.GetShapes(OpenNest.Converters.ConvertProgram.ToGeometry(program)
            .Where(e => e.Layer != SpecialLayers.Rapid).ToList()).Single();

    private static Program Rectangle(double x1, double y1, double x2, double y2) =>
        LeadPathValidationTests.Rectangle(x1, y1, x2, y2);

    private static Program Circle(double x, double y, double radius)
    {
        var p = new Program();
        p.MoveTo(x + radius, y);
        p.ArcTo(x + radius, y, x, y, RotationType.CCW);
        return p;
    }

    private static string Describe(CuttingPlanResult result) =>
        $"{result.Status}: " + string.Join("; ", result.Findings.Select(f => f.Message).Take(5));
}
