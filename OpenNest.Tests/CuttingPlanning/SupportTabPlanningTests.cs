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
    public void BlockedSteadyingSide_TabsTheSteadiestEntryThatFitsAndReportsIt()
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

        Assert.True(result.Status == CuttingPlanStatus.BestEffort, string.Join("\n", result.Findings.Select(f => f.Message)));
        var proposal = result.ProposedOrder.Single(p => ReferenceEquals(p.SourcePart, strip));
        var (entry, cutEnd) = EntryAndCutEnd(proposal);
        Assert.Equal(TabSize, entry.DistanceTo(cutEnd), 6);
        // The steadiest entry that fits: halfway up an end, not the bottom corner nearer the start.
        var support = SupportAnalyzer.Analyze(bed, plate.Quadrant, strip);
        Assert.True(support.MarginWith(entry) > support.MarginWith(new Vector(7, 2)), $"entry {entry}");
        Assert.True(entry.Y > 2.5 - 1e-9, $"entry {entry}");
        Assert.True(support.MarginWith(entry) > support.MarginWith(new Vector(12, 2)), $"entry {entry}");
        Assert.False(support.HoldsLevelWithTabAt(entry));
        Assert.Contains(result.Findings, f => ReferenceEquals(f.SourcePart, strip)
            && f.Kind == PostVerificationKind.PartSupport
            && f.Message.Contains("no lead-in fits at a point where its tab would steady the part"));
    }

    [Fact]
    public void PartNoSingleTabCanHold_IsTabbedAndReported()
    {
        // The grid's rows run either side of the strip: nothing under it, so one tab can only balance it.
        var bed = new SupportGrid(1, 1, 0, 0.9, stabilityMargin: 0.1);
        var plate = new Plate(20, 20) { SupportBed = bed };
        var strip = new Part(new Drawing("strip", LeadPathValidationTests.Rectangle(0, 0, 10, 0.8)), new Vector(2, 2));
        plate.Parts.Add(strip);

        var result = CuttingPlanService.Plan(CuttingPlanRequest.ForPlate(plate, new Vector(0, 0),
            confirmedParameters: Parameters()));

        Assert.Equal(CuttingPlanStatus.BestEffort, result.Status);
        var (entry, cutEnd) = EntryAndCutEnd(Assert.Single(result.ProposedOrder));
        Assert.Equal(TabSize, entry.DistanceTo(cutEnd), 6);
        var warning = Assert.Single(result.Findings);
        Assert.Same(strip, warning.SourcePart);
        Assert.Equal(PostVerificationKind.PartSupport, warning.Kind);
        Assert.Contains("no single tab can steady it", warning.Message);
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
