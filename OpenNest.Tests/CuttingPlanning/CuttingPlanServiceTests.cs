using OpenNest.CNC;
using OpenNest.CNC.CuttingPlanning;
using OpenNest.CNC.CuttingStrategy;
using OpenNest.Diagnostics;
using OpenNest.Engine.CuttingPlanning;
using OpenNest.Geometry;

namespace OpenNest.Tests.CuttingPlanning;

public class CuttingPlanServiceTests
{
    [Fact]
    public void FixedPrograms_OriginalOrderCrossesA_ReorderedWholeProgramsDoNot()
    {
        var parts = Fixture();
        var original = Analyze(parts);
        Assert.Equal(new int?[] { 2, 3 }, original.Findings.Select(f => f.PartNumber));
        Assert.All(original.Findings, crossing =>
        {
            Assert.Equal(PostVerificationKind.RapidCrossing, crossing.Kind);
            Assert.Equal(1, crossing.OtherPartNumber);
        });
        var reordered = Analyze(parts[1], parts[0], parts[2]);
        Assert.Empty(reordered.Findings);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Plan_ReordersWholeFixedPrograms_AndIndependentlyReplaysEveryPlacement(bool locked)
    {
        var parts = Fixture();
        foreach (var part in parts)
            part.LeadInsLocked = locked;
        var unchanged = Unchanged(parts);
        var result = CuttingPlanService.Plan(new CuttingPlanRequest(parts));
        Assert.Equal(CuttingPlanStatus.Ready, result.Status);
        Assert.True(result.IndependentlyReplayed);
        Assert.Equal(new[] { parts[1], parts[0], parts[2] }, result.ProposedOrder.Select(p => p.SourcePart));
        Assert.Equal(new[] { 1, 0, 2 }, result.ProposedOrder.Select(p => p.SourceOrdinal));
        Assert.Equal(parts.Length, result.ProposedOrder.Select(p => p.SourcePart).Distinct().Count());
        Assert.Empty(result.Findings);
        Assert.Empty(Analyze(result.ProposedOrder.Select(p => p.SourcePart).ToArray()).Findings);
        Assert.Equal(new Vector(12.25, 2), result.ProposedOrder[^1].Execution.DeparturePoint);
        unchanged();
    }

    [Fact]
    public void Plan_BacktracksRatherThanReturningUnsafeNearestOrder()
    {
        var parts = Fixture();
        var start = new Vector(8.25, 2); // A is nearest but no continuation after A can reach B safely.
        var result = CuttingPlanService.Plan(new CuttingPlanRequest(parts, start));
        Assert.Equal(CuttingPlanStatus.Ready, result.Status);
        Assert.Equal(new[] { parts[1], parts[0], parts[2] }, result.ProposedOrder.Select(p => p.SourcePart));
        Assert.True(result.Expansions > parts.Length);
        Assert.Empty(AnalyzeFrom(start, result.ProposedOrder.Select(p => p.SourcePart).ToArray()).Findings);
    }

    [Theory]
    [InlineData(Mode.Absolute)]
    [InlineData(Mode.Incremental)]
    public void Plan_LockedFixedInternalCrossingIsConflictWithExactSourceIdentity(Mode mode)
    {
        var part = Rectangle("holes", 100, 50, 20, 20);
        var hole = new Program();
        hole.MoveTo(0.5, 0);
        hole.Codes.Add(new LinearMove(1, 0) { Layer = LayerType.Leadin });
        hole.Codes.Add(new ArcMove(1, 0, 0, 0) { Rotation = RotationType.CCW });
        hole.Mode = mode;
        var placed = new Program();
        placed.SubPrograms[-7] = hole;
        placed.Codes.Add(new SubProgramCall { Program = hole, Offset = new Vector(5, 5), Id = -7 });
        placed.MoveTo(8, 5);
        placed.MoveTo(2, 5);
        placed.Codes.Add(new SubProgramCall { Program = hole, Offset = new Vector(15, 5), Id = -7 });
        placed.Mode = mode;
        Assert.True(part.RestoreLeadInProgram(placed, true));
        var parts = new[] { Fixture()[0], part };
        var unchanged = Unchanged(parts);
        Assert.Contains(Analyze(part).Findings, f => f.Kind == PostVerificationKind.RapidCrossing);
        var result = CuttingPlanService.Plan(new CuttingPlanRequest(parts));
        Assert.Equal(CuttingPlanStatus.ConstraintConflict, result.Status);
        Assert.Empty(result.ProposedOrder);
        Assert.NotEmpty(result.Findings);
        Assert.All(result.Findings, finding =>
        {
            Assert.Equal(PostVerificationKind.RapidCrossing, finding.Kind);
            Assert.Equal(1, finding.SourceOrdinal);
            Assert.Equal(1, finding.OtherSourceOrdinal);
            Assert.Same(part, finding.SourcePart);
            Assert.Same(part, finding.OtherSourcePart);
        });
        unchanged();
    }

    [Theory]
    [InlineData("recursive")]
    [InlineData("missing-call")]
    [InlineData("null-code")]
    [InlineData("nonfinite")]
    [InlineData("bad-arc")]
    [InlineData("empty")]
    [InlineData("motionless")]
    [InlineData("null-codes")]
    public void Plan_MalformedOrEmptyCuttingStateNeverReady(string fault)
    {
        var part = Fixture()[0];
        var program = part.Program;
        switch (fault)
        {
            case "recursive": program.Codes.Add(new SubProgramCall { Program = program }); break;
            case "missing-call": program.Codes.Add(new SubProgramCall()); break;
            case "null-code": program.Codes.Add(null!); break;
            case "nonfinite": program.MoveTo(double.NaN, 0); break;
            case "bad-arc": program.ArcTo(1, 1, 0, 0, RotationType.CW); break;
            case "empty": program.Codes.Clear(); break;
            case "motionless": program.Codes.Clear(); program.MoveTo(1, 1); break;
            case "null-codes": program.Codes = null!; break;
        }
        var beforeCodes = program.Codes?.ToArray();
        var result = CuttingPlanService.Plan(new CuttingPlanRequest([part]));
        Assert.Equal(CuttingPlanStatus.InvalidInput, result.Status);
        Assert.Empty(result.ProposedOrder);
        Assert.Same(program, part.Program);
        Assert.Equal(beforeCodes, program.Codes?.ToArray());
    }

    [Theory]
    [InlineData("suppressed")]
    [InlineData("missing-lead")]
    [InlineData("retention-unknown")]
    [InlineData("cutoff")]
    public void Plan_UnsupportedOrIncompleteStateIsRefused(string fault)
    {
        var part = Fixture()[0];
        var expected = CuttingPlanStatus.UnsupportedGeometry;
        switch (fault)
        {
            case "suppressed": ((Motion)part.Program.Codes[2]).Suppressed = true; break;
            case "missing-lead":
                part.Program.Codes.RemoveAt(1);
                ((RapidMove)part.Program.Codes[0]).EndPoint = new Vector(4, 2);
                expected = CuttingPlanStatus.ConstraintConflict; break;
            case "retention-unknown": ((LinearMove)part.Program.Codes[^2]).EndPoint = new Vector(4, 2.25); break;
            case "cutoff": part.BaseDrawing.IsCutOff = true; break;
        }
        var unchanged = Unchanged([part]);
        var result = CuttingPlanService.Plan(new CuttingPlanRequest([part]));
        Assert.Equal(expected, result.Status);
        Assert.Empty(result.ProposedOrder);
        Assert.NotEmpty(result.Findings);
        unchanged();
    }

    [Fact]
    public void Plan_BudgetExhaustionHasNoFallbackOrMutation()
    {
        var parts = Fixture();
        var unchanged = Unchanged(parts);
        var result = CuttingPlanService.Plan(new CuttingPlanRequest(parts, expansionBudget: 1));
        Assert.Equal(CuttingPlanStatus.NoSolutionWithinBudget, result.Status);
        Assert.Equal(1, result.Expansions);
        Assert.Empty(result.ProposedOrder);
        Assert.False(result.IndependentlyReplayed);
        unchanged();
    }

    [Fact]
    public void Plan_CancelledCaptureAndWorkerHaveNoMutation()
    {
        var parts = Fixture();
        var snapshot = CuttingPlanService.Capture(new CuttingPlanRequest(parts));
        var unchanged = Unchanged(parts);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Equal(CuttingPlanStatus.Cancelled,
            CuttingPlanService.Plan(new CuttingPlanRequest(parts), cancellation.Token).Status);
        Assert.Equal(CuttingPlanStatus.Cancelled, CuttingPlanService.Plan(snapshot, cancellation.Token).Status);
        unchanged();
    }

    [Fact]
    public void Snapshot_OwnsMotionsPosesLocksAndOrder_NotLiveProgramsOrSettings()
    {
        var parts = Fixture();
        var snapshot = CuttingPlanService.Capture(new CuttingPlanRequest(parts));
        var ownedEnds = snapshot.Placements.SelectMany(p => p.Execution.Motions.Select(m => m.End)).ToArray();
        foreach (var part in parts)
        {
            part.Program.Codes.Clear();
            part.BaseDrawing.Program.Codes.Clear();
            part.Location = new Vector(999, 999);
            part.CuttingParameters.TabsEnabled = true;
            part.LeadInsLocked = true;
        }
        Array.Reverse(parts);
        var result = CuttingPlanService.Plan(snapshot);
        Assert.Equal(CuttingPlanStatus.Ready, result.Status);
        Assert.Equal(new[] { 1, 0, 2 }, result.ProposedOrder.Select(p => p.SourceOrdinal));
        Assert.Equal(ownedEnds, snapshot.Placements.SelectMany(p => p.Execution.Motions.Select(m => m.End)));
        Assert.Equal(new Vector(4, 0), snapshot.Placements[0].Location);
        Assert.All(snapshot.Placements, p => Assert.False(p.LeadInsLocked));
        Assert.True(((ICollection<FixedProgramPlacement>)snapshot.Placements).IsReadOnly);
        Assert.True(((ICollection<ExecutionMotion>)snapshot.Placements[0].Execution.Motions).IsReadOnly);
        Assert.True(((ICollection<FixedProgramPlacement>)result.ProposedOrder).IsReadOnly);
    }

    [Fact]
    public void Plan_DeterministicReferenceIdentity_NotDrawingNamesOrNameEquality()
    {
        var parts = Fixture();
        foreach (var part in parts)
            part.BaseDrawing.Name = "same";
        var snapshot = CuttingPlanService.Capture(new CuttingPlanRequest(parts));
        for (var repeat = 0; repeat < 5; repeat++)
            Assert.Equal(new[] { parts[1], parts[0], parts[2] },
                CuttingPlanService.Plan(snapshot).ProposedOrder.Select(p => p.SourcePart));
        Assert.Equal(CuttingPlanStatus.InvalidInput,
            CuttingPlanService.Plan(new CuttingPlanRequest([parts[0], parts[0]])).Status);
        Assert.Equal(CuttingPlanStatus.InvalidInput, CuttingPlanService.Plan(new CuttingPlanRequest([])).Status);
        Assert.Equal(CuttingPlanStatus.InvalidInput,
            CuttingPlanService.Plan(new CuttingPlanRequest(parts, expansionBudget: 0)).Status);
        Assert.Equal(CuttingPlanStatus.InvalidInput,
            CuttingPlanService.Plan(new CuttingPlanRequest(parts, new Vector(double.NaN, 0))).Status);
    }

    [Fact]
    public void ExplicitStart_UsesActualLeadoutDeparture_AndDefaultRemainsZero()
    {
        var part = Fixture()[1];
        var defaultResult = CuttingPlanService.Plan(new CuttingPlanRequest([part]));
        var explicitResult = CuttingPlanService.Plan(new CuttingPlanRequest([part], new Vector(2.25, 2)));
        Assert.Equal(CuttingPlanStatus.Ready, defaultResult.Status);
        Assert.Equal(CuttingPlanStatus.Ready, explicitResult.Status);
        Assert.Equal(Vector.Zero.DistanceTo(new Vector(2.25, 2)), defaultResult.RapidDistance, 10);
        Assert.Equal(0, explicitResult.RapidDistance);
        Assert.Equal(new Vector(2.25, 2), explicitResult.ProposedOrder[0].Execution.DeparturePoint);
        Assert.Empty(Analyze(part).Findings);
        Assert.Empty(AnalyzeFrom(Vector.Zero, part).Findings);
    }

    [Theory]
    [InlineData("unsafe")]
    [InlineData("duplicate")]
    [InlineData("omitted")]
    [InlineData("unknown")]
    public void IndependentReplay_RefusesUnsafeOrIncompleteProposals(string fault)
    {
        var parts = Fixture();
        var snapshot = CuttingPlanService.Capture(new CuttingPlanRequest(parts));
        var unchanged = Unchanged(parts);
        var order = fault switch
        {
            "unsafe" => new[] { 0, 1, 2 },
            "duplicate" => [1, 0, 0],
            "omitted" => [1, 0],
            _ => [1, 0, 3]
        };
        var result = CuttingPlanService.Replay(snapshot, order, 0, default);
        Assert.Equal(fault == "unsafe" ? CuttingPlanStatus.ConstraintConflict : CuttingPlanStatus.InvalidInput,
            result.Status);
        Assert.Empty(result.ProposedOrder);
        Assert.False(result.IndependentlyReplayed);
        if (fault == "unsafe")
        {
            Assert.Equal(new int?[] { 1, 2 }, result.Findings.Select(f => f.SourceOrdinal));
            Assert.All(result.Findings, f => Assert.Same(parts[0], f.OtherSourcePart));
        }
        unchanged();
    }

    [Fact]
    public void EqualRapidDistance_TiesKeepSourceOrdinal()
    {
        var parts = new[] { Rectangle("same", 10, -3, 2, 2), Rectangle("same", 10, 1, 2, 2) };
        var result = CuttingPlanService.Plan(new CuttingPlanRequest(parts));
        Assert.Equal(CuttingPlanStatus.Ready, result.Status);
        Assert.Equal(parts, result.ProposedOrder.Select(p => p.SourcePart));
        Assert.Empty(Analyze(parts).Findings);
    }

    [Theory]
    [InlineData(Mode.Absolute)]
    [InlineData(Mode.Incremental)]
    public void OwnedReader_SharedSignedSubcallsPreserveTrueLeadoutDepartureAndOwnership(Mode mode)
    {
        var sub = new Program();
        sub.MoveTo(1.25, 0);
        sub.Codes.Add(new LinearMove(1, 0) { Layer = LayerType.Leadin });
        sub.Codes.Add(new ArcMove(1, 0, 0, 0) { Rotation = RotationType.CCW });
        sub.Codes.Add(new LinearMove(1.25, 0) { Layer = LayerType.Leadout });
        sub.Mode = mode;
        var program = new Program();
        program.SubPrograms[-7] = sub;
        program.Codes.Add(new SubProgramCall { Id = -7, Program = sub, Offset = new Vector(5, 5) });
        program.Codes.Add(new SubProgramCall { Id = -7, Program = sub, Offset = new Vector(15, 5) });
        var before = sub.ToString();
        var owned = ExecutionMotionReader.Read(program, new Vector(100, 50), Vector.Zero, default);
        Assert.Equal(new Vector(116.25, 55), owned.DeparturePoint);
        Assert.Equal(new Vector(106.25, 55), owned.Motions[3].End);
        Assert.Empty(new ReleasedContourState().Check(owned, Vector.Zero, 1));
        sub.Codes.Clear();
        program.Codes.Clear();
        Assert.Equal(new Vector(116.25, 55), owned.DeparturePoint);
        Assert.Empty(new ReleasedContourState().Check(owned, Vector.Zero, 1));
        Assert.NotEmpty(before);
    }

    [Fact]
    public void Check_CopiedReleasedStateDoesNotLeakBranchObstacles()
    {
        var parts = Fixture();
        var snapshot = CuttingPlanService.Capture(new CuttingPlanRequest(parts));
        var parent = new ReleasedContourState();
        var branch = parent.Copy();
        Assert.Empty(branch.Check(snapshot.Placements[0].Execution, Vector.Zero, 1));
        Assert.NotEmpty(branch.Check(snapshot.Placements[1].Execution, new Vector(8.25, 2), 2));
        Assert.Empty(parent.Check(snapshot.Placements[1].Execution, new Vector(8.25, 2), 2));
    }

    private static Action Unchanged(Part[] parts)
    {
        var order = parts.ToArray();
        var programs = parts.Select(p => p.Program).ToArray();
        var codeRefs = parts.Select(p => p.Program.Codes.ToArray()).ToArray();
        var text = parts.Select(p => p.Program.ToString()).ToArray();
        var locations = parts.Select(p => p.Location).ToArray();
        var rotations = parts.Select(p => p.Rotation).ToArray();
        var bounds = parts.Select(p => p.BoundingBox).ToArray();
        var parameters = parts.Select(p => p.CuttingParameters).ToArray();
        var locks = parts.Select(p => p.LeadInsLocked).ToArray();
        var manual = parts.Select(p => p.HasManualLeadIns).ToArray();
        var quantities = parts.Select(p => p.BaseDrawing.Quantity.Nested).ToArray();
        var subs = parts.SelectMany(p => p.Program.Codes.OfType<SubProgramCall>())
            .Select(call => (call, call.Program, text: call.Program.ToString(), call.Offset, call.Rotation, call.Id)).ToArray();
        return () =>
        {
            Assert.Equal(order, parts);
            for (var index = 0; index < parts.Length; index++)
            {
                var part = parts[index];
                Assert.Same(programs[index], part.Program);
                Assert.Equal(codeRefs[index], part.Program.Codes);
                Assert.Equal(text[index], part.Program.ToString());
                Assert.Equal(locations[index], part.Location);
                Assert.Equal(rotations[index], part.Rotation);
                Assert.Same(bounds[index], part.BoundingBox);
                Assert.Same(parameters[index], part.CuttingParameters);
                Assert.Equal(locks[index], part.LeadInsLocked);
                Assert.Equal(manual[index], part.HasManualLeadIns);
                Assert.Equal(quantities[index], part.BaseDrawing.Quantity.Nested);
            }
            foreach (var sub in subs)
            {
                Assert.Same(sub.Program, sub.call.Program);
                Assert.Equal(sub.text, sub.call.Program.ToString());
                Assert.Equal(sub.Offset, sub.call.Offset);
                Assert.Equal(sub.Rotation, sub.call.Rotation);
                Assert.Equal(sub.Id, sub.call.Id);
            }
        };
    }

    private static Part[] Fixture() =>
    [
        Rectangle("A", 4, 0, 4, 4),
        Rectangle("B", 0, 1, 2, 2),
        Rectangle("C", 10, 1, 2, 2)
    ];

    private static Part Rectangle(string name, double x, double y, double width, double height)
    {
        var clean = new Program();
        clean.MoveTo(width, height / 2);
        Contour(clean, width, height);
        var part = new Part(new Drawing(name, clean), new Vector(x, y));
        var placed = new Program();
        placed.MoveTo(width + 0.25, height / 2);
        placed.Codes.Add(new LinearMove(width, height / 2) { Layer = LayerType.Leadin });
        Contour(placed, width, height);
        placed.Codes.Add(new LinearMove(width + 0.25, height / 2) { Layer = LayerType.Leadout });
        Assert.True(part.RestoreLeadInProgram(placed, false));
        part.CuttingParameters = new CuttingParameters();
        return part;
    }

    private static void Contour(Program program, double width, double height)
    {
        program.LineTo(width, 0);
        program.LineTo(0, 0);
        program.LineTo(0, height);
        program.LineTo(width, height);
        program.LineTo(width, height / 2);
    }

    // Independent replay uses private placements/drawings, never an event-wired plate
    // around the source drawings (which would change their quantity accounting).
    private static PostVerificationReport Analyze(params Part[] parts) => AnalyzeFrom(Vector.Zero, parts);

    private static PostVerificationReport AnalyzeFrom(Vector start, params Part[] parts)
    {
        var nest = new Nest();
        var plate = nest.CreatePlate();
        foreach (var source in parts)
        {
            var copy = new Part(new Drawing("replay", (Program)source.BaseDrawing.Program.Clone()), source.Location);
            Assert.True(copy.RestoreLeadInProgram((Program)source.Program.Clone(), source.LeadInsLocked));
            plate.Parts.Add(copy);
        }
        return PostVerificationAnalyzer.Analyze(nest, start);
    }
}
