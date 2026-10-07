using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using OpenNest.CNC;
using OpenNest.CNC.CuttingPlanning;
using OpenNest.CNC.CuttingStrategy;
using OpenNest.Engine.CuttingPlanning;
using OpenNest.Geometry;

namespace OpenNest.Tests.CuttingPlanning;

public class SideCoverageRegressionTests
{
    [Fact]
    public void DefaultCap_ReusesClearBottomCandidatePassedWhileSeekingLeft()
    {
        var clean = new Program();
        clean.MoveTo(0, 0);
        clean.LineTo(0, 10);
        for (var i = 1; i <= 12; i++)
            clean.LineTo(i * 10.0 / 12, 10);
        for (var i = 1; i <= 12; i++)
            clean.LineTo(10, 10 - i * 10.0 / 12);
        clean.LineTo(0, 0);
        var parameters = Parameters(0.15, 90, 0);
        var prepared = PreparedContours.Capture(clean, parameters);
        var blockers = new[]
        {
            Rectangle(-0.5, -0.5, 1.5, 0.4), Rectangle(9, -0.5, 1.05, 0.4),
            Rectangle(10.1, -0.3, 0.5, 0.6), Rectangle(-0.5, 9.8, 0.4, 0.4),
        };
        var target = new Vector(20, 20);
        var ranked = prepared.AutomaticEntryCandidatesWithFallbacks(0, target)
            .RankTowardNextCut(target, new Vector(10, -2));
        var adapter = Adapter(prepared, clean, blockers);
        var calls = new HashSet<(long, long)>();

        var selected = ContourEntrySelection.Select(ranked, candidate =>
        {
            Assert.True(calls.Add(candidate.GeometryKey), "A candidate was evaluated twice.");
            return adapter.Check(candidate.Choice);
        });

        Assert.Equal(16, selected.Choices.Count);
        var bottom = ranked.Single(c => c.Choice.Point.DistanceTo(new Vector(5, 0)) < 1e-9);
        Assert.Equal(32, ranked.ToList().IndexOf(bottom));
        Assert.True(adapter.Check(bottom.Choice).IsClear);
        Assert.Equal(new[] { 0, 1, 2, 3 }, ClearSides(ranked, adapter));
        Assert.Equal(new[] { 0, 1, 2, 3 }, SelectedSides(selected, ranked));
        Assert.Contains(selected.Choices, c => c.Point.DistanceTo(bottom.Choice.Point) < 1e-9);
        Assert.All(selected.Choices, c => Assert.True(adapter.Check(c).IsClear));
        Assert.Equal(ContourSelectionShortfall.None, selected.Shortfall);
        AssertGlobalOrder(selected, ranked);
        Assert.Equal(calls.Count, selected.EvaluatedCount);
    }

    [Fact]
    public void SupportedCapFour_ReusesClearTopCandidatePassedWhileSeekingBottom()
    {
        var clean = Polygon();
        var prepared = PreparedContours.Capture(clean, Parameters());
        var target = new Vector(100 * System.Math.Cos(System.Math.PI), 100 * System.Math.Sin(System.Math.PI));
        var arrival = new Vector(40 * System.Math.Cos(3 * System.Math.PI / 2), 40 * System.Math.Sin(3 * System.Math.PI / 2));
        var ranked = prepared.AutomaticEntryCandidatesWithFallbacks(0, target).RankTowardNextCut(target, arrival);
        var adapter = Adapter(prepared, clean, []);

        var selected = ContourEntrySelection.Select(ranked, c => adapter.Check(c.Choice), 4);

        Assert.Equal(4, selected.Choices.Count);
        Assert.True(adapter.Check(ranked[4].Choice).IsClear);
        Assert.Contains(3, Sides(ranked[4].Choice.Point, ranked));
        Assert.Equal(new[] { 0, 1, 2, 3 }, ClearSides(ranked, adapter));
        Assert.Equal(new[] { 0, 1, 2, 3 }, SelectedSides(selected, ranked));
        Assert.All(selected.Choices, c => Assert.True(adapter.Check(c).IsClear));
        AssertGlobalOrder(selected, ranked);
    }

