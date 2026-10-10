using System.Threading;
using OpenNest.CNC;
using OpenNest.CNC.CuttingPlanning;
using OpenNest.CNC.CuttingStrategy;
using OpenNest.Engine.CuttingPlanning;
using OpenNest.Geometry;

namespace OpenNest.Tests.CuttingPlanning;

/// <summary>
/// S07: the feasibility adapter certifies EXACTLY what LeadPathValidator certifies for one
/// emitted candidate contour — no more (NoLeadIn is not a plan approval) and no less
/// (incomplete checks never read as clear), cached per captured attempt only.
/// </summary>
public class ContourEntryFeasibilityTests
{
    private static readonly Vector At = Vector.Zero;

    private static (PreparedContours Prepared, ContourChoice Choice, LeadMaterialSnapshot Own)
        PreparedSquare(string style = "line")
    {
        var clean = ExplicitContourTests.Square(false);
        var prepared = PreparedContours.Capture(clean, ExplicitContourTests.Parameters(style));
        var choice = prepared.Entry(0, 0, new Vector(0, 5)); // left edge, mid-side
        return (prepared, choice, LeadMaterialSnapshot.Capture(clean, At));
    }

    private static LeadMaterialSnapshot Box(double x1, double y1, double x2, double y2)
    {
        var p = new Program();
        p.MoveTo(x1, y1);
        p.LineTo(x2, y1); p.LineTo(x2, y2); p.LineTo(x1, y2); p.LineTo(x1, y1);
        var snapshot = LeadMaterialSnapshot.Capture(p, Vector.Zero);
        Assert.True(snapshot.IsComplete, snapshot.Reason);
        return snapshot;
    }

    [Fact]
    public void FittingStraightLeadIsClear()
    {
        var (prepared, choice, own) = PreparedSquare();
        var feasibility = new ContourEntryFeasibility(prepared, At, own, []);

        var verdict = feasibility.Check(choice);

        Assert.True(verdict.IsClear, verdict.Reason);
        Assert.Equal(ContourFeasibilityStatus.Clear, verdict.Status);
        Assert.Equal(1, feasibility.EvaluationCount);
    }

    [Fact]
    public void ArcLeadCandidateVerdictsComeFromTheSharedValidator()
    {
        var (prepared, choice, own) = PreparedSquare("arc");
        var feasibility = new ContourEntryFeasibility(prepared, At, own, []);
        Assert.True(feasibility.Check(choice).IsClear);
    }

    [Fact]
    public void NeighbourContactRejectsKeepingReason()
    {
        var (prepared, choice, own) = PreparedSquare();
        // A neighbouring sheet covering the lead-in approach region.
        var neighbour = Box(-1.5, 4.4, -0.05, 5.6);
        var feasibility = new ContourEntryFeasibility(prepared, At, own, [neighbour]);

        var verdict = feasibility.Check(choice);

        Assert.Equal(ContourFeasibilityStatus.Blocked, verdict.Status);
        Assert.False(verdict.IsClear);
        Assert.False(string.IsNullOrEmpty(verdict.Reason));
    }

    [Fact]
    public void OwnMaterialCrossingRejects()
    {
        var clean = ExplicitContourTests.Square(false);
        var prepared = PreparedContours.Capture(clean, ExplicitContourTests.Parameters());
        var choice = prepared.Entry(0, 0, new Vector(0, 5));
        // Own snapshot is where the sheet actually is; the placement drifts 0.4 east, so the
        // emitted external lead (approach x=-0.21..0 relative, entry at x=0.4 absolute)
        // crosses into the sheet's own material.
        var own = LeadMaterialSnapshot.Capture(clean, Vector.Zero);
        var feasibility = new ContourEntryFeasibility(prepared, new Vector(0.4, 0), own, []);

        var verdict = feasibility.Check(choice);

        Assert.Equal(ContourFeasibilityStatus.Blocked, verdict.Status);
        Assert.False(verdict.IsClear);
    }

    [Fact]
    public void IncompleteMaterialNeverReadsAsClear()
    {
        var (prepared, choice, _) = PreparedSquare();
        // Two touching rings: the capture itself refuses, producing an incomplete snapshot.
        var joined = ExplicitContourTests.Square(false);
        var touching = new Program();
        touching.MoveTo(0, 10);
        touching.LineTo(10, 10); touching.LineTo(10, 20); touching.LineTo(0, 20); touching.LineTo(0, 10);
        joined.Codes.AddRange(touching.Codes);
        var incomplete = LeadMaterialSnapshot.Capture(joined, Vector.Zero);
        Assert.False(incomplete.IsComplete);
        Assert.NotNull(incomplete.Reason);

        var ownVerdict = new ContourEntryFeasibility(prepared, At, incomplete, []).Check(choice);
        Assert.Equal(ContourFeasibilityStatus.Incomplete, ownVerdict.Status);
        var otherVerdict = new ContourEntryFeasibility(prepared, At,
            LeadMaterialSnapshot.Capture(ExplicitContourTests.Square(false), At), [incomplete]).Check(choice);
        Assert.Equal(ContourFeasibilityStatus.Incomplete, otherVerdict.Status);
    }

