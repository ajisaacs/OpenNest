using OpenNest.CNC;
using OpenNest.CNC.CuttingPlanning;
using OpenNest.CNC.CuttingStrategy;
using OpenNest.Diagnostics;
using OpenNest.Engine.CuttingPlanning;
using OpenNest.Geometry;

namespace OpenNest.Tests.CuttingPlanning;

public class HoleLookAheadTests
{
    private static readonly Vector Start = new(-2, 4);

    internal static Program MixedHoles()
    {
        var clean = LeadPathValidationTests.Rectangle(0, 0, 12, 10);
        clean.MoveTo(3, 3);
        clean.LineTo(5, 3); clean.LineTo(5, 5); clean.LineTo(3, 5); clean.LineTo(3, 3);
        clean.MoveTo(9, 4);
        clean.ArcTo(9, 4, 8, 4, RotationType.CCW);
        return clean;
    }

    internal static CuttingParameters Parameters()
    {
        var p = ExplicitContourTests.Parameters();
        p.InternalLeadIn = new LineLeadIn { Length = 0.15, ApproachAngle = 90 };
        p.ArcCircleLeadIn = new LineLeadIn { Length = 0.15, ApproachAngle = 90 };
        p.PierceClearance = 0;
        return p;
    }

    [Fact]
    public void MixedHolesFaceTheNextContourAndRectanglesUseFeasibleCorners()
    {
        var parameters = Parameters();
        var part = new Part(new Drawing("mixed holes", MixedHoles()));
        var next = new Part(new Drawing("next", LeadPathValidationTests.Rectangle(0, 0, 2, 2)), new Vector(20, 2));
        var fingerprint = ExplicitContourTests.Fingerprint(part.Program);
        var snapshot = CuttingPlanService.Capture(new CuttingPlanRequest([part, next], Start,
            confirmedParameters: parameters, preservePartOrder: true));
        var result = CuttingPlanService.Plan(snapshot);
        Assert.True(result.Status == CuttingPlanStatus.Ready, Describe(result));
        Assert.True(result.IndependentlyReplayed);
        var proposal = result.ProposedOrder[0];
        Assert.Equal(new[] { 0, 1, 2 }, proposal.ContourChoices.Select(c => c.ContourOrdinal));
        var rectangle = proposal.ContourChoices[0].Point;
        Assert.True(rectangle.X > 4.9 && (rectangle.Y < 3.1 || rectangle.Y > 4.9),
            $"Expected a right-facing rectangle corner, got {rectangle}");
        Assert.True(proposal.ContourChoices[1].Point.X > 8, "Circle must finish toward the outside start.");
        Assert.True(proposal.ContourChoices[^1].Point.X > 11.9, "Outside start must face the next part.");
        var runs = CutRuns(proposal.Execution);
        Assert.Equal(3, runs.Count);
        for (var i = 0; i < runs.Count - 1; i++)
            Assert.True(runs[i].End.X < runs[i + 1].Pierce.X, "Actual emitted cuts must flow rightward.");
        Assert.Empty(new ReleasedContourState().Check(proposal.Execution, Start, 1));
        Assert.Equal(fingerprint, ExplicitContourTests.Fingerprint(part.Program));
    }

