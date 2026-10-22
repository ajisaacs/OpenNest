using OpenNest.CNC;
using OpenNest.CNC.CuttingPlanning;
using OpenNest.Diagnostics;
using OpenNest.Engine.CuttingPlanning;
using OpenNest.Geometry;

namespace OpenNest.Tests.CuttingPlanning;

public class SafestRouteFallbackTests
{
    [Fact]
    public void KeptOrderThroughAWall_OffersTheSafestRouteAsAnUnverifiedPlan()
    {
        var plate = WalledPlate();
        var proposal = CuttingPlanBatch.Capture([plate], ExplicitContourTests.Parameters(), preservePartOrder: true).Plan();

        var planned = Assert.Single(proposal.Plates);
        Assert.True(planned.Result.IsSafestRouteFallback);
        Assert.Equal(CuttingPlanStatus.BestEffort, planned.Result.Status);
        Assert.False(planned.IsReady);
        Assert.True(planned.CanApplyWithWarnings);
        Assert.False(planned.HasOnlyFixedLeadWarnings);
        Assert.False(planned.SearchLimitReached);
        Assert.Equal(plate.Parts, planned.Result.ProposedOrder.Select(p => p.SourcePart));
        // Only the rapid from the left square to the right one has to cross the cut bar.
        var crossing = Assert.Single(planned.Result.Findings, f => f.Kind == PostVerificationKind.RapidCrossing);
        Assert.Equal(2, crossing.SourceOrdinal);
        Assert.Equal(0, crossing.OtherSourceOrdinal);
        Assert.DoesNotContain(planned.Result.Findings, f => f.Kind is null or PostVerificationKind.Incomplete);
        Assert.True(proposal.RequiresWarningAcceptance);
        var text = string.Join("\n", proposal.Describe("in"));
        Assert.Contains("Safest-route plan available", text);
        Assert.Contains("safest route found, unverified", text);
        Assert.Contains("breaks 1 check", text);
    }

    [Fact]
    public void SafestRoute_AppliesOnlyWithExplicitAcceptance()
    {
        var plate = WalledPlate();
        var proposal = CuttingPlanBatch.Capture([plate], ExplicitContourTests.Parameters(), preservePartOrder: true).Plan();

        Assert.Equal(CuttingCommitStatus.InvalidInput, proposal.Apply().Status);
        Assert.All(plate.Parts, part => Assert.False(part.HasManualLeadIns));

        Assert.Equal(CuttingCommitStatus.Applied, proposal.Apply(acceptWarnings: true).Status);
        Assert.All(plate.Parts, part => Assert.True(part.HasManualLeadIns));
    }

    [Fact]
    public void FreeOrder_FindsTheRouteThatPassesEveryCheck_WithoutTheFallback()
    {
        var proposal = CuttingPlanBatch.Capture([WalledPlate()], ExplicitContourTests.Parameters(), preservePartOrder: false).Plan();

        var planned = Assert.Single(proposal.Plates);
        Assert.True(planned.IsReady);
        Assert.False(planned.Result.IsSafestRouteFallback);
    }

    [Fact]
    public void SafestRoute_ThatPassesEveryCheck_IsReadyAndReplayed()
    {
        var snapshot = CuttingPlanService.Capture(CuttingPlanRequest.ForPlate(WalledPlate(),
            confirmedParameters: ExplicitContourTests.Parameters()));

        var result = CuttingPlanService.PlanSafestRoute(snapshot, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(CuttingPlanStatus.Ready, result.Status);
        Assert.True(result.IndependentlyReplayed);
        Assert.False(result.IsSafestRouteFallback);
        Assert.Empty(result.Findings);
    }

    [Fact]
    public void SafestRoute_NeverMutatesThePlate()
    {
        var plate = WalledPlate();
        var programs = plate.Parts.Select(p => ExplicitContourTests.Fingerprint(p.Program)).ToArray();
        var snapshot = CuttingPlanService.Capture(CuttingPlanRequest.ForPlate(plate,
            confirmedParameters: ExplicitContourTests.Parameters(), preservePartOrder: true));

        Assert.NotNull(CuttingPlanService.PlanSafestRoute(snapshot, CancellationToken.None));

        Assert.Equal(programs, plate.Parts.Select(p => ExplicitContourTests.Fingerprint(p.Program)));
        Assert.All(plate.Parts, part => Assert.False(part.HasManualLeadIns));
    }

    [Fact]
    public void Severity_RanksMaterialBeforeRapidsBeforeEverythingElse()
    {
        var lead = RouteSeverity.Of([null]);
        var rapids = RouteSeverity.Of([PostVerificationKind.RapidCrossing, PostVerificationKind.RapidCrossing]);
        var minor = RouteSeverity.Of([PostVerificationKind.PierceZone, PostVerificationKind.PartSupport, PostVerificationKind.MissingLeadIn]);

        Assert.Equal(new RouteSeverity(1, 0, 0), lead);
        Assert.True(minor < rapids);
        Assert.True(rapids < lead);
        Assert.True(default(RouteSeverity) < minor);
        Assert.True(default(RouteSeverity).IsClear);
        Assert.Equal(new RouteSeverity(1, 0, 0), RouteSeverity.Of([PostVerificationKind.NestedCutOrder]));
        Assert.Equal(new RouteSeverity(1, 0, 0), RouteSeverity.Of([PostVerificationKind.Incomplete]));
    }

    // A tall bar between two squares, in the order bar, left, right: once the bar is cut every
    // straight rapid from the left square to the right one crosses it. Cut last, it crosses nothing.
    private static Plate WalledPlate()
    {
        var plate = new Nest().CreatePlate();
        plate.Size = new Size(60, 60);
        plate.Parts.Add(new Part(new Drawing("bar", Rectangle(1, 50)), new Vector(25, 2)));
        plate.Parts.Add(new Part(new Drawing("left", ExplicitContourTests.Square(false)), new Vector(5, 20)));
        plate.Parts.Add(new Part(new Drawing("right", ExplicitContourTests.Square(false)), new Vector(36, 20)));
        return plate;
    }

    private static Program Rectangle(double width, double height)
    {
        var program = new Program();
        program.MoveTo(0, 0);
        program.LineTo(0, height);
        program.LineTo(width, height);
        program.LineTo(width, 0);
        program.LineTo(0, 0);
        return program;
    }
}
