using OpenNest.CNC;
using OpenNest.CNC.CuttingPlanning;
using OpenNest.CNC.CuttingStrategy;
using OpenNest.Diagnostics;
using OpenNest.Engine.CuttingPlanning;
using OpenNest.Geometry;

namespace OpenNest.Tests.CuttingPlanning;

public class PierceZoneTests
{
    private static readonly Vector Start = new(-2, 5);

    [Fact]
    public void Validator_RadiusZeroChecksPathsOnly_PositiveRadiusRejectsNearbyMaterial()
    {
        var target = Snapshot(Rectangle(0, 0, 10, 10));
        var neighbour = Snapshot(Rectangle(-6, 0, -4, 10)); // 1 from the pierce at (-3, 5)
        var lead = Read(ExternalLead());

        Assert.True(LeadPathValidator.Check(lead, target, [target, neighbour]).IsClear);
        AssertClear(LeadPathValidator.Check(lead, target, [target, neighbour], 0.9));
        var near = LeadPathValidator.Check(lead, target, [target, neighbour], 1.2);
        Assert.True(near.IsComplete, near.Reason);
        Assert.False(near.IsClear);
        Assert.Contains("another placed part", near.Reason);
        Assert.Contains("2.4 pierce diameter needs 1.2", near.Reason);
        var own = LeadPathValidator.Check(lead, target, [target], 3.5);
        Assert.True(own.IsComplete, own.Reason);
        Assert.False(own.IsClear);
        Assert.Contains("its own part's material", own.Reason);
    }

    [Fact]
    public void Validator_DirectContourPierceIsNotChecked_InvalidRadiusIsIncomplete()
    {
        var clean = Rectangle(0, 0, 10, 10);
        var target = Snapshot(clean);

        AssertClear(LeadPathValidator.Check(Read(clean), target, [target], 5));
        var invalid = LeadPathValidator.Check(Read(ExternalLead()), target, [], -1);
        Assert.False(invalid.IsComplete);
        Assert.False(invalid.IsClear);
    }

    [Fact]
    public void Validator_FixedPierceFailureIsCollectedNotReturned()
    {
        var target = Snapshot(Rectangle(0, 0, 10, 10));
        var neighbour = Snapshot(Rectangle(-6, 0, -4, 10));
        var failures = new List<FixedLeadFailure>();

        var result = LeadPathValidator.Check(Read(ExternalLead()), target, [target, neighbour], _ => true,
            failures, default, pierceRadius: 1.2);

        AssertClear(result);
        Assert.Equal(1, Assert.Single(failures).Obstacle);
    }

    [Fact]
    public void ResolvePierceZone_ClearOrOffLeadsAreReturnedAsGiven()
    {
        var hole = Slot();
        var lead = new LineLeadIn { Length = 0.28, ApproachAngle = 90 };

        Assert.Same(lead, ContourCuttingStrategy.ResolvePierceZone(hole, new Vector(2, 5), lead, 0, 0));
        var none = new NoLeadIn();
        Assert.Same(none, ContourCuttingStrategy.ResolvePierceZone(hole, new Vector(2, 5), none, 0, 0.25));
        // A pierce 0.28 into the slot keeps 0.28 from its end and 0.3 from its sides.
        Assert.Same(lead, ContourCuttingStrategy.ResolvePierceZone(hole, new Vector(2, 5), lead, 0, 0.25));
    }

    [Fact]
    public void ResolvePierceZone_SlotEndMovesOnlyAsFarAsTheZoneNeeds()
    {
        var hole = Slot();
        var point = new Vector(2, 5);

        var resolved = ContourCuttingStrategy.ResolvePierceZone(hole, point,
            new LineLeadIn { Length = 0.1, ApproachAngle = 90 }, 0, 0.25);

        // Toward the slot's pole, not all the way to it: 0.25 in from the end wall.
        var pierce = resolved.GetPiercePoint(point, 0);
        Assert.Equal(2.25, pierce.X, 6);
        Assert.Equal(5, pierce.Y, 2); // The pole is found to within a hundredth of the zone.
        Assert.IsType<LineLeadIn>(resolved);
    }