    [Fact]
    public void CrossingPreferredHolePathBacktracksToACheckedAlternative()
    {
        // Calibrated: the preferred rapid from hole 1 to hole 2 touches already-cut hole 3.
        var centres = new[] { new Vector(3, 3), new Vector(7, 15), new Vector(3, 7), new Vector(3, 11) };
        var clean = LeadPathValidationTests.Rectangle(0, 0, 18, 18);
        foreach (var c in centres)
        {
            clean.MoveTo(c.X + 1.75, c.Y);
            clean.ArcTo(c.X + 1.75, c.Y, c.X, c.Y, RotationType.CCW);
        }
        var part = new Part(new Drawing("crossing preference", clean));
        var unchanged = ExplicitContourTests.Fingerprint(part.Program);
        var start = new Vector(-2, 9);
        var snapshot = CuttingPlanService.Capture(new CuttingPlanRequest([part], start,
            confirmedParameters: Parameters(), preservePartOrder: true));
        var source = snapshot.Placements[0];
        var prepared = source.Prepared;
        var adapter = new ContourEntryFeasibility(prepared, source.Location, source.Material, []);
        var outside = ContourEntrySelection.Select(prepared.AutomaticEntryCandidatesWithFallbacks(4)
            .RankTowardNextCut(null, start), c => adapter.Check(c.Choice)).Choices[0];
        var route = CuttingHoleOrder.Plan(new[] { 0, 1, 2, 3 }, centres, start,
            PreferredContourEntries.Pierce(prepared, outside, default));
        var preferred = PreferredContourEntries.TryPlan(prepared, outside, route, centres, start,
            c => adapter.Check(c.Choice));
        Assert.True(preferred.IsPreferred, preferred.Reason);
        var unsafeChoices = preferred.HoleChoices.Append(outside).ToArray();
        var unsafeProgram = prepared.Emit(unsafeChoices);
        var unsafeExecution = ExecutionMotionReader.Read(unsafeProgram, Vector.Zero, start, default);
        Assert.Contains(new ReleasedContourState().Check(unsafeExecution, start, 1),
            f => f.Kind == PostVerificationKind.RapidCrossing);
        var rejected = CuttingPlanService.ReplayPrograms(snapshot,
            [source.Propose(unsafeProgram, unsafeExecution, unsafeChoices)], 0, default);
        Assert.NotEqual(CuttingPlanStatus.Ready, rejected.Status);
        Assert.False(rejected.IndependentlyReplayed);

        var result = CuttingPlanService.Plan(snapshot);
        Assert.True(result.Status == CuttingPlanStatus.Ready, Describe(result));
        Assert.True(result.IndependentlyReplayed);
        var actual = result.ProposedOrder[0];
        Assert.Equal(outside.Point, actual.ContourChoices[^1].Point);
        Assert.Equal(route, actual.ContourChoices.Take(4).Select(c => c.ContourOrdinal));
        Assert.NotEqual(unsafeChoices.Single(c => c.ContourOrdinal == 2).Point,
            actual.ContourChoices.Single(c => c.ContourOrdinal == 2).Point);
        Assert.Empty(new ReleasedContourState().Check(actual.Execution, start, 1));
        Assert.Equal(unchanged, ExplicitContourTests.Fingerprint(part.Program));
    }

