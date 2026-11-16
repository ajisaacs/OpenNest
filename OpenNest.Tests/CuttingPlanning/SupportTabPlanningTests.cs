using OpenNest.CNC;
using OpenNest.CNC.CuttingPlanning;
using OpenNest.CNC.CuttingStrategy;
using OpenNest.Diagnostics;
using OpenNest.Engine.CuttingPlanning;
using OpenNest.Geometry;
using OpenNest.Support;

namespace OpenNest.Tests.CuttingPlanning;

/// <summary>
/// Plan Cutting with "Tab parts the support grid cannot hold level": a part the grid cannot
/// hold is tabbed where the tab steadies it; a supported part is left untabbed.
/// </summary>
public class SupportTabPlanningTests
{
    private const double TabSize = 0.2;

    private static CuttingParameters Parameters()
    {
        var parameters = ExplicitContourTests.Parameters();
        parameters.SupportTabs = true;
        parameters.TabConfig = new NormalTab { Size = TabSize };
        return parameters;
    }

    // A 10 x 1 strip at the origin; the start is beside its lower-left corner.
    private static (Plate Plate, Part Part) Strip(SupportBed bed)
    {
        var plate = new Plate(20, 20) { SupportBed = bed };
        var part = new Part(new Drawing("strip", LeadPathValidationTests.Rectangle(0, 0, 10, 1)), new Vector(2, 2));
        plate.Parts.Add(part);
        return (plate, part);
    }

    private static (Vector Entry, Vector CutEnd) EntryAndCutEnd(FixedProgramPlacement proposal)
    {
        var motions = ExecutionMotionReader.Read(proposal.CopyProgram(), proposal.Location, null, default).Motions;
        var entry = motions.Last(m => m.Layer == LayerType.Leadin).End;
        var cutEnd = motions.Last(m => !m.Rapid && m.Layer is LayerType.Cut or LayerType.Display).End;
        return (entry, cutEnd);
    }

    [Fact]
    public void StripOnOneRowOfContacts_IsTabbedOnTheEdgeAcrossItsCenterFromTheRow()
    {
        // One row of grid points at y = 2.25 runs under the strip (y 2..3), below its center.
        var bed = new SupportGrid(2, 4, 0, 2.25, stabilityMargin: 0.1);
        var (plate, part) = Strip(bed);

        var result = CuttingPlanService.Plan(CuttingPlanRequest.ForPlate(plate, new Vector(0, 0),
            confirmedParameters: Parameters()));

        Assert.True(result.Status == CuttingPlanStatus.Ready, string.Join("\n", result.Findings.Select(f => f.Message)));
        var (entry, cutEnd) = EntryAndCutEnd(Assert.Single(result.ProposedOrder));
        Assert.Equal(3, entry.Y, 6);
        Assert.Equal(TabSize, entry.DistanceTo(cutEnd), 6);
        var support = SupportAnalyzer.Analyze(bed, plate.Quadrant, part);
        Assert.False(support.IsSupported);
        Assert.True(support.MarginWith(entry) >= bed.StabilityMargin);
    }

    // A 10 x 1 strip with a 2 x 0.2 bump on its top edge: inside corners at (4, 1) and (6, 1).
    private static Program BumpedStrip()
    {
        var program = new Program();
        program.MoveTo(0, 0);
        program.LineTo(0, 1); program.LineTo(4, 1); program.LineTo(4, 1.2); program.LineTo(6, 1.2);
        program.LineTo(6, 1); program.LineTo(10, 1); program.LineTo(10, 0); program.LineTo(0, 0);
        return program;
    }

    [Fact]
    public void BumpedStrip_IsNotTabbedInAnInsideCorner()
    {
        // The bump's base corners tie with the top edge as tab points; the tab keeps clear.
        var bed = new SupportGrid(2, 4, 0, 2.25, stabilityMargin: 0.1);
        var plate = new Plate(20, 20) { SupportBed = bed };
        plate.Parts.Add(new Part(new Drawing("bumped", BumpedStrip()), new Vector(2, 2)));

        var result = CuttingPlanService.Plan(CuttingPlanRequest.ForPlate(plate, new Vector(0, 0),
            confirmedParameters: Parameters()));

        Assert.True(result.Status == CuttingPlanStatus.Ready, string.Join("\n", result.Findings.Select(f => f.Message)));
        var (entry, cutEnd) = EntryAndCutEnd(Assert.Single(result.ProposedOrder));
        Assert.Equal(TabSize, entry.DistanceTo(cutEnd), 6);
        foreach (var corner in new[] { new Vector(6, 3), new Vector(8, 3) })
        {
            Assert.True(entry.DistanceTo(corner) >= 2 * TabSize, $"entry {entry}");
            Assert.True(cutEnd.DistanceTo(corner) >= TabSize, $"cut end {cutEnd}");
        }
    }

