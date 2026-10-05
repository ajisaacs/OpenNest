using OpenNest.CNC;
using OpenNest.CNC.CuttingPlanning;
using OpenNest.CNC.CuttingStrategy;
using OpenNest.Diagnostics;
using OpenNest.Engine.CuttingPlanning;
using OpenNest.Geometry;

namespace OpenNest.Tests.CuttingPlanning;

public class CuttingReplayContractTests
{
    private static readonly Vector Start = new(-2, 5);

    [Theory]
    [InlineData("partial")]
    [InlineData("retrace")]
    [InlineData("reverse")]
    [InlineData("twice")]
    [InlineData("wrong-point")]
    [InlineData("wrong-index")]
    [InlineData("different-entry")]
    [InlineData("short-lead")]
    public void Replay_RejectsCounterfeitSelectedPayload(string fault)
    {
        var (part, settings) = Fixture();
        var before = ExplicitContourTests.Fingerprint(part.Program);
        var snapshot = CuttingPlanService.Capture(new CuttingPlanRequest([part], Start, confirmedParameters: settings));
        var source = snapshot.Placements[0];
        var choices = new[] { source.Prepared.ClosestEntry(0, Start) };
        var program = source.Prepared.Emit(choices);
        switch (fault)
        {
            case "partial": program = Path(new Vector(0, 10)); break;
            case "retrace": program = Path(new(0, 8), new(0, 7), new(0, 10), new(10, 10), new(10, 0), new(0, 0), new(0, 5)); break;
            case "reverse": program = Path(new(0, 0), new(10, 0), new(10, 10), new(0, 10), new(0, 5)); break;
            case "twice": program = Path(new(0, 10), new(10, 10), new(10, 0), new(0, 0), new(0, 5), new(0, 10), new(10, 10), new(10, 0), new(0, 0), new(0, 5)); break;
            case "wrong-point": choices[0] = choices[0] with { Point = new Vector(999, 999) }; break;
            case "wrong-index": choices[0] = choices[0] with { EntityOrdinal = 999 }; break;
            case "different-entry": choices[0] = source.Prepared.Entry(0, 0, new Vector(0, 6)); break;
            case "short-lead":
                program.Mode = Mode.Absolute;
                var rapid = program.Codes.OfType<RapidMove>().First();
                var lead = program.Codes.OfType<LinearMove>().First(m => m.Layer == LayerType.Leadin);
                rapid.EndPoint = lead.EndPoint + (rapid.EndPoint - lead.EndPoint) * 0.5;
                break;
        }
        var result = CuttingPlanService.ReplayPrograms(snapshot, [source.Propose(program, Read(program), choices)], 0, default);
        Assert.NotEqual(CuttingPlanStatus.Ready, result.Status);
        Assert.Empty(result.ProposedOrder);
        Assert.False(result.IndependentlyReplayed);
        Assert.Equal(before, ExplicitContourTests.Fingerprint(part.Program));
    }

    [Theory]
    [InlineData(true, "partial")]
    [InlineData(false, "partial")]
    [InlineData(true, "reverse")]
    [InlineData(false, "retrace")]
    public void ConfirmedFixedProgram_RejectsIncompleteOrWrongDirectedCoverageWithoutRepair(bool locked, string fault)
    {
        var (part, settings) = Fixture();
        var program = fault switch
        {
            "reverse" => Path(new(0, 0), new(10, 0), new(10, 10), new(0, 10), new(0, 5)),
            "retrace" => Path(new(0, 8), new(0, 7), new(0, 10), new(10, 10), new(10, 0), new(0, 0), new(0, 5)),
            _ => Path(new Vector(0, 10))
        };
        Assert.True(part.RestoreLeadInProgram(program, locked));
        var before = ExplicitContourTests.Fingerprint(part.Program);
        var snapshot = CuttingPlanService.Capture(new CuttingPlanRequest([part], Start, confirmedParameters: settings,
            eligibleParts: locked ? null : []));
        var result = CuttingPlanService.Plan(snapshot);
        Assert.NotEqual(CuttingPlanStatus.Ready, result.Status);
        Assert.NotEqual(CuttingPlanStatus.Ready, CuttingPlanService.ReplayPrograms(snapshot, snapshot.Placements, 0, default).Status);
        Assert.Empty(result.ProposedOrder);
        Assert.Equal(before, ExplicitContourTests.Fingerprint(part.Program));
    }

