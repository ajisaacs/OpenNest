using OpenNest.CNC;
using OpenNest.CNC.CuttingPlanning;
using OpenNest.CNC.CuttingStrategy;
using OpenNest.Diagnostics;
using OpenNest.Engine.CuttingPlanning;
using OpenNest.Geometry;
using OpenNest.IO;

namespace OpenNest.Tests.CommonLines;

/// <summary>
/// Plan Cutting cuts each common line once (docs/common-line-cutting.md): a group is cut member by
/// member, each cutting its holes, then the shared cuts it owns on the centerline, then its
/// remainder, which ends in the kerf the shared cut opened. The parts are PT42-like.
/// </summary>
public class CommonLinePlanningTests
{
    internal const double Kerf = 0.012;
    internal const double Width = 6;
    internal const double Height = 12.44;
    internal const double Chamfer = 0.38;

    internal static CuttingParameters Parameters(double leadIn = 0.25) => new()
    {
        ExternalLeadIn = new LineLeadIn { Length = leadIn, ApproachAngle = 90 },
        InternalLeadIn = new LineLeadIn { Length = leadIn, ApproachAngle = 90 },
        ExternalLeadOut = new NoLeadOut(),
        InternalLeadOut = new NoLeadOut(),
        PierceClearance = 0.05,
        Kerf = Kerf,
    };

    /// <summary>A PT42-like drawing: Width x Height with chamfered corners, optionally a round hole.</summary>
    internal static Drawing Pt42(bool hole = false)
    {
        var program = new Program();
        program.Codes.Add(new RapidMove(Chamfer, 0));
        program.Codes.Add(new LinearMove(Width - Chamfer, 0));
        program.Codes.Add(new LinearMove(Width, Chamfer));
        program.Codes.Add(new LinearMove(Width, Height - Chamfer));
        program.Codes.Add(new LinearMove(Width - Chamfer, Height));
        program.Codes.Add(new LinearMove(Chamfer, Height));
        program.Codes.Add(new LinearMove(0, Height - Chamfer));
        program.Codes.Add(new LinearMove(0, Chamfer));
        program.Codes.Add(new LinearMove(Chamfer, 0));
        if (hole)
        {
            program.Codes.Add(new RapidMove(Width / 2 + 0.5, Height / 2));
            program.Codes.Add(new ArcMove(new Vector(Width / 2 + 0.5, Height / 2), new Vector(Width / 2, Height / 2),
                RotationType.CCW));
        }
        return new Drawing("PT42", program);
    }

    /// <summary>A plate with <paramref name="count"/> PT42s in a row, one kerf apart and joined.</summary>
    internal static Plate Row(int count, bool hole = false, double x = 2, double y = 2, Drawing drawing = null)
    {
        var plate = new Plate(48, 120) { PartSpacing = 0.25 };
        drawing ??= Pt42(hole);
        var parts = new List<Part>();
        for (var i = 0; i < count; i++)
        {
            var part = new Part(drawing, new Vector(x + i * (Width + Kerf), y));
            plate.Parts.Add(part);
            parts.Add(part);
        }
        plate.JoinCommonLine(parts, Kerf);
        return plate;
    }

    internal static CuttingPlanResult Plan(Plate plate, CuttingParameters parameters = null, bool keepOrder = false) =>
        CuttingPlanService.Plan(CuttingPlanRequest.ForPlate(plate, Vector.Zero, 20000, parameters ?? Parameters(),
            preservePartOrder: keepOrder));

    internal static IReadOnlyList<ExecutionMotion> Motions(FixedProgramPlacement proposal) =>
        ExecutionMotionReader.Read(proposal.CopyProgram(), proposal.Location, null, default).Motions;

    internal static IReadOnlyList<ExecutionMotion> Motions(Part part) =>
        ExecutionMotionReader.Read(part.Program, part.Location, null, default).Motions;

    internal static string Describe(CuttingPlanResult result) =>
        $"{result.Status}: " + string.Join(" | ", result.Findings.Select(f => f.Message));