    [Fact]
    public void BlockedSteadyingSide_TabsTheSteadiestEntryThatFits()
    {
        // Only the middle of the top edge steadies the strip by 0.2. Parts 0.1 above it and beside
        // its upper half leave no room for a lead-in there or at its top corners.
        var bed = new SupportGrid(2, 4, 0, 2.25, stabilityMargin: 0.2);
        var (plate, strip) = Strip(bed);
        plate.Parts.Add(new Part(new Drawing("above", LeadPathValidationTests.Rectangle(0, 0, 14, 2)), new Vector(0, 3.1)));
        plate.Parts.Add(new Part(new Drawing("left", LeadPathValidationTests.Rectangle(0, 0, 1.9, 0.5)), new Vector(0, 2.5)));
        plate.Parts.Add(new Part(new Drawing("right", LeadPathValidationTests.Rectangle(0, 0, 1.9, 0.5)), new Vector(12.1, 2.5)));

        var result = CuttingPlanService.Plan(CuttingPlanRequest.ForPlate(plate, new Vector(7, 0),
            confirmedParameters: Parameters()));

        // Tabbed, the strip counts as steady even where its tab cannot hold it level.
        Assert.True(result.Status == CuttingPlanStatus.Ready, string.Join("\n", result.Findings.Select(f => f.Message)));
        var proposal = result.ProposedOrder.Single(p => ReferenceEquals(p.SourcePart, strip));
        var (entry, cutEnd) = EntryAndCutEnd(proposal);
        Assert.Equal(TabSize, entry.DistanceTo(cutEnd), 6);
        // The steadiest entry that fits: halfway up an end, not the bottom corner nearer the start.
        var support = SupportAnalyzer.Analyze(bed, plate.Quadrant, strip);
        Assert.True(support.MarginWith(entry) > support.MarginWith(new Vector(7, 2)), $"entry {entry}");
        Assert.True(entry.Y > 2.5 - 1e-9, $"entry {entry}");
        Assert.True(support.MarginWith(entry) > support.MarginWith(new Vector(12, 2)), $"entry {entry}");
        Assert.False(support.HoldsLevelWithTabAt(entry));
        Assert.DoesNotContain(result.Findings, f => f.Kind == PostVerificationKind.PartSupport);
    }

    [Fact]
    public void PartNoSingleTabCanHold_IsTabbedWithoutAWarning()
    {
        // The grid's rows run either side of the strip: nothing under it, so one tab can only balance it.
        var bed = new SupportGrid(1, 1, 0, 0.9, stabilityMargin: 0.1);
        var plate = new Plate(20, 20) { SupportBed = bed };
        var strip = new Part(new Drawing("strip", LeadPathValidationTests.Rectangle(0, 0, 10, 0.8)), new Vector(2, 2));
        plate.Parts.Add(strip);

        var result = CuttingPlanService.Plan(CuttingPlanRequest.ForPlate(plate, new Vector(0, 0),
            confirmedParameters: Parameters()));

        Assert.True(result.Status == CuttingPlanStatus.Ready, string.Join("\n", result.Findings.Select(f => f.Message)));
        var (entry, cutEnd) = EntryAndCutEnd(Assert.Single(result.ProposedOrder));
        Assert.Equal(TabSize, entry.DistanceTo(cutEnd), 6);
        Assert.DoesNotContain(result.Findings, f => f.Kind == PostVerificationKind.PartSupport);
    }

    [Fact]
    public void StripWithALeadOut_IsTabbedAndPlanned()
    {
        // The lead-out leaves the tabbed perimeter from its trimmed end, away from the tab: the
        // open-cut proof shows it cuts no gap, as pre-post review does.
        var bed = new SupportGrid(2, 4, 0, 2.25, stabilityMargin: 0.1);
        var (plate, _) = Strip(bed);
        var parameters = Parameters();
        parameters.ExternalLeadOut = new LineLeadOut { Length = 0.1, ApproachAngle = 60 };

        var result = CuttingPlanService.Plan(CuttingPlanRequest.ForPlate(plate, new Vector(0, 0),
            confirmedParameters: parameters));

        Assert.True(result.Status == CuttingPlanStatus.Ready, string.Join("\n", result.Findings.Select(f => f.Message)));
        var proposal = Assert.Single(result.ProposedOrder);
        var (entry, cutEnd) = EntryAndCutEnd(proposal);
        Assert.Equal(TabSize, entry.DistanceTo(cutEnd), 6);
        var motions = ExecutionMotionReader.Read(proposal.CopyProgram(), proposal.Location, null, default).Motions;
        Assert.Equal(LayerType.Leadout, motions[^1].Layer);
    }

