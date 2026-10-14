using OpenNest.CNC;
using OpenNest.CNC.CuttingPlanning;
using OpenNest.Engine.CuttingPlanning;
using OpenNest.Geometry;
using OpenNest.Tests.CuttingPlanning;

namespace OpenNest.Tests.CutOffs;

/// <summary>The cutting planner's existing-work banner, its Clear button and staged automatic cut-offs.</summary>
public class PlannerCutOffStagingTests
{
    private static readonly AutomaticCutOffOptions Options = new() { Spacing = 35 };

    [Fact]
    public void Summarize_CountsAppliedLeadInsLockedPartsAndCutOffs_ButNotStagedOnes()
    {
        var nest = new Nest();
        var first = Plate(nest, Clean("a", 1, 1), Clean("b", 12, 1), Clean("c", 23, 1));
        var second = Plate(nest, Clean("d", 1, 1));
        var third = Plate(nest, Clean("e", 1, 1));
        Lead(first.Parts[0], locked: true);
        Lead(first.Parts[2], locked: false);
        var manual = new CutOff(new Vector(60, 0), CutOffAxis.Vertical);
        var staged = new CutOff(new Vector(80, 0), CutOffAxis.Vertical);
        third.CutOffs.Add(manual);
        third.CutOffs.Add(staged);
        third.RegenerateCutOffs(new CutOffSettings());

        var summary = AppliedCuttingWork.Summarize([first, second, third], [4, 5, 6], [staged]);

        Assert.Equal(2, summary.LeadInParts);
        Assert.Equal(1, summary.LockedLeadInParts);
        Assert.Equal(1, summary.CutOffs); // Cut-off parts are not lead-in parts; the staged line is ignored.
        Assert.Equal([4, 6], summary.PlateNumbers);
        Assert.True(summary.HasExistingWork);
        Assert.Equal("ALREADY APPLIED: lead-ins on 2 parts (1 locked) and 1 cut-off on plates 4, 6."
            + " Planning recalculates unlocked lead-ins. Planning keeps locked lead-ins and existing cut-offs unchanged."
            + " Clear them to recalculate everything from scratch.", summary.Describe());

        var clean = AppliedCuttingWork.Summarize([second], null, null);
        Assert.False(clean.HasExistingWork);
        Assert.Equal(string.Empty, clean.Describe());
        Assert.False(AppliedCuttingWork.Summarize([third], null, third.CutOffs).HasExistingWork);
    }

    [Fact]
    public void Clear_RemovesLeadInsAndCutOffs_KeepsPosesOrderAndQuantities()
    {
        var nest = new Nest();
        var plate = Plate(nest, Clean("a", 1, 1), Clean("b", 12, 1), Clean("c", 40, 1));
        var parameters = ExplicitContourTests.Parameters();
        Assert.Equal(CuttingCommitStatus.Applied,
            CuttingPlanBatch.Capture([plate], parameters, preservePartOrder: true).Plan().Apply().Status);
        plate.Parts[1].LeadInsLocked = true;
        plate.CutOffs.Add(new CutOff(new Vector(30, 0), CutOffAxis.Vertical));
        plate.RegenerateCutOffs(new CutOffSettings());
        var real = plate.Parts.Where(p => !p.BaseDrawing.IsCutOff).ToArray();
        var poses = real.Select(p => (p.Location, p.Rotation)).ToArray();
        var quantities = real.Select(p => p.BaseDrawing.Quantity.Nested).ToArray();
        Assert.All(real, p => Assert.True(p.HasManualLeadIns));
        Assert.NotNull(plate.CuttingParameters);

        var cleared = AppliedCuttingWork.Clear([plate], [3]);

        Assert.Equal((3, 1, 1), (cleared.LeadInParts, cleared.LockedLeadInParts, cleared.CutOffs));
        Assert.Equal([3], cleared.PlateNumbers);
        Assert.Equal(real, plate.Parts.ToArray());
        Assert.Empty(plate.CutOffs);
        Assert.Null(plate.CuttingParameters);
        Assert.All(real, p => Assert.False(p.HasManualLeadIns || p.LeadInsLocked));
        Assert.All(real, p => Assert.True(ProgramContent.Equal(p.BaseDrawing.Program, p.Program)));
        Assert.Equal(poses, real.Select(p => (p.Location, p.Rotation)));
        Assert.Equal(quantities, real.Select(p => p.BaseDrawing.Quantity.Nested));
        Assert.False(AppliedCuttingWork.Summarize([plate]).HasExistingWork);
    }