    // The centerline between the parts at positions i and i + 1 of a row.
    private static double Centerline(int i, double x = 2) => x + (i + 1) * Width + i * Kerf + Kerf / 2;

    private static bool IsCut(ExecutionMotion m) => !m.Rapid && m.Layer is LayerType.Cut or LayerType.Display;

    [Fact]
    public void Pair_CutsTheSharedLineOnceOnTheCenterline()
    {
        var plate = Row(2);

        var result = Plan(plate);

        Assert.True(result.Status == CuttingPlanStatus.Ready, Describe(result));
        Assert.True(result.IndependentlyReplayed);
        var cut = Assert.Single(result.ProposedOrder.SelectMany(Motions), m => m.Layer == LayerType.CommonLine);
        Assert.Equal(Centerline(0), cut.Curve.Start.X, 9);
        Assert.Equal(Centerline(0), cut.End.X, 9);
        Assert.Equal(Height - 2 * Chamfer, cut.Length, 9);
        // The member that cuts it comes first; its partner's remainder ends in its kerf.
        Assert.Same(plate.Parts[0], result.ProposedOrder[0].SourcePart);
    }

    [Fact]
    public void Row_CutsEachMembersHolesThenItsSharedCutsThenItsRemainder()
    {
        var plate = Row(3, hole: true);

        var result = Plan(plate);

        Assert.True(result.Status == CuttingPlanStatus.Ready, Describe(result));
        // The group is cut contiguously, owners before partners.
        Assert.Equal(plate.Parts, result.ProposedOrder.Select(p => p.SourcePart));
        for (var i = 0; i < 3; i++)
        {
            var motions = Motions(result.ProposedOrder[i]);
            var hole = motions.ToList().FindLastIndex(m => IsCut(m) && !m.Curve.IsLine);
            var shared = motions.ToList().FindIndex(m => m.Layer == LayerType.CommonLine);
            var remainder = motions.ToList().FindIndex(m => IsCut(m) && m.Curve.IsLine);
            Assert.True(hole >= 0, $"Part {i + 1} cuts no hole.");
            Assert.True(remainder > hole, $"Part {i + 1} cuts its remainder before its hole.");
            if (i < 2)
                Assert.True(shared > hole && shared < remainder, $"Part {i + 1} does not cut hole, shared cut, remainder in order.");
            else
                Assert.Equal(-1, shared); // The last member owns no shared cut.
        }
        Assert.Equal(2, result.ProposedOrder.SelectMany(Motions).Count(m => m.Layer == LayerType.CommonLine));
    }

    [Fact]
    public void Remainders_StartInTheSharedKerf_AndEndOnTheirSpan()
    {
        var plate = Row(2);
        var result = Plan(plate);
        Assert.True(result.Status == CuttingPlanStatus.Ready, Describe(result));

        foreach (var proposal in result.ProposedOrder)
        {
            var motions = Motions(proposal);
            var lead = motions.Last(m => m.Layer == LayerType.Leadin);
            // Pierced on the centerline (nothing left to pierce), entering the member's own edge.
            Assert.Equal(Centerline(0), lead.Start!.Value.X, 9);
            var edge = ReferenceEquals(proposal.SourcePart, plate.Parts[0]) ? 2 + Width : 2 + Width + Kerf;
            Assert.Equal(edge, lead.End.X, 9);
            // The remainder ends at the other end of its shared span, on the member's edge.
            var last = motions.Last(IsCut);
            Assert.Equal(edge, last.End.X, 9);
        }
    }