    [Fact]
    public void StripWhoseLeadOutMayCutItsTab_KeepsTheLeadOutAndWarns()
    {
        // Wound counterclockwise, every entry's 60 degree lead-out runs 0.05 along the edge, past
        // the 0.03 tab, into the lead-in. The settings' lead-out is kept as configured and the
        // plan is unverified, with a warning on the part.
        var bed = new SupportGrid(2, 4, 0, 2.25, stabilityMargin: 0.1);
        var plate = new Plate(20, 20) { SupportBed = bed };
        var clean = new Program(Mode.Absolute);
        clean.MoveTo(0, 0); clean.LineTo(10, 0); clean.LineTo(10, 1); clean.LineTo(0, 1); clean.LineTo(0, 0);
        plate.Parts.Add(new Part(new Drawing("strip", clean), new Vector(2, 2)));
        var parameters = Parameters();
        parameters.Kerf = 0.014;
        parameters.TabConfig = new NormalTab { Size = 0.03 };
        parameters.ExternalLeadIn = new LineLeadIn { Length = 0.15, ApproachAngle = 60 };
        parameters.ExternalLeadOut = new LineLeadOut { Length = 0.1, ApproachAngle = 60 };

        var result = CuttingPlanService.Plan(CuttingPlanRequest.ForPlate(plate, new Vector(0, 0),
            confirmedParameters: parameters));

        Assert.Equal(CuttingPlanStatus.BestEffort, result.Status);
        Assert.True(result.IndependentlyReplayed);
        var warning = Assert.Single(result.Findings);
        Assert.Equal(PostVerificationKind.TabLeadOut, warning.Kind);
        var proposal = Assert.Single(result.ProposedOrder);
        var (entry, cutEnd) = EntryAndCutEnd(proposal);
        Assert.Equal(0.03, entry.DistanceTo(cutEnd), 6);
        var motions = ExecutionMotionReader.Read(proposal.CopyProgram(), proposal.Location, null, default).Motions;
        var leadOut = motions[^1];
        Assert.Equal(LayerType.Leadout, leadOut.Layer);
        Assert.Equal(0.1, leadOut.Start!.Value.DistanceTo(leadOut.End), 6);
    }

    // A 10 x 0.8 strip with a 9 x 0.4 slot near its bottom edge: no rapid from its top edge
    // reaches the plate below it without crossing the slot once that is cut.
    private static Program SlottedStrip()
    {
        var program = new Program();
        program.Merge(LeadPathValidationTests.Rectangle(0.5, 0.1, 9.5, 0.5));
        program.Merge(LeadPathValidationTests.Rectangle(0, 0, 10, 0.8));
        return program;
    }

    [Fact]
    public void PartNoSingleTabCanHold_LeavesFromAnotherEntryWhenItsSteadiestIsBoxedIn()
    {
        // No grid row runs under the slotted strip, so no tab can hold it; its steadiest tab
        // point is on its top edge, across the slot from the part cut after it.
        var bed = new SupportGrid(1, 1, 0, 0.9, stabilityMargin: 0.1);
        var plate = new Plate(20, 20) { SupportBed = bed };
        var slotted = new Part(new Drawing("slotted", SlottedStrip()), new Vector(2, 2));
        plate.Parts.Add(slotted);
        plate.Parts.Add(new Part(new Drawing("below", LeadPathValidationTests.Rectangle(0, 0, 10, 0.8)), new Vector(2, 0.6)));
        var support = SupportAnalyzer.Analyze(bed, plate.Quadrant, slotted);
        Assert.False(support.TabCanSupport);
        Assert.True(support.BestTabPoint!.Value.Y > 2.4, $"best tab {support.BestTabPoint}");

        var result = CuttingPlanService.Plan(CuttingPlanRequest.ForPlate(plate, new Vector(0, 0),
            confirmedParameters: Parameters(), preservePartOrder: true));

        Assert.True(result.Status == CuttingPlanStatus.Ready, string.Join("\n", result.Findings.Select(f => f.Message)));
        var (entry, cutEnd) = EntryAndCutEnd(result.ProposedOrder.Single(p => ReferenceEquals(p.SourcePart, slotted)));
        Assert.Equal(TabSize, entry.DistanceTo(cutEnd), 6);
        Assert.True(entry.Y < 2.4, $"entry {entry}");
        Assert.DoesNotContain(result.Findings, f => f.Kind == PostVerificationKind.PartSupport);
    }

    // A 0.8 x 10 upright strip at x 4..4.8 between squares cut before and after it on either side.
    // The kept order goes left, strip, right, then back left: that last rapid must cross the strip.
    private static (Plate Plate, Part Strip) StripBetweenSquares(SupportBed bed)
    {
        var plate = new Plate(20, 20) { SupportBed = bed };
        var strip = new Part(new Drawing("strip", LeadPathValidationTests.Rectangle(0, 0, 0.8, 10)), new Vector(4, 1));
        var square = new Drawing("square", LeadPathValidationTests.Rectangle(0, 0, 1, 1));
        plate.Parts.Add(new Part(square, new Vector(1, 2)));
        plate.Parts.Add(strip);
        plate.Parts.Add(new Part(square, new Vector(7, 4)));
        plate.Parts.Add(new Part(square, new Vector(1, 8)));
        return (plate, strip);
    }

