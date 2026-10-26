using OpenNest.CNC;
using OpenNest.CNC.CuttingPlanning;
using OpenNest.CNC.CuttingStrategy;
using OpenNest.Diagnostics;
using OpenNest.Engine.CuttingPlanning;
using OpenNest.Geometry;
using OpenNest.Support;
using OpenNest.Tests.CuttingPlanning;

namespace OpenNest.Tests.Support;

/// <summary>
/// Support tabs on a plate with no support bed: the parts whose width is in the settings' no-bed
/// range are tabbed instead, any tab clear of inside corners counts, and with no range Plan
/// Cutting says that nothing was tabbed.
/// </summary>
public class NoBedSupportTabTests
{
    private const double TabSize = 0.2;

    private static CuttingParameters Parameters(double min = 0.5, double max = 2)
    {
        var parameters = ExplicitContourTests.Parameters();
        parameters.SupportTabs = true;
        parameters.TabConfig = new NormalTab { Size = TabSize };
        parameters.NoBedTabMinWidth = min;
        parameters.NoBedTabMaxWidth = max;
        return parameters;
    }

    // A 10 x 1 strip at (2, 2) on a plate with no support bed.
    private static (Nest Nest, Plate Plate, Part Part) Strip(CuttingParameters parameters)
    {
        var nest = new Nest("no bed");
        var plate = new Plate(20, 20) { CuttingParameters = parameters };
        nest.Plates.Add(plate);
        var part = new Part(new Drawing("strip", LeadPathValidationTests.Rectangle(0, 0, 10, 1)), new Vector(2, 2));
        plate.Parts.Add(part);
        return (nest, plate, part);
    }

    private static double TabGap(FixedProgramPlacement proposal)
    {
        var motions = ExecutionMotionReader.Read(proposal.CopyProgram(), proposal.Location, null, default).Motions;
        var entry = motions.Last(m => m.Layer == LayerType.Leadin).End;
        var cutEnd = motions.Last(m => !m.Rapid && m.Layer is LayerType.Cut or LayerType.Display).End;
        return entry.DistanceTo(cutEnd);
    }

    [Theory]
    [InlineData(0, 0, false)]   // no range
    [InlineData(2, 1, false)]   // minimum above maximum
    [InlineData(0.5, 2, true)]
    [InlineData(1, 1, true)]    // the bounds are inclusive
    public void TheRangeTabsWidthsInsideIt(double min, double max, bool tabsAWidthOfOne)
    {
        var parameters = new CuttingParameters { NoBedTabMinWidth = min, NoBedTabMaxWidth = max };

        Assert.Equal(tabsAWidthOfOne, parameters.TabsWithoutBedAt(1));
        Assert.False(parameters.TabsWithoutBedAt(3));
    }

    [Fact]
    public void WithoutABed_TheBestTabCrossesTheCenterAndAnyClearTabIsAccepted()
    {
        var (_, _, part) = Strip(Parameters());
        var material = SupportMaterial.FromPart(part);

        var support = SupportAnalyzer.AnalyzeWithoutBed(material);

        Assert.True(support.IsComplete);
        Assert.False(support.HasBed);
        Assert.False(support.IsSupported);
        Assert.Equal(1, support.Width, 6);
        Assert.Equal(1, SupportAnalyzer.Width(material), 6);
        // The nearest of the outline samples to the center line at x = 7.
        Assert.InRange(support.BestTabPoint!.Value.X, 6.9, 7.1);
        Assert.True(support.AcceptsTabAt(new Vector(2, 2.5)));
        Assert.True(support.IsSteadiedByTabAt(new Vector(12, 2.5)));
    }