    [Fact]
    public void Members_CutTheirPerimeterExactlyOnce_SharedSpansByTheSharedCut()
    {
        var plate = Row(2);
        var part = plate.Parts[0];
        var spans = CommonLineGeometry.Check(plate.CommonLines[0]).Spans;
        var marked = CommonLineCutting.MarkSharedSpans(part.BaseDrawing.Program, part.Location, spans, Kerf);
        var material = LeadMaterialSnapshot.Capture(marked, part.Location, default, sharedSpans: true);
        Assert.True(material.IsComplete, material.Reason);

        var result = Plan(plate);
        Assert.True(result.Status == CuttingPlanStatus.Ready, Describe(result));
        var planned = ExecutionMotionReader.Read(result.ProposedOrder[0].CopyProgram(), part.Location, null, default);
        Assert.True(ContourProgramVerifier.Verify(planned, material, null, default));

        // Cutting the whole perimeter would cut the shared span a second time.
        var whole = ExecutionMotionReader.Read(part.BaseDrawing.Program, part.Location, null, default);
        Assert.False(ContourProgramVerifier.Verify(whole, material, null, default));
    }

    [Fact]
    public void SharedCutLeadBlockedAtBothEnds_RefusesNamingTheSpan()
    {
        var plate = Row(2, x: 2, y: 2);
        // Strips just past both notches stop the axial lead-ins and lead-outs.
        foreach (var y in new[] { 2 - 0.3, 2 + Height + 0.1 })
        {
            var strip = new Program();
            strip.Codes.Add(new RapidMove(0, 0));
            strip.Codes.Add(new LinearMove(0, 0.2));
            strip.Codes.Add(new LinearMove(4, 0.2));
            strip.Codes.Add(new LinearMove(4, 0));
            strip.Codes.Add(new LinearMove(0, 0));
            plate.Parts.Add(new Part(new Drawing("strip", strip), new Vector(Centerline(0) - 2, y)));
        }

        var result = Plan(plate, Parameters(leadIn: 0.5));

        Assert.Equal(CuttingPlanStatus.UnsupportedGeometry, result.Status);
        Assert.Contains(result.Findings, f => f.Message.Contains("No lead fits the common line between parts 1 and 2"));
    }

    [Fact]
    public void KeptOrderCuttingThePartnerFirst_IsAConstraintConflict()
    {
        var plate = Row(2);
        plate.Parts.Reorder([plate.Parts[1], plate.Parts[0]]);

        var result = Plan(plate, keepOrder: true);

        Assert.Equal(CuttingPlanStatus.ConstraintConflict, result.Status);
        Assert.Contains(result.Findings, f => f.Message.Contains("common-line partner that cuts their shared line"));
    }

    [Fact]
    public void Replay_RefusesARemainderBeforeItsSharedCut()
    {
        var plate = Row(2);
        var result = Plan(plate);
        Assert.True(result.Status == CuttingPlanStatus.Ready, Describe(result));

        var swapped = CuttingPlanService.ReplayPrograms(result.Snapshot,
            [result.ProposedOrder[1], result.ProposedOrder[0]], 0, default);

        Assert.Equal(CuttingPlanStatus.InvalidInput, swapped.Status);
        Assert.Contains(swapped.Findings, f => f.Message.Contains("common-line partner"));
    }

    [Fact]
    public void Apply_KeepsUtilizationQuantityAndOverlapResults()
    {
        var nest = new Nest("N26-295");
        var plate = Row(3, hole: true);
        nest.Plates.Add(plate);
        var drawing = plate.Parts[0].BaseDrawing;
        nest.Drawings.Add(drawing);
        var utilization = plate.Utilization();
        var nested = drawing.Quantity.Nested;
        var overlaps = plate.HasOverlappingParts(out _);
        var pairs = PlateOverlapAnalyzer.Analyze(plate.Parts.ToList()).Pairs.Count;
        var area = plate.Parts.Sum(p => p.BaseDrawing.Area);

        var result = Plan(plate);
        Assert.True(result.Status == CuttingPlanStatus.Ready, Describe(result));
        Assert.Equal(CuttingCommitStatus.Applied, CuttingPlanService.Apply([result]).Status);

        Assert.All(plate.Parts.Take(2), p => Assert.True(CommonLineCuts.IsSinglePass(p.Program)));
        Assert.Equal(utilization, plate.Utilization(), 12);
        Assert.Equal(nested, drawing.Quantity.Nested);
        Assert.Equal(overlaps, plate.HasOverlappingParts(out _));
        Assert.Equal(pairs, PlateOverlapAnalyzer.Analyze(plate.Parts.ToList()).Pairs.Count);
        Assert.Equal(area, plate.Parts.Sum(p => p.BaseDrawing.Area), 12);
        Assert.True(CommonLineGeometry.Check(plate.CommonLines[0]).IsIntact);
    }