    [Fact]
    public void Replay_RejectsDifferentActualContourOrderWithUnchangedChoices()
    {
        var settings = ExplicitContourTests.Parameters();
        var part = new Part(new Drawing("holes", PreparedContourTests.Holes()));
        var snapshot = CuttingPlanService.Capture(new CuttingPlanRequest([part], Start, confirmedParameters: settings));
        var source = snapshot.Placements[0];
        var choices = Enumerable.Range(0, 3).Select(i => source.Prepared.ClosestEntry(i, Start)).ToArray();
        var swapped = new[] { choices[1], choices[0], choices[2] };
        var program = source.Prepared.Emit(swapped);
        Assert.NotEqual(CuttingPlanStatus.Ready,
            CuttingPlanService.ReplayPrograms(snapshot, [source.Propose(program, Read(program), choices)], 0, default).Status);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Replay_PreservesFullAndExactConfiguredTabWithSubdivisions(bool tabbed)
    {
        var (part, settings) = Fixture();
        settings.TabsEnabled = tabbed;
        settings.TabConfig = new NormalTab { Size = 0.2 };
        var snapshot = CuttingPlanService.Capture(new CuttingPlanRequest([part], Start, confirmedParameters: settings));
        var ready = CuttingPlanService.Plan(snapshot);
        Assert.True(ready.Status == CuttingPlanStatus.Ready, string.Join("; ", ready.Findings.Select(f => f.Message)));
        var source = snapshot.Placements[0];
        var proposal = ready.ProposedOrder[0];
        Assert.Equal(tabbed ? 39.8 : 40, proposal.Execution.Motions.Where(IsCut).Sum(m => m.Length), 8);
        var subdivided = Subdivide(proposal.CopyProgram());
        var replay = CuttingPlanService.ReplayPrograms(snapshot,
            [source.Propose(subdivided, Read(subdivided), proposal.ContourChoices)], 0, default);
        Assert.Equal(CuttingPlanStatus.Ready, replay.Status);
        Assert.True(replay.IndependentlyReplayed);
        // A different retained gap is not the selected tab, even though it remains open.
        if (tabbed)
        {
            var counterfeit = proposal.CopyProgram();
            counterfeit.Mode = Mode.Absolute;
            var last = counterfeit.Codes.OfType<LinearMove>().Last(m => m.Layer == LayerType.Display);
            last.EndPoint = new Vector(0, 4.9);
            Assert.NotEqual(CuttingPlanStatus.Ready, CuttingPlanService.ReplayPrograms(snapshot,
                [source.Propose(counterfeit, Read(counterfeit), proposal.ContourChoices)], 0, default).Status);
        }
    }

    [Fact]
    public void FixedUnknownTabMetadata_IsRefusedButCompatibilityRouteRemainsUnchanged()
    {
        var (part, settings) = Fixture();
        Assert.True(part.RestoreLeadInProgram(Path(new Vector(0, 10)), true));
        Assert.Equal(CuttingPlanStatus.Ready, CuttingPlanService.Plan(new CuttingPlanRequest([part], Start)).Status);
        settings.TabsEnabled = true;
        settings.TabConfig = new NormalTab { Size = 0.2 };
        var prepared = PreparedContours.Capture(part.BaseDrawing.Program, settings);
        var tabbed = prepared.Emit([prepared.ClosestEntry(0, Start)]);
        Assert.True(part.RestoreLeadInProgram(tabbed, true));
        Assert.NotEqual(CuttingPlanStatus.Ready, CuttingPlanService.Plan(new CuttingPlanRequest([part], Start, confirmedParameters: settings)).Status);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CompleteFixedProgram_RetainsAuthoredEntryAndDimensions(bool locked)
    {
        var (part, settings) = Fixture();
        part.LeadInsLocked = locked;
        ((LineLeadIn)settings.ExternalLeadIn).Length = 0.7;
        var before = ExplicitContourTests.Fingerprint(part.Program);
        var result = CuttingPlanService.Plan(new CuttingPlanRequest([part], Start, confirmedParameters: settings,
            eligibleParts: locked ? null : []));
        Assert.Equal(CuttingPlanStatus.Ready, result.Status);
        Assert.Equal(before, ExplicitContourTests.Fingerprint(result.ProposedOrder[0].CopyProgram()));
    }

    [Theory]
    [InlineData("circles")]
    [InlineData("mixed-circles")]
    [InlineData("arcs")]
    public void NativeCurves_SelectedRoundingClampingAndSubdivisionRemainValid(string shape)
    {
        var settings = ExplicitContourTests.Parameters();
        settings.RoundLeadInAngles = true;
        settings.LeadInAngleIncrement = 90;
        settings.ArcCircleLeadIn = new LineLeadIn { Length = 10 };
        var clean = PreparedContourTests.Holes();
        if (shape == "mixed-circles") clean.Codes.OfType<ArcMove>().Last().Rotation = RotationType.CW;
        if (shape == "arcs")
        {
            clean = ExplicitContourTests.Square(false);
            clean.MoveTo(6, 3);
            clean.ArcTo(4, 3, 5, 3, RotationType.CCW);
            clean.LineTo(6, 3);
        }
        var part = new Part(new Drawing("native", clean));
        var snapshot = CuttingPlanService.Capture(new CuttingPlanRequest([part], Start, confirmedParameters: settings));
        var ready = CuttingPlanService.Plan(snapshot);
        Assert.True(ready.Status == CuttingPlanStatus.Ready, string.Join("; ", ready.Findings.Select(f => f.Message)));
        var source = snapshot.Placements[0];
        var selected = ready.ProposedOrder[0];
        if (shape != "arcs")
        {
            // Search may legitimately select cardinal candidates. Force off-angle
            // choices to exercise rounding, right hole before left for clear departure.
            var choices = new[] { source.Prepared.ClosestEntry(1, Start),
                source.Prepared.ClosestEntry(0, Start), source.Prepared.ClosestEntry(2, Start) };
            var explicitProgram = source.Prepared.Emit(choices);
            selected = source.Propose(explicitProgram, Read(explicitProgram), choices);
            Assert.Equal(CuttingPlanStatus.Ready,
                CuttingPlanService.ReplayPrograms(snapshot, [selected], 0, default).Status);
            var cuts = selected.Execution.Motions.Where(IsCut).Take(2).ToArray();
            Assert.Contains(Enumerable.Range(0, 2), i => cuts[i].Start!.Value.DistanceTo(selected.ContourChoices[i].Point) > 0.1);
            Assert.All(selected.Execution.Motions.Where(m => m.Layer == LayerType.Leadin).Take(2), m => Assert.True(m.Length < 2));
        }
        var program = Subdivide(selected.CopyProgram());
        Assert.Equal(CuttingPlanStatus.Ready, CuttingPlanService.ReplayPrograms(snapshot,
            [source.Propose(program, Read(program), selected.ContourChoices)], 0, default).Status);
    }

    [Fact]
    public void SelectedContract_IsFrozenBeforeReplayAndNeverTrustsCachedExecution()
    {
        var (part, settings) = Fixture();
        var snapshot = CuttingPlanService.Capture(new CuttingPlanRequest([part], Start, confirmedParameters: settings));
        var source = snapshot.Placements[0];
        var choices = new[] { source.Prepared.ClosestEntry(0, Start) };
        var program = source.Prepared.Emit(choices);
        var cachedExecution = Read(program);
        // Supply stale complete execution alongside actual partial code. This must
        // refuse even when proposal storage defensively copies its input program.
        program.Mode = Mode.Absolute;
        program.Codes.RemoveRange(3, program.Codes.Count - 3);
        var proposal = source.Propose(program, cachedExecution, choices);
        ((LineLeadIn)settings.ExternalLeadIn).Length = 900;
        part.BaseDrawing.Program.Codes.Clear();
        var replay = CuttingPlanService.ReplayPrograms(snapshot, [proposal], 0, default);
        Assert.NotEqual(CuttingPlanStatus.Ready, replay.Status);
        Assert.Empty(replay.ProposedOrder);
    }

    [Fact]
    public void FixedReindexedMergedNativeIntervals_PreserveCompleteCoverageAndPayload()
    {
        var settings = ExplicitContourTests.Parameters();
        var clean = ExplicitContourTests.Square(false);
        clean.Codes.Insert(1, new LinearMove(0, 7));
        var part = new Part(new Drawing("subdivided clean", clean));
        var program = new Program();
        program.MoveTo(5, 10.3);
        program.Codes.Add(new LinearMove(5, 10) { Layer = LayerType.Leadin });
        program.LineTo(10, 10); program.LineTo(10, 0); program.LineTo(0, 0);
        program.LineTo(0, 10); program.LineTo(5, 10);
        Assert.True(part.RestoreLeadInProgram(program, true));
        var result = CuttingPlanService.Plan(new CuttingPlanRequest([part], new Vector(5, 12), confirmedParameters: settings));
        Assert.Equal(CuttingPlanStatus.Ready, result.Status);
        Assert.Equal(ExplicitContourTests.Fingerprint(program), ExplicitContourTests.Fingerprint(result.ProposedOrder[0].CopyProgram()));
    }

    private static (Part, CuttingParameters) Fixture()
    {
        var settings = ExplicitContourTests.Parameters();
        var clean = ExplicitContourTests.Square(false);
        var prepared = PreparedContours.Capture(clean, settings);
        var part = new Part(new Drawing("square", clean));
        Assert.True(part.RestoreLeadInProgram(prepared.Emit([prepared.ClosestEntry(0, Start)]), false));
        return (part, settings);
    }

    private static Program Path(params Vector[] points)
    {
        var program = new Program();
        program.MoveTo(-0.3, 5);
        program.Codes.Add(new LinearMove(0, 5) { Layer = LayerType.Leadin });
        foreach (var point in points) program.LineTo(point);
        return program;
    }

    private static OwnedExecution Read(Program program) => ExecutionMotionReader.Read(program, Vector.Zero, null, default);
    private static bool IsCut(ExecutionMotion motion) => !motion.Rapid && motion.Layer is LayerType.Cut or LayerType.Display;

    private static Program Subdivide(Program program)
    {
        // Flatten executed frames through the existing native reader, not tessellation.
        var copy = new Program();
        foreach (var motion in Read(program).Motions)
        {
            if (motion.Rapid) { copy.MoveTo(motion.End); continue; }
            var midpoint = motion.Curve.Midpoint;
            if (motion.Curve.ToEntity() is Arc arc)
            {
                copy.Codes.Add(new ArcMove(midpoint, arc.Center, arc.Rotation) { Layer = motion.Layer });
                copy.Codes.Add(new ArcMove(motion.End, arc.Center, arc.Rotation) { Layer = motion.Layer });
            }
            else if (motion.Curve.ToEntity() is Circle circle)
            {
                var clockwise = PostVerificationGeometry.Curve.Create(motion.Start!.Value, motion.End, circle.Center, true);
                var original = motion.Curve.SameDirection(clockwise) ? RotationType.CW : RotationType.CCW;
                copy.Codes.Add(new ArcMove(midpoint, circle.Center, original) { Layer = motion.Layer });
                copy.Codes.Add(new ArcMove(motion.End, circle.Center, original) { Layer = motion.Layer });
            }
            else
            {
                copy.Codes.Add(new LinearMove(midpoint) { Layer = motion.Layer });
                copy.Codes.Add(new LinearMove(motion.End) { Layer = motion.Layer });
            }
        }
        return copy;
    }

}