    [Theory]
    [InlineData(4)]
    [InlineData(16)]
    public void FullCheckedRoute_RetainsTopDepartureToLockedMarkedSquare(int cap)
    {
        var parameters = Parameters();
        var first = new Part(new Drawing("convex polygon", Polygon()));
        var nextClean = Rectangle(0, 0, 2, 2);
        nextClean.MoveTo(101, 21);
        nextClean.Codes.Add(new LinearMove(102, 21) { Layer = LayerType.Scribe });
        var next = new Part(new Drawing("locked marked square", nextClean), new Vector(-101, -1));
        var nextPrepared = PreparedContours.Capture(nextClean, parameters);
        Assert.True(next.RestoreLeadInProgram(nextPrepared.Emit([nextPrepared.ClosestEntry(0, new Vector(3, 1))]), true));
        var before = new[] { ExplicitContourTests.Fingerprint(first.Program), ExplicitContourTests.Fingerprint(next.Program) };
        var start = new Vector(-7.347880794884118e-15, -40);
        var snapshot = CuttingPlanService.Capture(new CuttingPlanRequest([first, next], start,
            confirmedParameters: parameters, preservePartOrder: true, maxEntries: cap));
        var source = snapshot.Placements[0];
        var prepared = source.Prepared;
        var witness = prepared.AutomaticEntryCandidatesWithFallbacks(0, new Vector(-100, 0))
            .First(c => c.Choice.Point.DistanceTo(new Vector(-4.99220639970189, 8.664749001718139)) < 1e-9);
        var witnessProgram = prepared.Emit([witness.Choice]);
        var witnessExecution = ExecutionMotionReader.Read(witnessProgram, source.Location, start, default);
        var witnessProposal = source.Propose(witnessProgram, witnessExecution, [witness.Choice], default);
        var witnessReplay = CuttingPlanService.ReplayPrograms(snapshot, [witnessProposal, snapshot.Placements[1]], 0, default);
        Assert.Equal(CuttingPlanStatus.Ready, witnessReplay.Status);
        Assert.True(witnessReplay.IndependentlyReplayed);

        var result = CuttingPlanService.Plan(snapshot);

        Assert.True(result.Status == CuttingPlanStatus.Ready,
            $"{result.Status}, {result.Expansions} expansions: {string.Join("; ", result.Findings.Select(f => f.Message))}");
        Assert.True(result.IndependentlyReplayed);
        Assert.Equal(new[] { 0, 1 }, result.ProposedOrder.Select(p => p.SourceOrdinal));
        Assert.Single(result.ProposedOrder[0].ContourChoices);
        Assert.False(result.ProposedOrder[1].IsRegenerated);
        var replay = CuttingPlanService.ReplayPrograms(snapshot, result.ProposedOrder, 0, default);
        Assert.Equal(CuttingPlanStatus.Ready, replay.Status);
        Assert.True(replay.IndependentlyReplayed);
        Assert.Equal(before, new[] { ExplicitContourTests.Fingerprint(first.Program), ExplicitContourTests.Fingerprint(next.Program) });
    }

    [Fact]
    public void CorrectiveRescan_PreservesGlobalRankAndLeavesTailUnevaluated()
    {
        var candidates = CorrectiveCandidates();
        var calls = new HashSet<(long, long)>();
        var result = ContourEntrySelection.Select(candidates, c =>
        {
            Assert.True(calls.Add(c.GeometryKey));
            return new(ContourFeasibilityStatus.Clear, null);
        }, 4);

        Assert.Equal(new[] { candidates[0].Choice, candidates[1].Choice, candidates[4].Choice, candidates[5].Choice }, result.Choices);
        Assert.Equal(6, result.EvaluatedCount);
        Assert.DoesNotContain(candidates[6].GeometryKey, result.EvaluatedKeys);
        Assert.Equal(ContourSelectionShortfall.None, result.Shortfall);
    }

    [Theory]
    [InlineData(ContourFeasibilityStatus.Blocked)]
    [InlineData(ContourFeasibilityStatus.Incomplete)]
    public void CorrectiveRescan_DoesNotPromoteUnclearSide(ContourFeasibilityStatus status)
    {
        var candidates = CorrectiveCandidates();
        var calls = new HashSet<(long, long)>();
        var result = ContourEntrySelection.Select(candidates, c =>
        {
            Assert.True(calls.Add(c.GeometryKey));
            return c.GeometryKey == candidates[4].GeometryKey
                ? new(status, "unclear bottom") : new(ContourFeasibilityStatus.Clear, null);
        }, 4);

        Assert.DoesNotContain(candidates[4].Choice, result.Choices);
        Assert.Equal(4, result.Choices.Count);
        Assert.Equal(status == ContourFeasibilityStatus.Incomplete ? ContourSelectionShortfall.Incomplete : ContourSelectionShortfall.None, result.Shortfall);
        if (status == ContourFeasibilityStatus.Incomplete)
        {
            Assert.Contains(candidates[4].Choice, result.UncertainChoices);
            Assert.DoesNotContain("No tested lead-in fits", result.Reason);
        }
        Assert.Equal(calls.Count, result.EvaluatedCount);
    }