    [Fact]
    public void Analyze_UsesTheBedWhenThereIsOneAndTheRangeOnlyWithout()
    {
        var (_, _, part) = Strip(Parameters());
        var material = SupportMaterial.FromPart(part);
        var bed = new SupportGrid(2, 4, 0, 2.25, stabilityMargin: 0.1);

        Assert.True(SupportTabSettings.Analyze(Parameters(), bed, 1, material).HasBed);
        Assert.False(SupportTabSettings.Analyze(Parameters(), null, 1, material).HasBed);
        Assert.Null(SupportTabSettings.Analyze(Parameters(2, 5), null, 1, material));
        Assert.Null(SupportTabSettings.Analyze(Parameters(0, 0), null, 1, material));
        var off = Parameters();
        off.SupportTabs = false;
        Assert.Null(SupportTabSettings.Analyze(off, null, 1, material));
    }

    [Fact]
    public void PlanCutting_TabsAPartInTheRange()
    {
        var (_, plate, _) = Strip(Parameters());

        var result = CuttingPlanService.Plan(CuttingPlanRequest.ForPlate(plate, Vector.Zero,
            confirmedParameters: Parameters()));

        Assert.True(result.Status == CuttingPlanStatus.Ready, string.Join("\n", result.Findings.Select(f => f.Message)));
        Assert.Equal(TabSize, TabGap(Assert.Single(result.ProposedOrder)), 6);
        Assert.Empty(result.Findings);
    }

    [Fact]
    public void PlanCutting_LeavesAPartOutsideTheRangeUntabbed()
    {
        var (_, plate, _) = Strip(Parameters(2, 5));

        var result = CuttingPlanService.Plan(CuttingPlanRequest.ForPlate(plate, Vector.Zero,
            confirmedParameters: Parameters(2, 5)));

        Assert.Equal(CuttingPlanStatus.Ready, result.Status);
        Assert.Equal(0, TabGap(Assert.Single(result.ProposedOrder)), 6);
        Assert.Empty(result.Findings);
    }

    [Fact]
    public void PlanCutting_NotesThatNothingWasTabbedWithoutABedOrARange()
    {
        var (_, plate, _) = Strip(Parameters(0, 0));

        var result = CuttingPlanService.Plan(CuttingPlanRequest.ForPlate(plate, Vector.Zero,
            confirmedParameters: Parameters(0, 0)));

        Assert.Equal(CuttingPlanStatus.Ready, result.Status);
        Assert.Equal(0, TabGap(Assert.Single(result.ProposedOrder)), 6);
        var note = Assert.Single(result.Findings);
        Assert.Null(note.Kind);
        Assert.Contains("no support bed", note.Message);
    }

    [Fact]
    public void ManualLeadIn_TabsAPartInTheRange()
    {
        var parameters = Parameters();
        var (_, plate, part) = Strip(parameters);

        Assert.True(SupportTabSettings.ForManualLeadIn(parameters, plate, part).TabsEnabled);
        Assert.Same(parameters, SupportTabSettings.ForManualLeadIn(parameters, plate, new Part(
            new Drawing("wide", LeadPathValidationTests.Rectangle(0, 0, 10, 5)), new Vector(2, 5))));
    }

    [Fact]
    public void PrePost_WarnsAboutAnUntabbedPartInTheRange_AndAcceptsAnyTab()
    {
        var (untabbed, _, bare) = Strip(Parameters());
        bare.ApplySingleLeadIn(Parameters(), new Vector(0, 0.5), new Line(new Vector(0, 0), new Vector(0, 1)),
            ContourType.External);
        var (tabbed, _, held) = Strip(Parameters());
        var withTabs = Parameters();
        withTabs.TabsEnabled = true;
        held.ApplySingleLeadIn(withTabs, new Vector(0, 0.5), new Line(new Vector(0, 0), new Vector(0, 1)),
            ContourType.External);

        var finding = Assert.Single(PostVerificationAnalyzer.Analyze(untabbed).Findings,
            f => f.Kind == PostVerificationKind.PartSupport);
        Assert.Contains("without a support bed", finding.Message);
        Assert.Contains("no tab", finding.Message);
        Assert.DoesNotContain(PostVerificationAnalyzer.Analyze(tabbed).Findings,
            f => f.Kind == PostVerificationKind.PartSupport);
    }
}