    [Fact]
    public void SquareHole_ShortLeadPiercesAtTheZoneEdge()
    {
        var clean = Rectangle(0, 0, 10, 10);
        clean.Codes.AddRange(Rectangle(3, 3, 7, 7).Codes);
        var target = Snapshot(clean);

        var legacy = Read(new ContourCuttingStrategy { Parameters = Parameters(0) }.Apply(clean, Start).Program);
        Assert.False(LeadPathValidator.Check(legacy, target, [], 0.5).IsClear);

        var execution = Read(new ContourCuttingStrategy { Parameters = Parameters(1) }.Apply(clean, Start).Program);
        var pierce = Assert.Single(Pierces(execution), p => p.X > 3 && p.X < 7 && p.Y > 3 && p.Y < 7);
        var clearance = new[] { pierce.X - 3, 7 - pierce.X, pierce.Y - 3, 7 - pierce.Y }.Min();
        Assert.InRange(clearance, 0.5 - 1e-6, 0.5 + 1e-6);
        AssertClear(LeadPathValidator.Check(execution, target, [], 0.5));
    }

    [Fact]
    public void RoundHole_ShortLeadPiercesAtTheZoneEdge_TooSmallHolePiercesAtItsCentre()
    {
        var clean = Rectangle(0, 0, 10, 10);
        clean.Codes.AddRange(Circle(3, 5, 2).Codes);
        clean.Codes.AddRange(Circle(7.5, 5, 0.3).Codes);
        var target = Snapshot(clean);

        var execution = Read(new ContourCuttingStrategy { Parameters = Parameters(1) }.Apply(clean, Start).Program);
        var pierces = Pierces(execution);
        Assert.Contains(pierces, p => System.Math.Abs(p.DistanceTo(new Vector(3, 5)) - 1.5) < 1e-6);
        Assert.Contains(pierces, p => p.DistanceTo(new Vector(7.5, 5)) < 1e-6);
        var result = LeadPathValidator.Check(execution, target, [], 0.5);
        Assert.True(result.IsComplete, result.Reason);
        Assert.False(result.IsClear); // The small hole cannot hold the zone; it is reported, not hidden.
        Assert.Contains("its own part's material", result.Reason);
    }

    [Fact]
    public void Planner_ReadyPlanKeepsEveryPierceZone()
    {
        var host = Rectangle(0, 0, 10, 10);
        host.Codes.AddRange(Rectangle(3, 3, 7, 7).Codes);
        var part = new Part(new Drawing("host", host));
        var neighbour = new Part(new Drawing("neighbour", Rectangle(0, 0, 2, 2)), new Vector(10.6, 4));
        var parameters = Parameters(1);

        var snapshot = CuttingPlanService.Capture(new CuttingPlanRequest([part, neighbour], Start,
            confirmedParameters: parameters, preservePartOrder: true));
        var result = CuttingPlanService.Plan(snapshot);

        Assert.True(result.Status == CuttingPlanStatus.Ready, Describe(result));
        var materials = snapshot.Placements.Select(p => p.Material).ToArray();
        foreach (var proposal in result.ProposedOrder)
            AssertClear(LeadPathValidator.Check(proposal.Execution,
                snapshot.Placements[proposal.SourceOrdinal].Material, materials, 0.5));
    }

    [Fact]
    public void Planner_PerimeterLeadShorterThanTheZoneIsAConflictNotAPlan()
    {
        var part = new Part(new Drawing("plain", Rectangle(0, 0, 10, 10)));
        var parameters = Parameters(1);
        parameters.ExternalLeadIn = new LineLeadIn { Length = 0.3, ApproachAngle = 90 };

        var result = CuttingPlanService.Plan(CuttingPlanService.Capture(new CuttingPlanRequest([part], Start,
            confirmedParameters: parameters, preservePartOrder: true)));

        Assert.NotEqual(CuttingPlanStatus.Ready, result.Status);
        Assert.Contains(result.Findings, f => f.Message.Contains("pierce diameter"));
    }

    [Fact]
    public void PrePostReview_ReportsAPierceNearAnotherPart_OnlyWithAPierceDiameter()
    {
        var nest = new Nest();
        var plate = nest.CreatePlate();
        plate.Parts.Add(LeadPart(new Vector(0, 0), leadStart: new Vector(-0.5, 5)));
        plate.Parts.Add(new Part(new Drawing("neighbour", Rectangle(0, 0, 2, 10)), new Vector(-2.7, 0)));

        Assert.DoesNotContain(PostVerificationAnalyzer.Analyze(nest).Findings,
            f => f.Kind == PostVerificationKind.PierceZone);

        plate.CuttingParameters = new CuttingParameters { PierceDiameter = 1 };
        var report = PostVerificationAnalyzer.Analyze(nest);
        var finding = Assert.Single(report.Findings, f => f.Kind == PostVerificationKind.PierceZone);
        Assert.Equal(1, finding.PartNumber);
        Assert.Equal(2, finding.OtherPartNumber);
        Assert.Equal(new Vector(-0.5, 5), finding.Location);
        Assert.False(report.CanPost(false));
        Assert.Contains("Pierce zones: 1 warning(s)", report.ToDisplayText());
    }