    [Fact]
    public void Applied_PassesPrePostReview()
    {
        var nest = new Nest("N26-295");
        var plate = Row(3, hole: true);
        plate.CuttingParameters = Parameters();
        nest.Plates.Add(plate);
        var result = Plan(plate);
        Assert.True(result.Status == CuttingPlanStatus.Ready, Describe(result));
        Assert.Equal(CuttingCommitStatus.Applied, CuttingPlanService.Apply([result]).Status);

        var findings = PostVerificationAnalyzer.Analyze(nest).Findings;

        Assert.DoesNotContain(findings, f => f.Kind is PostVerificationKind.CommonLine or PostVerificationKind.Incomplete
            or PostVerificationKind.RapidCrossing or PostVerificationKind.MissingLeadIn or PostVerificationKind.PartSpacing);
    }

    [Fact]
    public void PrePost_ReportsACommonLineLeftUncut()
    {
        var nest = new Nest("N26-295");
        var plate = Row(2);
        nest.Plates.Add(plate);
        // Planned to cut twice, then switched back to once without replanning.
        plate.CutCommonLinesTwice = true;
        var result = Plan(plate);
        Assert.True(result.Status == CuttingPlanStatus.Ready, Describe(result));
        Assert.Equal(CuttingCommitStatus.Applied, CuttingPlanService.Apply([result]).Status);
        plate.CutCommonLinesTwice = false;

        var finding = Assert.Single(PostVerificationAnalyzer.Analyze(nest).Findings,
            f => f.Kind == PostVerificationKind.CommonLine);
        Assert.Contains("not cut once", finding.Message);
    }

    [Fact]
    public void CutTwice_CutsEveryMembersWholePerimeterAndNoSharedCut()
    {
        var plate = Row(2);
        plate.CutCommonLinesTwice = true;

        var result = Plan(plate);

        Assert.True(result.Status == CuttingPlanStatus.Ready, Describe(result));
        var all = result.ProposedOrder.SelectMany(Motions).ToArray();
        Assert.DoesNotContain(all, m => m.Layer == LayerType.CommonLine);
        foreach (var proposal in result.ProposedOrder)
        {
            var cuts = Motions(proposal).Where(IsCut).ToArray();
            Assert.Equal(cuts[0].Curve.Start.X, cuts[^1].End.X, 9);
            Assert.Equal(cuts[0].Curve.Start.Y, cuts[^1].End.Y, 9);
            // No lead-in enters the shared edge, which would cut the partner one kerf away.
            var lead = Motions(proposal).First(m => m.Layer == LayerType.Leadin);
            var sharedEdge = ReferenceEquals(proposal.SourcePart, plate.Parts[0]) ? 2 + Width : 2 + Width + Kerf;
            Assert.False(System.Math.Abs(lead.End.X - sharedEdge) < 1e-9
                && lead.End.Y > 2 + Chamfer && lead.End.Y < 2 + Height - Chamfer, "A lead-in enters the shared edge.");
        }
    }

    [Fact]
    public void LockedMember_Refuses()
    {
        var plate = Row(2);
        plate.Parts[1].LeadInsLocked = true;

        var result = Plan(plate);

        Assert.Equal(CuttingPlanStatus.UnsupportedGeometry, result.Status);
        Assert.Contains(result.Findings, f => f.Message.Contains("Part 2 shares a common line"));
    }