    [Fact]
    public void LaterLockedPartForcesNewPerimeterAndRecomputedHolePreference()
    {
        var parameters = Parameters();
        var first = new Part(new Drawing("holes", MixedHoles()));
        var nextClean = LeadPathValidationTests.Rectangle(0, 0, 2, 2);
        // The fixed next program starts above-left, then moves above the first part before
        // cutting on the right. Its material centre alone cannot predict that approach.
        nextClean.MoveTo(-22, 10);
        nextClean.Codes.Add(new LinearMove(-21, 10) { Layer = LayerType.Scribe });
        nextClean.MoveTo(0, 10);
        nextClean.Codes.Add(new LinearMove(1, 10) { Layer = LayerType.Scribe });
        var next = new Part(new Drawing("locked marked part", nextClean), new Vector(20, 2));
        var nextPrepared = PreparedContours.Capture(nextClean, parameters);
        Assert.True(next.RestoreLeadInProgram(nextPrepared.Emit(
            [nextPrepared.ClosestEntry(0, new Vector(3, 1))]), true));
        var lockedFingerprint = ExplicitContourTests.Fingerprint(next.Program);
        var snapshot = CuttingPlanService.Capture(new CuttingPlanRequest([first, next], Start,
            confirmedParameters: parameters, preservePartOrder: true));
        var source = snapshot.Placements[0];
        var prepared = source.Prepared;
        var adapter = new ContourEntryFeasibility(prepared, source.Location, source.Material,
            snapshot.Placements.Select(p => p.Material).ToArray());
        var preferredOutside = ContourEntrySelection.Select(prepared.AutomaticEntryCandidatesWithFallbacks(2, new Vector(21, 3))
            .RankTowardNextCut(new Vector(21, 3), Start), c => adapter.Check(c.Choice)).Choices[0];
        var centres = prepared.HoleCentres().Select(c => c ?? Vector.Zero).ToArray();
        var route = CuttingHoleOrder.Plan(new[] { 0, 1 }, centres, Start,
            PreferredContourEntries.Pierce(prepared, preferredOutside, default));
        var preferred = PreferredContourEntries.TryPlan(prepared, preferredOutside, route, centres, Start,
            c => adapter.Check(c.Choice));
        Assert.True(preferred.IsPreferred, preferred.Reason);
        var firstExecution = ExecutionMotionReader.Read(prepared.Emit(
            preferred.HoleChoices.Append(preferredOutside).ToArray()), Vector.Zero, Start, default);
        var state = new ReleasedContourState();
        Assert.Empty(state.Check(firstExecution, Start, 1));
        Assert.Contains(state.Check(snapshot.Placements[1].Execution, firstExecution.DeparturePoint, 2),
            f => f.Kind == PostVerificationKind.RapidCrossing);

        var result = CuttingPlanService.Plan(snapshot);
        Assert.True(result.Status == CuttingPlanStatus.Ready, Describe(result));
        Assert.True(result.IndependentlyReplayed);
        Assert.NotEqual(preferredOutside.Point, result.ProposedOrder[0].ContourChoices[^1].Point);
        Assert.NotEqual(preferred.HoleChoices.Select(c => c.Point).ToArray(),
            result.ProposedOrder[0].ContourChoices.Take(2).Select(c => c.Point).ToArray());
        Assert.Equal(lockedFingerprint, ExplicitContourTests.Fingerprint(result.ProposedOrder[1].CopyProgram()));
        Assert.Equal(lockedFingerprint, ExplicitContourTests.Fingerprint(next.Program));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HoleProgramsRetainScribesOnceAndNeverInstallOnFailure(bool tabs)
    {
        var clean = MixedHoles();
        clean.MoveTo(-2, -2);
        clean.Codes.Add(new LinearMove(-1, -1) { Layer = LayerType.Scribe });
        var parameters = Parameters();
        parameters.TabsEnabled = tabs;
        parameters.TabConfig = new NormalTab { Size = 0.2 };
        parameters.ExternalLeadOut = new ArcLeadOut { Radius = 0.2 };
        var part = new Part(new Drawing("marked", clean));
        var fingerprint = ExplicitContourTests.Fingerprint(part.Program);
        var result = CuttingPlanService.Plan(new CuttingPlanRequest([part], Start,
            confirmedParameters: parameters, preservePartOrder: true));
        if (tabs)
        {
            Assert.NotEqual(CuttingPlanStatus.Ready, result.Status);
            Assert.Empty(result.ProposedOrder);
            Assert.Contains(result.Findings, f => f.Kind == PostVerificationKind.Incomplete);
        }
        else
        {
            Assert.True(result.Status == CuttingPlanStatus.Ready, Describe(result));
            Assert.True(result.IndependentlyReplayed);
            var actual = result.ProposedOrder[0];
            Assert.Equal(2, actual.ContourChoices[^1].ContourOrdinal);
            Assert.Equal(3, actual.ContourChoices.Select(c => c.ContourOrdinal).Distinct().Count());
            Assert.Single(actual.Execution.Motions.Where(m => !m.Rapid && m.Layer == LayerType.Scribe));
        }
        Assert.Equal(fingerprint, ExplicitContourTests.Fingerprint(part.Program));
    }

    internal static List<(Vector Pierce, Vector End)> CutRuns(OwnedExecution execution)
    {
        var runs = new List<(Vector Pierce, Vector End)>();
        Vector? pierce = null;
        var end = Vector.Zero;
        foreach (var motion in execution.Motions)
        {
            if (motion.Rapid)
            {
                if (pierce is { } p) runs.Add((p, end));
                pierce = null;
            }
            else if (motion.Layer != LayerType.Scribe)
            {
                pierce ??= motion.Start ?? motion.End;
                end = motion.End;
            }
        }
        if (pierce is { } last) runs.Add((last, end));
        return runs;
    }

    private static string Describe(CuttingPlanResult result) =>
        $"{result.Status}: " + string.Join("; ", result.Findings.Select(f => f.Message));
}
