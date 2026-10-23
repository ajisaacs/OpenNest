using OpenNest.CNC;
using OpenNest.CNC.CuttingPlanning;
using OpenNest.Engine.CuttingPlanning;
using OpenNest.Geometry;

namespace OpenNest.Tests.CuttingPlanning;

/// <summary>
/// Plan Cutting may cut a head-locked cut-off's far-edge segment from either end, and Apply keeps
/// the chosen direction on the definition. Without the head lock the direction stays fixed.
/// </summary>
public class HeadLockedCutOffPlanningTests
{
    [Fact]
    public void Plan_StartsAHeadLockedSegmentAtTheFarEdgeWhenTheToolIsThere_AndApplyKeepsIt()
    {
        var (plate, cutOff) = SingleLine(headLock: 1.5);

        var result = CuttingPlanService.Plan(CuttingPlanRequest.ForPlate(plate, startPoint: new Vector(40, 60), confirmedParameters: Parameters));

        Assert.True(result.Status == CuttingPlanStatus.Ready && result.IndependentlyReplayed, Describe(result));
        var proposal = Assert.Single(result.ProposedOrder);
        Assert.True(proposal.ReversesCutOff);
        Assert.True(CutOff.StartsAtFarEdge(proposal.CopyProgram()));

        Assert.Equal(CuttingCommitStatus.Applied, CuttingPlanService.Apply([result]).Status);
        Assert.True(cutOff.FarEdgeFirst);
        var segment = Assert.Single(plate.Parts);
        Assert.True(CutOff.StartsAtFarEdge(segment.Program));
        Assert.True(CutOff.StartsAtFarEdge(cutOff.Drawing.Program));

        // Regeneration keeps the direction, and replanning from the origin turns it back.
        plate.RegenerateCutOffs(new CutOffSettings());
        Assert.True(CutOff.StartsAtFarEdge(Assert.Single(plate.Parts).Program));
        var back = CuttingPlanService.Plan(CuttingPlanRequest.ForPlate(plate, confirmedParameters: Parameters));
        Assert.True(back.Status == CuttingPlanStatus.Ready, Describe(back));
        Assert.True(Assert.Single(back.ProposedOrder).ReversesCutOff);
        Assert.Equal(CuttingCommitStatus.Applied, CuttingPlanService.Apply([back]).Status);
        Assert.False(cutOff.FarEdgeFirst);
        Assert.False(CutOff.StartsAtFarEdge(Assert.Single(plate.Parts).Program));
    }

    [Fact]
    public void Plan_KeepsTheDirectionWhenTheToolIsAtTheOrigin()
    {
        var (plate, cutOff) = SingleLine(headLock: 1.5);

        var result = CuttingPlanService.Plan(CuttingPlanRequest.ForPlate(plate, confirmedParameters: Parameters));

        Assert.True(result.Status == CuttingPlanStatus.Ready, Describe(result));
        Assert.False(Assert.Single(result.ProposedOrder).ReversesCutOff);
        Assert.Equal(CuttingCommitStatus.Applied, CuttingPlanService.Apply([result]).Status);
        Assert.False(cutOff.FarEdgeFirst);
    }

    [Fact]
    public void Plan_NeverReversesACutOffWithoutTheHeadLock()
    {
        var (plate, _) = SingleLine(headLock: 0);

        var result = CuttingPlanService.Plan(CuttingPlanRequest.ForPlate(plate, startPoint: new Vector(40, 60), confirmedParameters: Parameters));

        Assert.True(result.Status == CuttingPlanStatus.Ready, Describe(result));
        var proposal = Assert.Single(result.ProposedOrder);
        Assert.False(proposal.ReversesCutOff);
        Assert.Equal(new Vector(40, 0), ((RapidMove)proposal.CopyProgram().Codes[0]).EndPoint);
    }

    // A line through a part is cut in segments placed before the part; only the segment reaching
    // the far edge is head-locked, so only it may be started from that edge.
    [Fact]
    public void Plan_SegmentsBeforeAPart_OnlyTheFarEdgeSegmentMayStartAtTheFarEdge()
    {
        var (plate, cutOff) = SingleLine(headLock: 1.5);
        plate.Parts.Add(TestHelpers.MakePartAt(35, 30, 10));
        plate.RegenerateCutOffs(new CutOffSettings());
        var segments = CutOffSegments.Of(plate, cutOff);
        Assert.Equal(2, segments.Count);
        // Segments due in the same gap chain in plate order: the far one first, from the start point.
        plate.Parts.Reorder([segments[1].Part, segments[0].Part, plate.Parts.Single(p => !p.BaseDrawing.IsCutOff)]);

        var result = CuttingPlanService.Plan(CuttingPlanRequest.ForPlate(plate, startPoint: new Vector(40, 62),
            confirmedParameters: Parameters));

        Assert.True(result.Status == CuttingPlanStatus.Ready && result.IndependentlyReplayed, Describe(result));
        var order = result.ProposedOrder.Select(p => p.SourcePart).ToList();
        var part = plate.Parts.Single(p => !p.BaseDrawing.IsCutOff);
        Assert.All(segments, segment => Assert.True(order.IndexOf(segment.Part) < order.IndexOf(part)));
        var far = result.ProposedOrder.Single(p => ReferenceEquals(p.SourcePart, segments[1].Part));
        Assert.True(far.ReversesCutOff);
        Assert.False(result.ProposedOrder.Single(p => ReferenceEquals(p.SourcePart, segments[0].Part)).ReversesCutOff);

        Assert.Equal(CuttingCommitStatus.Applied, CuttingPlanService.Apply([result]).Status);
        Assert.True(cutOff.FarEdgeFirst);
        Assert.True(CutOff.StartsAtFarEdge(segments[1].Part.Program));
        Assert.False(HeadHeightLock.IsMarked(segments[0].Part.Program));
    }

    [Fact]
    public void Apply_ChangedDirectionAfterPlanning_IsStale()
    {
        var (plate, cutOff) = SingleLine(headLock: 1.5);
        var result = CuttingPlanService.Plan(CuttingPlanRequest.ForPlate(plate, startPoint: new Vector(40, 60), confirmedParameters: Parameters));
        Assert.True(result.Status == CuttingPlanStatus.Ready, Describe(result));

        cutOff.FarEdgeFirst = true;

        Assert.Equal(CuttingCommitStatus.Stale, CuttingPlanService.Apply([result]).Status);
        Assert.False(CutOff.StartsAtFarEdge(Assert.Single(plate.Parts).Program));
    }

    private static readonly OpenNest.CNC.CuttingStrategy.CuttingParameters Parameters = ExplicitContourTests.Parameters();

    private static (Plate Plate, CutOff CutOff) SingleLine(double headLock)
    {
        var nest = new Nest();
        var plate = nest.CreatePlate();
        plate.Size = new Size(60, 120);
        var cutOff = new CutOff(new Vector(40, 0), CutOffAxis.Vertical) { HeadLockOvercut = headLock };
        plate.CutOffs.Add(cutOff);
        plate.RegenerateCutOffs(new CutOffSettings());
        return (plate, cutOff);
    }

    private static string Describe(CuttingPlanResult r) =>
        $"{r.Status}, expanded {r.Expansions}: " + string.Join("; ", r.Findings.Select(f => f.Message));
}