    [Fact]
    public void Stage_Revert_RestoresTheEarlierCutOffsAndSequenceExactly()
    {
        var nest = new Nest();
        var plate = Plate(nest, Clean("a", 1, 1), Clean("b", 40, 1));
        var settings = new CutOffSettings();
        var manual = new CutOff(new Vector(75, 0), CutOffAxis.Vertical);
        plate.CutOffs.Add(manual);
        plate.RegenerateCutOffs(settings);
        var manualPart = plate.Parts[^1];
        plate.Parts.Remove(manualPart);
        plate.Parts.Insert(0, manualPart);
        var before = plate.Parts.ToArray();
        var manualProgram = manual.Drawing.Program;

        var staging = AutomaticCutOffBatch.Stage([plate], Options, settings);

        Assert.False(staging.IsBlocked);
        Assert.True(staging.CanRevert);
        Assert.NotEmpty(staging.Definitions);
        Assert.Equal(1 + staging.Definitions.Count, plate.CutOffs.Count);
        Assert.Equal(plate.CutOffs.Count, plate.Parts.Count(p => p.BaseDrawing.IsCutOff));

        staging.Revert();

        Assert.False(staging.CanRevert);
        Assert.Same(manual, Assert.Single(plate.CutOffs));
        Assert.Same(manualProgram, manual.Drawing.Program);
        Assert.Equal(before, plate.Parts.ToArray());
        staging.Revert(); // Runs once.
        Assert.Equal(before, plate.Parts.ToArray());
    }

    [Fact]
    public void Stage_Keep_LeavesTheCutOffsInPlace_AndABlockedBatchChangesNothing()
    {
        var nest = new Nest();
        var plate = Plate(nest, Clean("a", 1, 1), Clean("b", 40, 1));
        var settings = new CutOffSettings();
        var staging = AutomaticCutOffBatch.Stage([plate], Options, settings);
        var staged = plate.CutOffs.ToArray();

        staging.Keep();
        staging.Revert();

        Assert.False(staging.CanRevert);
        Assert.Equal(staged, plate.CutOffs.ToArray());
        Assert.NotEmpty(staged);

        var other = Plate(nest, Clean("c", 1, 1), Clean("d", 40, 1));
        var limited = new CutOff(new Vector(35, 0), CutOffAxis.Vertical) { EndLimit = 2 };
        other.CutOffs.Add(limited);
        var parts = other.Parts.ToArray();
        var blocked = AutomaticCutOffBatch.Stage([other], Options, settings);

        Assert.True(blocked.IsBlocked);
        Assert.False(blocked.CanRevert);
        Assert.Empty(blocked.Definitions);
        Assert.Same(limited, Assert.Single(other.CutOffs));
        Assert.Equal(parts, other.Parts.ToArray());
    }

    [Fact]
    public void StagedCutOffs_AreSequencedByThePlannerBeforeThePartsTheyCross_AndSurviveApply()
    {
        var nest = new Nest();
        var plate = Plate(nest, Clean("a", 1, 1), Clean("b", 30, 1), Clean("c", 30, 40), Clean("d", 60, 20));
        var settings = new CutOffSettings();
        var staging = AutomaticCutOffBatch.Stage([plate], Options, settings);
        Assert.NotEmpty(staging.Definitions);

        // The planner judges crossings on the bounds it captured, before lead-ins widen them.
        var captured = plate.Parts.ToDictionary(p => p, p => p.BoundingBox, ReferenceEqualityComparer.Instance);
        var proposal = CuttingPlanBatch.Capture([plate], ExplicitContourTests.Parameters(), preservePartOrder: false).Plan();
        Assert.True(proposal.CanApply);
        Assert.Equal(CuttingCommitStatus.Applied, proposal.Apply().Status);
        staging.Keep();

        Assert.Equal(staging.Definitions, plate.CutOffs.ToArray());
        var order = plate.Parts.ToList();
        var bounds = plate.BoundingBox(includeParts: false);
        var crossings = 0;
        foreach (var definition in plate.CutOffs)
        {
            var cutIndex = order.FindIndex(p => ReferenceEquals(p.BaseDrawing, definition.Drawing));
            Assert.True(cutIndex >= 0);
            foreach (var part in order.Where(p => !p.BaseDrawing.IsCutOff))
            {
                Assert.True(part.HasManualLeadIns);
                if (!CuttingDependencyGraph.CutOffCrosses(definition.Axis, definition.Position,
                        definition.StartLimit, definition.EndLimit, captured[part], bounds))
                    continue;
                crossings++;
                Assert.True(cutIndex < order.IndexOf(part)); // Planner rule: a crossing cut-off goes first.
            }
        }
        Assert.True(crossings > 0); // The fixture must exercise cut-off precedence.
    }

    private static void Lead(Part part, bool locked) =>
        Assert.True(part.RestoreLeadInProgram((Program)part.Program.Clone(), locked));

    private static Plate Plate(Nest nest, params Part[] parts)
    {
        var plate = nest.CreatePlate();
        plate.Size = new Size(100, 100);
        foreach (var part in parts)
            plate.Parts.Add(part);
        return plate;
    }

    private static Part Clean(string name, double x, double y) =>
        new(new Drawing(name, ExplicitContourTests.Square(false)), new Vector(x, y));
}