    [Fact]
    public void PrePostReview_ReportsAPierceTooCloseToItsOwnPart()
    {
        var nest = new Nest();
        var plate = nest.CreatePlate();
        plate.Parts.Add(LeadPart(Vector.Zero, leadStart: new Vector(-0.2, 5)));
        plate.CuttingParameters = new CuttingParameters { PierceDiameter = 1 };

        var finding = Assert.Single(PostVerificationAnalyzer.Analyze(nest).Findings,
            f => f.Kind == PostVerificationKind.PierceZone);

        Assert.Null(finding.OtherPartNumber);
        Assert.Contains("its own part's material", finding.Message);
    }

    // A 10x10 part whose placed program pierces at leadStart and leads in to (0, 5).
    private static Part LeadPart(Vector location, Vector leadStart)
    {
        var part = new Part(new Drawing("lead", Rectangle(0, 0, 10, 10)), location);
        var placed = new Program();
        placed.MoveTo(leadStart.X, leadStart.Y);
        placed.Codes.Add(new LinearMove(0, 5) { Layer = LayerType.Leadin });
        placed.LineTo(0, 10); placed.LineTo(10, 10); placed.LineTo(10, 0); placed.LineTo(0, 0); placed.LineTo(0, 5);
        Assert.True(part.RestoreLeadInProgram(placed, false));
        return part;
    }

    private static CuttingParameters Parameters(double pierceDiameter) => new()
    {
        ExternalLeadIn = new LineLeadIn { Length = 1, ApproachAngle = 90 },
        InternalLeadIn = new LineLeadIn { Length = 0.15, ApproachAngle = 90 },
        ArcCircleLeadIn = new LineLeadIn { Length = 0.15, ApproachAngle = 90 },
        ExternalLeadOut = new NoLeadOut(),
        InternalLeadOut = new NoLeadOut(),
        ArcCircleLeadOut = new NoLeadOut(),
        PierceClearance = 0,
        PierceDiameter = pierceDiameter,
    };

    // A 6 x 0.6 slot from (2, 4.7) to (8, 5.3).
    private static Shape Slot()
    {
        var shape = new Shape();
        shape.Entities.Add(new Line(new Vector(2, 4.7), new Vector(8, 4.7)));
        shape.Entities.Add(new Line(new Vector(8, 4.7), new Vector(8, 5.3)));
        shape.Entities.Add(new Line(new Vector(8, 5.3), new Vector(2, 5.3)));
        shape.Entities.Add(new Line(new Vector(2, 5.3), new Vector(2, 4.7)));
        return shape;
    }

    private static List<Vector> Pierces(OwnedExecution execution)
    {
        var motions = execution.Motions;
        return motions.Where((m, i) => !m.Rapid && m.Layer == LayerType.Leadin && m.Start != null
                && (i == 0 || motions[i - 1].Rapid))
            .Select(m => m.Start!.Value).ToList();
    }

    private static Program Rectangle(double x1, double y1, double x2, double y2) =>
        LeadPathValidationTests.Rectangle(x1, y1, x2, y2);

    private static Program Circle(double x, double y, double radius)
    {
        var p = new Program();
        p.MoveTo(x + radius, y);
        p.ArcTo(x + radius, y, x, y, RotationType.CCW);
        return p;
    }

    private static Program ExternalLead()
    {
        var p = new Program();
        p.MoveTo(-3, 5);
        p.Codes.Add(new LinearMove(0, 5) { Layer = LayerType.Leadin });
        p.LineTo(0, 10);
        return p;
    }

    private static LeadMaterialSnapshot Snapshot(Program clean)
    {
        var snapshot = LeadMaterialSnapshot.Capture(clean, Vector.Zero);
        Assert.True(snapshot.IsComplete, snapshot.Reason);
        return snapshot;
    }

    private static OwnedExecution Read(Program p) => ExecutionMotionReader.Read(p, Vector.Zero, null, default);

    private static void AssertClear(LeadPathValidationResult result)
    {
        Assert.True(result.IsComplete, result.Reason);
        Assert.True(result.IsClear, result.Reason);
    }

    private static string Describe(CuttingPlanResult result) =>
        $"{result.Status}: " + string.Join("; ", result.Findings.Select(f => f.Message).Take(5));
}
