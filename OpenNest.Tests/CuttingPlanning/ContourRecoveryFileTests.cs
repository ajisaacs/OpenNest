using System.Security.Cryptography;
using OpenNest.CNC.CuttingStrategy;
using OpenNest.Engine.CuttingPlanning;
using OpenNest.IO;

namespace OpenNest.Tests.CuttingPlanning;

public class ContourRecoveryFileTests
{
    // Optional private input: never embed customer geometry or names in the repository.
    [SkippableTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void InterlockedPlateFinishesThroughDesktopBatchWithoutChangingInput(bool lineLeads)
    {
        var path = TestConfig.GetExistingPath("InterlockedCuttingNestPath");
        Skip.If(path == null, "Configure InterlockedCuttingNestPath in ignored test-config.json.");
        var bytes = SHA256.HashData(File.ReadAllBytes(path!));
        var nest = new NestReader(path).Read();
        var plates = nest.Plates.Where(p => p.Parts.Count > 0).ToArray();
        Assert.NotEmpty(plates);
        var parts = plates.SelectMany(p => p.Parts).ToArray();
        var programs = parts.Select(p => ExplicitContourTests.Fingerprint(p.Program)).ToArray();
        var poses = parts.Select(p => (p.Location, p.Rotation, p.LeadInsLocked)).ToArray();
        var parameters = Parameters(lineLeads);

        var proposal = CuttingPlanBatch.Capture(plates, parameters, preservePartOrder: false).Plan();

        Assert.True(proposal.CanApply, string.Join(Environment.NewLine, proposal.Describe("in")));
        Assert.False(proposal.RequiresWarningAcceptance);
        Assert.Equal(plates.Length, proposal.Plates.Count);
        foreach (var planned in proposal.Plates)
        {
            Assert.True(planned.IsOverlapClear);
            Assert.True(planned.Result.IndependentlyReplayed);
            Assert.Equal(CuttingPlanStatus.Ready, planned.Result.Status);
            Assert.InRange(planned.Result.Expansions, 1, CuttingPlanBatch.PlateBudget(planned.Plate.Parts.Count));
            Assert.Equal(planned.Plate.Parts.Count, planned.PartCount);
            var snapshot = planned.Result.Snapshot;
            foreach (var part in planned.Result.ProposedOrder)
            {
                Assert.Equal(snapshot.Placements[part.SourceOrdinal].Prepared.Count, part.ContourChoices.Count);
                Assert.Equal(part.ContourChoices.Count, part.ContourChoices.Select(c => c.ContourOrdinal).Distinct().Count());
            }
            var replay = CuttingPlanService.ReplayPrograms(snapshot, planned.Result.ProposedOrder, 0, default);
            Assert.Equal(CuttingPlanStatus.Ready, replay.Status);
            Assert.True(replay.IndependentlyReplayed);
            Assert.Empty(replay.Findings);

            var bounded = CuttingPlanService.Plan(snapshot.WithSearchLimits(1, 16));
            Assert.Equal(CuttingPlanStatus.NoSolutionWithinBudget, bounded.Status);
            Assert.Equal(1, bounded.Expansions);
            Assert.Empty(bounded.ProposedOrder);
            Assert.False(bounded.IndependentlyReplayed);
        }
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var cancelled = CuttingPlanBatch.Capture(plates, parameters, false).Plan(token: cancellation.Token);
        Assert.True(cancelled.IsCancelled);
        Assert.False(cancelled.CanApplyWithWarnings);
        Assert.Equal(parts, plates.SelectMany(p => p.Parts));
        Assert.Equal(programs, parts.Select(p => ExplicitContourTests.Fingerprint(p.Program)));
        Assert.Equal(poses, parts.Select(p => (p.Location, p.Rotation, p.LeadInsLocked)));
        Assert.Equal(bytes, SHA256.HashData(File.ReadAllBytes(path)));
    }

    private static CuttingParameters Parameters(bool lineLeads)
    {
        var parameters = new CuttingParameters();
        if (lineLeads)
        {
            parameters.ExternalLeadIn = new LineLeadIn { Length = 0.15, ApproachAngle = 90 };
            parameters.InternalLeadIn = new LineLeadIn { Length = 0.15, ApproachAngle = 90 };
            parameters.ArcCircleLeadIn = new LineLeadIn { Length = 0.15, ApproachAngle = 90 };
        }
        return parameters;
    }
}
