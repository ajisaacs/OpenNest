using OpenNest.CNC;
using OpenNest.CNC.CuttingPlanning;
using OpenNest.Engine.CuttingPlanning;
using OpenNest.Geometry;

namespace OpenNest.Tests.CuttingPlanning;

public class CircleNextCutTests
{
    private static Program WithHoles()
    {
        var program = LeadPathValidationTests.Rectangle(0, 0, 16, 12);
        foreach (var centre in new[] { new Vector(3, 3), new Vector(9, 4), new Vector(11, 8) })
        {
            program.MoveTo(centre.X + 1, centre.Y);
            program.ArcTo(centre.X + 1, centre.Y, centre.X, centre.Y, RotationType.CCW);
        }
        return program;
    }

    [Theory]
    [InlineData(13, 4)]
    [InlineData(3, 13)]
    [InlineData(-7, 2)]
    [InlineData(4, -7)]
    public void CirclesPreferTheExactNextCutDirectionWithoutDroppingPolarOrDiagonalOptions(double x, double y)
    {
        var prepared = PreparedContours.Capture(WithHoles(), HoleLookAheadTests.Parameters());
        var target = new Vector(x, y);
        var catalogue = prepared.AutomaticEntryCandidatesWithFallbacks(0, target);
        var ranked = catalogue.RankTowardNextCut(target, new Vector(-4, -3));
        var expected = prepared.ClosestEntry(0, target).Point;

        Assert.True(ranked[0].Choice.Point.DistanceTo(expected) < 1e-8,
            $"Expected next-facing {expected}, got {ranked[0].Choice.Point}");
        Assert.Equal(catalogue.Count, ranked.Count);
        Assert.All(catalogue, candidate => Assert.Contains(candidate, ranked));
        for (var angle = 0; angle < 8; angle++)
        {
            var point = new Vector(3, 3) + new Vector(System.Math.Cos(angle * System.Math.PI / 4),
                System.Math.Sin(angle * System.Math.PI / 4));
            Assert.Contains(ranked, c => c.Choice.Point.DistanceTo(point) < 1e-8);
        }
    }

    [Fact]
    public void BlockedExactPointRetainsAFeasiblePolarAlternative()
    {
        var prepared = PreparedContours.Capture(WithHoles(), HoleLookAheadTests.Parameters());
        var target = new Vector(13, 4);
        var catalogue = prepared.AutomaticEntryCandidatesWithFallbacks(0, target);
        var ranked = catalogue.RankTowardNextCut(target, new Vector(-4, -3));
        var exact = prepared.ClosestEntry(0, target).Point;
        var material = LeadMaterialSnapshot.Capture(WithHoles(), Vector.Zero);
        var feasibility = new ContourEntryFeasibility(prepared, Vector.Zero, material, []);
        var blocked = 0;
        var selection = ContourEntrySelection.Select(ranked, candidate =>
        {
            if (candidate.Choice.Point.DistanceTo(exact) < 1e-8)
            {
                blocked++;
                return new(ContourFeasibilityStatus.Blocked, "Exact direction unavailable in this control.");
            }
            return feasibility.Check(candidate.Choice);
        }, 1);

        Assert.True(blocked > 0);
        var selected = Assert.Single(selection.Choices);
        Assert.True(selected.Point.DistanceTo(new Vector(4, 3)) < 1e-8,
            $"Expected east polar fallback, got {selected.Point}");
        Assert.Equal(ContourFeasibilityStatus.Clear, feasibility.Check(selected).Status);
    }

    [Theory]
    [InlineData(45)]
    [InlineData(90)]
    public void ConfiguredAngleRoundingStillSnapsTheActualCircleCuts(double increment)
    {
        var parameters = HoleLookAheadTests.Parameters();
        parameters.RoundLeadInAngles = true;
        parameters.LeadInAngleIncrement = increment;
        var part = new Part(new Drawing("rounded starts", WithHoles()));
        var next = new Part(new Drawing("next part", LeadPathValidationTests.Rectangle(0, 0, 2, 2)),
            new Vector(22, 1));
        var result = CuttingPlanService.Plan(new CuttingPlanRequest([part, next], new Vector(-2, 3),
            confirmedParameters: parameters, preservePartOrder: true));
        Assert.Equal(CuttingPlanStatus.Ready, result.Status);
        Assert.True(result.IndependentlyReplayed);
        var proposed = result.ProposedOrder[0];
        var runs = HoleLookAheadTests.CutRuns(proposed.Execution);
        var centres = new[] { new Vector(3, 3), new Vector(9, 4), new Vector(11, 8) };
        for (var i = 0; i < 3; i++)
        {
            var radial = runs[i].End - centres[proposed.ContourChoices[i].ContourOrdinal];
            var steps = System.Math.Atan2(radial.Y, radial.X) * 180 / System.Math.PI / increment;
            Assert.True(System.Math.Abs(steps - System.Math.Round(steps)) < 1e-8);
        }
        Assert.True(parameters.RoundLeadInAngles);
        Assert.Equal(increment, parameters.LeadInAngleIncrement);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(23, 17)]
    public void PlannedCircleStartsFaceTheImmediateNextPierceNotTwoFeaturesAhead(double x, double y)
    {
        var location = new Vector(x, y);
        var part = new Part(new Drawing("three round cutouts", WithHoles()), location);
        var next = new Part(new Drawing("next part", LeadPathValidationTests.Rectangle(0, 0, 2, 2)),
            location + new Vector(22, 1));
        var before = ExplicitContourTests.Fingerprint(part.Program);
        var arrival = location + new Vector(-2, 3);
        var snapshot = CuttingPlanService.Capture(new CuttingPlanRequest([part, next], arrival,
            confirmedParameters: HoleLookAheadTests.Parameters(), preservePartOrder: true));
        var result = CuttingPlanService.Plan(snapshot);
        Assert.True(result.Status == CuttingPlanStatus.Ready,
            $"{result.Status}: {string.Join("; ", result.Findings.Select(f => f.Message))}");
        Assert.True(result.IndependentlyReplayed);
        Assert.Equal(new[] { 0, 1 }, result.ProposedOrder.Select(p => p.SourceOrdinal));
        var proposal = result.ProposedOrder[0];
        var choices = proposal.ContourChoices;
        var runs = HoleLookAheadTests.CutRuns(proposal.Execution);
        Assert.Equal(4, runs.Count);
        Assert.Equal(new[] { 0, 1, 2, 3 }, choices.Select(c => c.ContourOrdinal));
        var prepared = snapshot.Placements[0].Prepared;
        for (var i = 0; i < choices.Count - 1; i++)
        {
            var target = runs[i + 1].Pierce - location;
            var expected = prepared.ClosestEntry(choices[i].ContourOrdinal, target).Point;
            Assert.True(choices[i].Point.DistanceTo(expected) < 1e-8,
                $"Hole {i}: expected immediate-next-facing {expected}, got {choices[i].Point}");
            // The emitter rounds circle start angles; verify actual output too, allowing
            // only that existing rounding rather than a snapped compass direction.
            Assert.True((runs[i].End - location).DistanceTo(expected) < 0.01);
        }
        Assert.Empty(new ReleasedContourState().Check(proposal.Execution, arrival, 1));
        Assert.Equal(before, ExplicitContourTests.Fingerprint(part.Program));
    }
}
