using OpenNest.CNC;
using OpenNest.CNC.CuttingStrategy;
using OpenNest.Diagnostics;
using OpenNest.Engine.CuttingPlanning;
using OpenNest.Geometry;
using OpenNest.Support;
using OpenNest.Tests.CuttingPlanning;

namespace OpenNest.Tests.Support;

/// <summary>
/// Parts the support grid cannot hold level: hand-placed lead-ins tab them, Plan Cutting warns
/// when it cannot steady them, and the pre-post review warns about any without a steadying tab.
/// </summary>
public class SupportReviewTests
{
    // One row of grid points at y = 2.25 runs under the 10 x 1 strip at (2, 2), below its center.
    private static readonly SupportGrid OneRow = new(2, 4, 0, 2.25, stabilityMargin: 0.1);

    private static CuttingParameters Parameters(bool tabs = false) => new()
    {
        ExternalLeadIn = new LineLeadIn { Length = 0.3, ApproachAngle = 90 },
        SupportTabs = true,
        TabsEnabled = tabs,
        TabConfig = new NormalTab { Size = 0.2 },
    };

    private static (Nest Nest, Plate Plate, Part Part) Strip(SupportBed bed, double height = 1)
    {
        var nest = new Nest("support");
        nest.SetSupportBed(bed);
        var plate = new Plate(20, 20) { CuttingParameters = Parameters() };
        nest.Plates.Add(plate);
        var part = new Part(new Drawing("strip", LeadPathValidationTests.Rectangle(0, 0, 10, height)), new Vector(2, 2));
        plate.Parts.Add(part);
        return (nest, plate, part);
    }

    private static void PlaceLeadIn(Part part, CuttingParameters parameters, Vector point, Line edge) =>
        part.ApplySingleLeadIn(parameters, point, edge, ContourType.External);

    [Fact]
    public void ManualLeadIn_TabsAnUnsupportedPartWithoutChangingTheSettings()
    {
        var (_, plate, part) = Strip(OneRow);
        var parameters = Parameters();

        var used = SupportTabSettings.ForManualLeadIn(parameters, plate, part);

        Assert.NotSame(parameters, used);
        Assert.True(used.TabsEnabled);
        Assert.False(parameters.TabsEnabled);
    }

    [Fact]
    public void ManualLeadIn_KeepsTheSettingsForASupportedPartOrWithSupportTabsOff()
    {
        var (_, plate, part) = Strip(new SupportGrid(0.5, 0.5, 0.25, 0.25, stabilityMargin: 0.1));
        var parameters = Parameters();
        Assert.Same(parameters, SupportTabSettings.ForManualLeadIn(parameters, plate, part));

        var (_, tipping, strip) = Strip(OneRow);
        parameters.SupportTabs = false;
        Assert.Same(parameters, SupportTabSettings.ForManualLeadIn(parameters, tipping, strip));
    }

    [Fact]
    public void PrePost_WarnsAboutAnUnsupportedPartWithoutATab()
    {
        var (nest, _, part) = Strip(OneRow);
        PlaceLeadIn(part, Parameters(), new Vector(5, 1), new Line(new Vector(0, 1), new Vector(10, 1)));

        var finding = Assert.Single(PostVerificationAnalyzer.Analyze(nest).Findings,
            f => f.Kind == PostVerificationKind.PartSupport);

        Assert.Equal(1, finding.PartNumber);
        Assert.Contains("no tab", finding.Message);
        Assert.Equal(new Vector(7, 2.5), finding.Location);
    }

    [Fact]
    public void PrePost_AcceptsATabThatSteadiesThePart_AndWarnsAboutOneThatDoesNot()
    {
        var (steadied, _, top) = Strip(OneRow);
        PlaceLeadIn(top, Parameters(tabs: true), new Vector(5, 1), new Line(new Vector(0, 1), new Vector(10, 1)));
        var (unsteady, _, bottom) = Strip(OneRow);
        PlaceLeadIn(bottom, Parameters(tabs: true), new Vector(5, 0), new Line(new Vector(10, 0), new Vector(0, 0)));

        Assert.DoesNotContain(PostVerificationAnalyzer.Analyze(steadied).Findings,
            f => f.Kind == PostVerificationKind.PartSupport);
        var finding = Assert.Single(PostVerificationAnalyzer.Analyze(unsteady).Findings,
            f => f.Kind == PostVerificationKind.PartSupport);
        Assert.Contains("does not steady it", finding.Message);
    }

    [Fact]
    public void PrePost_IgnoresPartsWithoutSupportTabsOrABed()
    {
        var (nest, _, part) = Strip(OneRow);
        var parameters = Parameters();
        parameters.SupportTabs = false;
        // A placed lead-in keeps its settings on the part, which the review reads first.
        PlaceLeadIn(part, parameters, new Vector(5, 1), new Line(new Vector(0, 1), new Vector(10, 1)));
        Assert.DoesNotContain(PostVerificationAnalyzer.Analyze(nest).Findings, f => f.Kind == PostVerificationKind.PartSupport);

        parameters.SupportTabs = true;
        nest.SetSupportBed(null);
        Assert.DoesNotContain(PostVerificationAnalyzer.Analyze(nest).Findings, f => f.Kind == PostVerificationKind.PartSupport);
    }

    [Fact]
    public void PlanCutting_WarnsWhenNoSingleTabCanSteadyThePart()
    {
        // The 10 x 0.8 strip lies between grid rows: it rests on nothing, so a tab only hangs it.
        var (_, plate, _) = Strip(new SupportGrid(1, 1, 0, 0.9, stabilityMargin: 0.1), height: 0.8);

        var result = CuttingPlanService.Plan(CuttingPlanRequest.ForPlate(plate, Vector.Zero,
            confirmedParameters: Parameters()));

        Assert.Equal(CuttingPlanStatus.BestEffort, result.Status);
        var finding = Assert.Single(result.Findings, f => f.Kind == PostVerificationKind.PartSupport);
        Assert.Contains("no single tab can steady it", finding.Message);
    }
}
