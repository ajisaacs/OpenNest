using OpenNest.CNC;
using OpenNest.CNC.CuttingPlanning;
using OpenNest.CNC.CuttingStrategy;
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