    [Theory]
    // No grid column runs under the strip: no single tab can hold it level, yet tabbed it counts as steady.
    [InlineData(0.9, false)]
    // A column runs under it, off its center line: its tab holds it level.
    [InlineData(0.25, true)]
    public void TabbedPart_StaysCrossableOnceCut_WhetherOrNotItsTabHoldsItLevel(double columnOffset, bool holdsLevel)
    {
        var bed = new SupportGrid(1, 1, columnOffset, 0, stabilityMargin: 0.1);
        var (plate, strip) = StripBetweenSquares(bed);
        var support = SupportAnalyzer.Analyze(bed, plate.Quadrant, strip);
        Assert.False(support.IsSupported);
        Assert.Equal(holdsLevel, support.TabCanSupport);

        var kept = CuttingPlanService.Plan(CuttingPlanRequest.ForPlate(plate, new Vector(0, 0),
            confirmedParameters: Parameters(), preservePartOrder: true));
        var free = CuttingPlanService.Plan(CuttingPlanRequest.ForPlate(plate, new Vector(0, 0),
            confirmedParameters: Parameters()));

        Assert.True(kept.Status == CuttingPlanStatus.Ready, string.Join("\n", kept.Findings.Select(f => f.Message)));
        Assert.True(free.Status == CuttingPlanStatus.Ready, string.Join("\n", free.Findings.Select(f => f.Message)));
        Assert.True(free.IndependentlyReplayed);
    }

    [Fact]
    public void FallbackCatalog_KeepsClearOfInsideCornersBeforeOfferingEveryEntry()
    {
        var bed = new SupportGrid(2, 4, 0, 0.25, stabilityMargin: 0.1);
        var program = BumpedStrip();
        var clearance = SupportAnalyzer.InsideCornerClearance(TabSize);
        var support = SupportAnalyzer.Analyze(bed, 1, SupportMaterial.FromProgram(program, Vector.Zero), clearance);
        var prepared = PreparedContours.Capture(program, Parameters())
            .WithSupportTab(support, Vector.Zero, new NormalTab { Size = TabSize });
        var corners = new[] { new Vector(4, 1), new Vector(6, 1) };
        bool NearACorner(ContourEntryCandidate c) => corners.Any(corner => c.Choice.Point.DistanceTo(corner) < clearance);

        var clear = prepared.AutomaticEntryCandidatesWithFallbacks(prepared.PerimeterOrdinal,
            supportEntries: SupportEntries.ClearOfInsideCorners);
        var any = prepared.AutomaticEntryCandidatesWithFallbacks(prepared.PerimeterOrdinal,
            supportEntries: SupportEntries.Any);

        Assert.Contains(any, NearACorner);
        Assert.DoesNotContain(clear, NearACorner);
        Assert.Equal(any.Count(c => !NearACorner(c)), clear.Count);
    }

    [Fact]
    public void SupportedPart_IsNotTabbed()
    {
        var (plate, _) = Strip(new SupportGrid(0.5, 0.5, 0.25, 0.25, stabilityMargin: 0.1));

        var result = CuttingPlanService.Plan(CuttingPlanRequest.ForPlate(plate, new Vector(0, 0),
            confirmedParameters: Parameters()));

        Assert.Equal(CuttingPlanStatus.Ready, result.Status);
        var (entry, cutEnd) = EntryAndCutEnd(Assert.Single(result.ProposedOrder));
        Assert.Equal(0, entry.DistanceTo(cutEnd), 6);
    }

    [Fact]
    public void WithoutSupportTabs_TheGridChangesNothing()
    {
        var bed = new SupportGrid(2, 4, 0, 2.25, stabilityMargin: 0.1);
        var parameters = Parameters();
        parameters.SupportTabs = false;
        var (withBed, _) = Strip(bed);
        var (withoutBed, _) = Strip(null);

        var planned = CuttingPlanService.Plan(CuttingPlanRequest.ForPlate(withBed, new Vector(0, 0),
            confirmedParameters: parameters));
        var plain = CuttingPlanService.Plan(CuttingPlanRequest.ForPlate(withoutBed, new Vector(0, 0),
            confirmedParameters: parameters));

        Assert.Equal(ExplicitContourTests.Fingerprint(Assert.Single(plain.ProposedOrder).CopyProgram()),
            ExplicitContourTests.Fingerprint(Assert.Single(planned.ProposedOrder).CopyProgram()));
    }
}