    [Fact]
    public void CorrectiveRescan_HonoursCancellationBeforeReusingCachedVerdict()
    {
        var candidates = CorrectiveCandidates();
        using var cancellation = new CancellationTokenSource();
        Assert.ThrowsAny<OperationCanceledException>(() => ContourEntrySelection.Select(candidates, c =>
        {
            if (c.GeometryKey == candidates[5].GeometryKey)
                cancellation.Cancel();
            return new(ContourFeasibilityStatus.Clear, null);
        }, 4, cancellation.Token));
    }

    private static ContourEntryCandidate[] CorrectiveCandidates()
    {
        var prepared = PreparedContours.Capture(Rectangle(0, 0, 10, 10), Parameters());
        return new[] { (10, 5), (5, 10), (9, 5), (5, 9), (5, 0), (0, 5), (6, 1) }
            .Select(p => new ContourEntryCandidate(new ContourChoice(0, 0, new Vector(p.Item1, p.Item2)) { Owner = prepared }, AutomaticEntryKind.ConvexCorner)).ToArray();
    }

    private static ContourEntryFeasibility Adapter(PreparedContours prepared, Program clean, Program[] blockers)
    {
        var own = LeadMaterialSnapshot.Capture(clean, Vector.Zero);
        var others = blockers.Select(p => LeadMaterialSnapshot.Capture(p, Vector.Zero)).ToArray();
        Assert.True(own.IsComplete, own.Reason);
        Assert.All(others, m => Assert.True(m.IsComplete, m.Reason));
        return new(prepared, Vector.Zero, own, others);
    }

    private static int[] ClearSides(IReadOnlyList<ContourEntryCandidate> ranked, ContourEntryFeasibility adapter) =>
        ranked.Where(c => adapter.Check(c.Choice).IsClear).SelectMany(c => Sides(c.Choice.Point, ranked)).Distinct().Order().ToArray();

    private static int[] SelectedSides(ContourSelectionResult selected, IReadOnlyList<ContourEntryCandidate> ranked) =>
        selected.Choices.SelectMany(c => Sides(c.Point, ranked)).Distinct().Order().ToArray();

    private static IEnumerable<int> Sides(Vector point, IReadOnlyList<ContourEntryCandidate> ranked)
    {
        var gaps = new[]
        {
            point.X - ranked.Min(c => c.Choice.Point.X), ranked.Max(c => c.Choice.Point.X) - point.X,
            point.Y - ranked.Min(c => c.Choice.Point.Y), ranked.Max(c => c.Choice.Point.Y) - point.Y,
        };
        return Enumerable.Range(0, 4).Where(side => gaps[side] <= gaps.Min() + 1e-6);
    }

    private static void AssertGlobalOrder(ContourSelectionResult selected, IReadOnlyList<ContourEntryCandidate> ranked)
    {
        var ranks = selected.Choices.Select(c => ranked.ToList().FindIndex(candidate => ReferenceEquals(candidate.Choice, c))).ToArray();
        Assert.Equal(ranks.Order(), ranks);
    }

    private static Program Polygon()
    {
        var points = Enumerable.Range(0, 16).Select(i => new Vector(
            10 * System.Math.Cos(0.13 - i * 2 * System.Math.PI / 16),
            10 * System.Math.Sin(0.13 - i * 2 * System.Math.PI / 16))).ToArray();
        var program = new Program();
        program.MoveTo(points[0].X, points[0].Y);
        foreach (var point in points.Skip(1).Append(points[0]))
            program.LineTo(point.X, point.Y);
        return program;
    }

    private static Program Rectangle(double x, double y, double width, double height)
    {
        var program = new Program();
        program.MoveTo(x, y);
        program.LineTo(x, y + height);
        program.LineTo(x + width, y + height);
        program.LineTo(x + width, y);
        program.LineTo(x, y);
        return program;
    }

    private static CuttingParameters Parameters(double length = 0.3, double angle = 45, double clearance = 0.05) => new()
    {
        ExternalLeadIn = new LineLeadIn { Length = length, ApproachAngle = angle },
        InternalLeadIn = new LineLeadIn { Length = length, ApproachAngle = angle },
        ArcCircleLeadIn = new LineLeadIn { Length = length },
        ExternalLeadOut = new NoLeadOut(),
        InternalLeadOut = new NoLeadOut(),
        TabsEnabled = false,
        RoundLeadInAngles = false,
        PierceClearance = clearance,
    };
}