    [Fact]
    public void NoLeadInClearVerdictIsNotAPlanApproval()
    {
        var parameters = ExplicitContourTests.Parameters();
        parameters.ExternalLeadIn = new NoLeadIn();
        parameters.InternalLeadIn = new NoLeadIn();
        parameters.ArcCircleLeadIn = new NoLeadIn();
        var clean = ExplicitContourTests.Square(false);
        var prepared = PreparedContours.Capture(clean, parameters);
        var choice = prepared.Entry(0, 0, new Vector(0, 5));
        var feasibility = new ContourEntryFeasibility(prepared, At, LeadMaterialSnapshot.Capture(clean, At), []);

        var verdict = feasibility.Check(choice);

        // Nothing to certify means the validator passes vacuously — the emitted program
        // carries no lead motion at all, so this verdict certifies no lead and the
        // complete-plan rapid/contour checks still have to run later. The adapter must not
        // silently drop the distinction: consumers can see it is vacuous by counting leads.
        Assert.True(verdict.IsClear);
        var motions = ExecutionMotionReader.Read(prepared.Emit(new[] { choice }), At, null, default).Motions;
        Assert.Empty(motions.Where(m => !m.Rapid
            && (m.Layer == LayerType.Leadin || m.Layer == LayerType.Leadout)));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void ForeignChoiceCannotReuseOrPoisonAnOwnedVerdict(bool foreignFirst, bool unowned)
    {
        var (prepared, choice, own) = PreparedSquare();
        var (_, otherChoice, _) = PreparedSquare();
        var foreign = unowned
            ? new ContourChoice(choice.ContourOrdinal, choice.EntityOrdinal, choice.Point)
            : otherChoice;
        Assert.Throws<ArgumentException>(() => prepared.Emit([foreign]));
        var adapter = new ContourEntryFeasibility(prepared, At, own, []);
        var cold = new ContourEntryFeasibility(prepared, At, own, []).Check(foreign);
        Assert.Equal(ContourFeasibilityStatus.Incomplete, cold.Status);

        if (foreignFirst)
            Assert.Equal(cold, adapter.Check(foreign));
        else
            Assert.True(adapter.Check(choice).IsClear);

        var refused = adapter.Check(foreign);
        Assert.Equal(cold, refused);
        Assert.Contains("Foreign contour choice", refused.Reason);
        Assert.True(adapter.Check(choice).IsClear);
        Assert.True(adapter.Check(choice with { }).IsClear);
        Assert.Equal(1, adapter.EvaluationCount);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => adapter.Check(foreign, token: cancelled.Token));
        Assert.True(adapter.Check(choice).IsClear);
        Assert.Equal(1, adapter.EvaluationCount);
    }

    [Fact]
    public void MalformedEmissionRefusesWithReasonInsteadOfCrashing()
    {
        var (prepared, choice, own) = PreparedSquare();
        var (other, _, _) = PreparedSquare();
        var forgedContour = choice with { Owner = other, ContourOrdinal = 99 };
        var forgedEntity = choice with { Owner = prepared, EntityOrdinal = 99 };
        var feasibility = new ContourEntryFeasibility(prepared, At, own, []);

        var verdict = feasibility.Check(forgedContour);
        Assert.Equal(ContourFeasibilityStatus.Incomplete, verdict.Status);
        Assert.False(string.IsNullOrEmpty(verdict.Reason));
        Assert.Equal(ContourFeasibilityStatus.Incomplete, feasibility.Check(forgedEntity).Status);
    }

    [Fact]
    public void CancellationPropagatesAndPoisonsNothing()
    {
        var (prepared, choice, own) = PreparedSquare();
        var feasibility = new ContourEntryFeasibility(prepared, At, own, []);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        Assert.ThrowsAny<OperationCanceledException>(() => feasibility.Check(choice, token: cancelled.Token));
        Assert.Equal(0, feasibility.EvaluationCount);
        Assert.True(feasibility.Check(choice).IsClear);
    }

    [Fact]
    public void SameChoiceAndNodeContextIsCachedWithinTheAttemptOnly()
    {
        var (prepared, choice, own) = PreparedSquare();
        var feasibility = new ContourEntryFeasibility(prepared, At, own, []);

        Assert.True(feasibility.Check(choice, "A").IsClear);
        Assert.True(feasibility.Check(choice, "A").IsClear);
        Assert.Equal(1, feasibility.EvaluationCount);

        // A different node context is a different key (the cache is deliberately conservative).
        Assert.True(feasibility.Check(choice, "B").IsClear);
        Assert.Equal(2, feasibility.EvaluationCount);

        // Nothing survives into a fresh planning attempt: no static or cross-instance cache.
        var fresh = new ContourEntryFeasibility(prepared, At, own, []);
        Assert.True(fresh.Check(choice, "A").IsClear);
        Assert.Equal(1, fresh.EvaluationCount);
    }

    [Fact]
    public void EvaluationIsLazyPerCandidate()
    {
        var (prepared, choice, own) = PreparedSquare();
        var feasibility = new ContourEntryFeasibility(prepared, At, own, []);
        var other = prepared.Entry(0, 1, new Vector(5, 10));

        // One Check evaluates exactly that candidate — never every point of every contour.
        Assert.True(feasibility.Check(other).IsClear);
        Assert.Equal(1, feasibility.EvaluationCount);
        Assert.NotSame(choice, other);
    }

    [Fact]
    public void VerdictsAreTheValidatorsVerdictsExactly()
    {
        // Adapter verdict == LeadPathValidator outcome for the same emitted program.
        var (prepared, choice, own) = PreparedSquare();
        var materials = new[] { own };
        var execution = ExecutionMotionReader.Read(
            prepared.EmitCandidateForValidation(choice), At, null, default);
        var direct = LeadPathValidator.Check(execution, own, materials, default);

        var verdict = new ContourEntryFeasibility(prepared, At, own, materials).Check(choice);

        Assert.Equal(direct.IsClear, verdict.IsClear);
        Assert.Equal(direct.Reason, verdict.Reason);
    }
}
