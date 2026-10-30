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
    public void Find_SlotIsPiercedOnItsCenterLineNearEachTarget()
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
        // Each lead runs from near the center line of its cutout to its entry: short, and no
        // lead-out follows.
        var leads = proposal.Execution.Motions.Where(m => m.Layer == LayerType.Leadin).ToArray();
        Assert.Equal(2, leads.Count(m => m.Length < 0.05));
        Assert.DoesNotContain(proposal.Execution.Motions, m => m.Layer == LayerType.Leadout);
        var slotPierce = holes.Single(c => c.Pierce!.Value.X < 3).Pierce!.Value;
        Assert.InRange(slotPierce.Y, 1.0225, 1.0275);
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

    [Theory]
    [InlineData(90)]
    [InlineData(60)]
    public void Planner_RoundHoleSmallerThanItsLeadIsPiercedAtItsCenter(double approach)
    {
        var program = new Program();
        program.Merge(Circle(2, 1.5, 0.09));
        program.Merge(Rectangle(0, 0, 4, 3));
        var part = new Part(new Drawing("plate", program));
        var parameters = new CuttingParameters
        {
            InternalLeadIn = new LineLeadIn { Length = 0.15, ApproachAngle = approach },
            InternalLeadOut = new NoLeadOut(),
            PierceClearance = 0.0625,
            PierceDiameter = 0.03,
        };

        var (_, result) = Plan(parameters, part);

        Assert.True(result.Status == CuttingPlanStatus.Ready, Describe(result));
        var lead = Assert.Single(Assert.Single(result.ProposedOrder).Execution.Motions, m => m.Layer == LayerType.Leadin);
        Assert.True(lead.Start!.Value.DistanceTo(new Vector(2, 1.5)) < 1e-6);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(0.03)]
    public void Planner_LeadStopsAtTheCenterLineOfANarrowStroke(double pierceDiameter)
    {
        // A slot 0.06 wide: the 0.15 lead would fit along it at a shallow angle.
        var program = new Program();
        program.Merge(Rectangle(1, 1, 2, 1.06));
        program.Merge(Rectangle(0, 0, 4, 3));
        var part = new Part(new Drawing("plate", program));
        var parameters = new CuttingParameters
        {
            InternalLeadIn = new LineLeadIn { Length = 0.15, ApproachAngle = 15 },
            InternalLeadOut = new NoLeadOut(),
            PierceDiameter = pierceDiameter,
        };

        var (_, result) = Plan(parameters, part);

        Assert.True(result.Status == CuttingPlanStatus.Ready, Describe(result));
        var lead = Assert.Single(Assert.Single(result.ProposedOrder).Execution.Motions, m => m.Layer == LayerType.Leadin);
        Assert.InRange(lead.Start!.Value.Y, 1.029, 1.031);
        Assert.True(lead.Length < 0.05, $"Lead length {lead.Length}");
    }

    [Fact]
    public void Intersects_RadialLineInASmallCircleHitsOnlyItsWall()
    {
        var circle = new Circle(new Vector(5, 1.5), 0.03);

        Assert.True(circle.Intersects(new Line(new Vector(5, 1.5), new Vector(5.03, 1.5)), out var points));

        Assert.Equal(5.03, Assert.Single(points).X, 9);
    }

    [Fact]
    public void StopAtCenterLine_LeadThatStaysOnItsOwnSideKeepsItsShape()
    {
        var hole = Shape(Rectangle(0, 0, 2, 2));
        var lead = new LineLeadIn { Length = 0.15, ApproachAngle = 60 };

        Assert.Same(lead, ContourCuttingStrategy.StopAtCenterLine(hole, new Vector(1, 0), lead,
            System.Math.PI / 2, RotationType.CW));
    }

    [Fact]
    public void StopAtCenterLine_LeadLeavingACutoutIsLeftForTheLeadCheck()
    {
        var hole = Shape(Rectangle(0, 0, 1, 0.06));
        var lead = new LineLeadIn { Length = 0.5, ApproachAngle = 90 };

        Assert.Same(lead, ContourCuttingStrategy.StopAtCenterLine(hole, new Vector(0.5, 0), lead,
            System.Math.PI / 2, RotationType.CW));
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

    // Optional private input: never embed customer geometry or names in the repository.
    // SmallFeatureLeadNestPath: a plate of serial-number tags whose 27 stencil-text cutouts are too
    // narrow for the lead-in; only eroded pierces and a reading-order hole sweep cut them all.
    [SkippableTheory]
    [InlineData(0.125)]
    [InlineData(0.5)]
    public void TagPlate_PlansEveryCutoutInReadingOrder(double lead)
    {
        var path = TestConfig.GetExistingPath("SmallFeatureLeadNestPath");
        Skip.If(path == null, "Configure SmallFeatureLeadNestPath in ignored test-config.json.");
        var plate = new OpenNest.IO.NestReader(path!).Read().Plates.First(p => p.Parts.Count > 0);
        var parameters = new CuttingParameters { InternalLeadIn = new LineLeadIn { Length = lead, ApproachAngle = 90 } };

        var proposal = CuttingPlanBatch.Capture([plate], parameters, preservePartOrder: false).Plan();

        Assert.True(proposal.CanApply, string.Join(Environment.NewLine, proposal.Describe("in")));
        var planned = Assert.Single(proposal.Plates);
        Assert.Equal(CuttingPlanStatus.Ready, planned.Result.Status);
        Assert.Equal(plate.Parts.Count, planned.PartCount);
    }

    // Optional private input: never embed customer geometry or names in the repository.
    // ArcTextNestPath: a column of tags with engraved-style arc text, where a letter's one fitting
    // lead faces away from the route, or leads tangent to its walls check as uncertain. Only
    // recovery's widened tries, with each part's exit held to face the next part, cut them all.
    [SkippableFact]
    public void ArcTextPlate_PlansWithItsPartsLeftTowardTheNext()
    {
        var path = TestConfig.GetExistingPath("ArcTextNestPath");
        Skip.If(path == null, "Configure ArcTextNestPath in ignored test-config.json.");
        var plate = new OpenNest.IO.NestReader(path!).Read().Plates.First(p => p.Parts.Count > 0);
        var parameters = new CuttingParameters
        {
            ExternalLeadIn = new LineLeadIn { Length = 0.15, ApproachAngle = 60 },
            ExternalLeadOut = new LineLeadOut { Length = 0.08, ApproachAngle = 60 },
            InternalLeadIn = new LineLeadIn { Length = 0.15, ApproachAngle = 90 },
        };

        var proposal = CuttingPlanBatch.Capture([plate], parameters, preservePartOrder: false).Plan();

        Assert.True(proposal.CanApply, string.Join(Environment.NewLine, proposal.Describe("in")));
        Assert.Equal(CuttingPlanStatus.Ready, Assert.Single(proposal.Plates).Result.Status);
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