    [Fact]
    public void BrokenGroup_Refuses()
    {
        var plate = Row(2);
        plate.Parts[1].Offset(0.5, 0);

        var result = Plan(plate);

        Assert.Equal(CuttingPlanStatus.UnsupportedGeometry, result.Status);
        Assert.Contains(result.Findings, f => f.Message.Contains("no longer shares"));
    }

    [Fact]
    public void LeadInLongerThanASharedSpan_Refuses()
    {
        var result = Plan(Row(2), Parameters(leadIn: 12));

        Assert.Equal(CuttingPlanStatus.UnsupportedGeometry, result.Status);
        Assert.Contains(result.Findings, f => f.Message.Contains("shorter than the 12 external lead-in"));
    }

    [Fact]
    public void SupportTabs_AreNeverAppliedToMembers()
    {
        var plate = Row(2);
        var single = new Part(plate.Parts[0].BaseDrawing, new Vector(30, 2));
        plate.Parts.Add(single);
        var parameters = Parameters();
        parameters.SupportTabs = true;
        parameters.TabBySize = true;
        parameters.TabSizeMinWidth = 0;
        parameters.TabSizeMaxWidth = 100;
        parameters.TabConfig = new NormalTab { Size = 0.1 };

        var result = Plan(plate, parameters);

        Assert.True(result.Status == CuttingPlanStatus.Ready, Describe(result));
        var perimeter = 2 * (Width + Height) - 8 * Chamfer + 4 * Chamfer * System.Math.Sqrt(2);
        var alone = result.ProposedOrder.Single(p => ReferenceEquals(p.SourcePart, single));
        Assert.True(Motions(alone).Where(IsCut).Sum(m => m.Length) < perimeter - 0.05, "The lone part is not tabbed.");
        foreach (var member in result.ProposedOrder.Where(p => !ReferenceEquals(p.SourcePart, single)))
        {
            var cut = Motions(member).Where(IsCut).Sum(m => m.Length);
            Assert.Equal(perimeter - (Height - 2 * Chamfer), cut, 6);
        }
    }

    [Fact]
    public void SwitchingToCutTwiceAfterPlanning_MakesThePlanStale()
    {
        var plate = Row(2);
        var result = Plan(plate);
        Assert.True(result.Status == CuttingPlanStatus.Ready, Describe(result));

        plate.CutCommonLinesTwice = true;

        Assert.Equal(CuttingCommitStatus.Stale, CuttingPlanService.Apply([result]).Status);
    }

    [Fact]
    public void Applied_RoundTripsThroughTheNestFile()
    {
        var nest = new Nest("N26-295");
        var plate = Row(2);
        nest.Plates.Add(plate);
        nest.Drawings.Add(plate.Parts[0].BaseDrawing);
        var result = Plan(plate);
        Assert.True(result.Status == CuttingPlanStatus.Ready, Describe(result));
        Assert.Equal(CuttingCommitStatus.Applied, CuttingPlanService.Apply([result]).Status);
        var twice = Row(2, drawing: plate.Parts[0].BaseDrawing);
        twice.CutCommonLinesTwice = true;
        nest.Plates.Add(twice);

        var path = Path.Combine(Path.GetTempPath(), $"common-line-{Guid.NewGuid():N}.nest");
        try
        {
            new NestWriter(nest).Write(path);
            var read = new NestReader(path).Read();

            Assert.False(read.Plates[0].CutCommonLinesTwice);
            Assert.True(read.Plates[1].CutCommonLinesTwice);
            var parts = read.Plates[0].Parts;
            Assert.Equal(Motions(plate.Parts[0]).Select(m => (m.Layer, m.End)), Motions(parts[0]).Select(m => (m.Layer, m.End)));
            Assert.True(CommonLineCuts.IsSinglePass(parts[0].Program));
            Assert.True(CommonLineCuts.IsSinglePass(parts[1].Program));
            Assert.DoesNotContain(PostVerificationAnalyzer.Analyze(read).Findings, f => f.PlateNumber == 1
                && f.Kind is PostVerificationKind.CommonLine or PostVerificationKind.RapidCrossing);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
