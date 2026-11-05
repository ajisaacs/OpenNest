using OpenNest.CNC;
using OpenNest.CNC.CuttingPlanning;
using OpenNest.CNC.CuttingStrategy;
using OpenNest.Diagnostics;
using OpenNest.Engine.CuttingPlanning;
using OpenNest.Geometry;
using OpenNest.IO;
using OpenNest.Support;
using OpenNest.Tests.CuttingPlanning;

namespace OpenNest.Tests.Support;

/// <summary>
/// Tabs by size (<see cref="CuttingParameters.TabBySize"/>): the support bed is ignored and every
/// part whose width is in the size range is tabbed, as on a plate with no bed; and the retired
/// "Tab every part" setting migrates to it in the user's settings only.
/// </summary>
public class TabBySizeTests
{
    private const double TabSize = 0.2;

    // Contacts every 2 along X and 4 along Y from (0, 2.25): a 10 x 1 strip at (2, 2) rests on
    // one row of contacts, so the bed cannot hold it level.
    private static SupportBed Bed() => new SupportGrid(2, 4, 0, 2.25, stabilityMargin: 0.1);

    private static CuttingParameters Parameters(double min = 0.5, double max = 2, bool bySize = true)
    {
        var parameters = ExplicitContourTests.Parameters();
        parameters.SupportTabs = true;
        parameters.TabBySize = bySize;
        parameters.TabConfig = new NormalTab { Size = TabSize };
        parameters.TabSizeMinWidth = min;
        parameters.TabSizeMaxWidth = max;
        return parameters;
    }

    private static (Nest Nest, Plate Plate, Part Part) Strip(CuttingParameters parameters)
    {
        var nest = new Nest("by size");
        var plate = new Plate(20, 20) { CuttingParameters = parameters, SupportBed = Bed() };
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

    [Fact]
    public void Analyze_BySizeIgnoresTheBed()
    {
        var (_, _, part) = Strip(Parameters());
        var material = SupportMaterial.FromPart(part);

        var byBed = SupportTabSettings.Analyze(Parameters(bySize: false), Bed(), 1, material);
        Assert.True(byBed.HasBed);
        Assert.False(byBed.IsSupported);
        var bySize = SupportTabSettings.Analyze(Parameters(), Bed(), 1, material);
        Assert.False(bySize.HasBed);
        Assert.False(bySize.IsSupported);
        // A size-tabbed part is held by any clear tab, so it is never a rapid obstacle.
        Assert.True(bySize.TabCanSupport);
        // Out of the range it is not tabbed, though the bed cannot hold it level.
        Assert.Null(SupportTabSettings.Analyze(Parameters(2, 5), Bed(), 1, material));
    }

    [Fact]
    public void CanTab_NeedsTheBedOrARange()
    {
        Assert.True(SupportTabSettings.CanTab(Parameters(0, 0, bySize: false), Bed()));
        Assert.False(SupportTabSettings.CanTab(Parameters(0, 0), Bed()));
        Assert.True(SupportTabSettings.CanTab(Parameters(), Bed()));
        Assert.False(SupportTabSettings.CanTab(Parameters(0, 0, bySize: false), null));
        Assert.True(SupportTabSettings.SkipsForNoRange(Parameters(0, 0), Bed()));
        Assert.False(SupportTabSettings.SkipsForNoRange(Parameters(0, 0, bySize: false), Bed()));
    }

    [Fact]
    public void PlanCutting_TabsAPartInTheRangeOnAPlateWithABed()
    {
        var (_, plate, _) = Strip(Parameters());

        var result = CuttingPlanService.Plan(CuttingPlanRequest.ForPlate(plate, Vector.Zero,
            confirmedParameters: Parameters()));

        Assert.True(result.Status == CuttingPlanStatus.Ready, string.Join("\n", result.Findings.Select(f => f.Message)));
        Assert.Equal(TabSize, TabGap(Assert.Single(result.ProposedOrder)), 6);
        Assert.Empty(result.Findings);
    }

    [Fact]
    public void PlanCutting_LeavesAPartOutsideTheRangeUntabbedWithoutWarning()
    {
        var (_, plate, _) = Strip(Parameters(2, 5));

        var result = CuttingPlanService.Plan(CuttingPlanRequest.ForPlate(plate, Vector.Zero,
            confirmedParameters: Parameters(2, 5)));

        Assert.Equal(CuttingPlanStatus.Ready, result.Status);
        Assert.Equal(0, TabGap(Assert.Single(result.ProposedOrder)), 6);
        Assert.Empty(result.Findings);
    }

    [Fact]
    public void PlanCutting_NotesThatNothingWasTabbedWithNoRange()
    {
        var (_, plate, _) = Strip(Parameters(0, 0));

        var result = CuttingPlanService.Plan(CuttingPlanRequest.ForPlate(plate, Vector.Zero,
            confirmedParameters: Parameters(0, 0)));

        Assert.Equal(CuttingPlanStatus.Ready, result.Status);
        Assert.Equal(0, TabGap(Assert.Single(result.ProposedOrder)), 6);
        var note = Assert.Single(result.Findings);
        Assert.Null(note.Kind);
        Assert.Contains("Tabs by size", note.Message);
    }

    [Fact]
    public void ManualLeadIn_TabsAPartInTheRangeOnAPlateWithABed()
    {
        var parameters = Parameters();
        var (_, plate, part) = Strip(parameters);

        Assert.True(SupportTabSettings.ForManualLeadIn(parameters, plate, part).TabsEnabled);
        var outOfRange = Parameters(2, 5);
        Assert.Same(outOfRange, SupportTabSettings.ForManualLeadIn(outOfRange, plate, part));
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
        Assert.Contains("tabbed by size", finding.Message);
        Assert.DoesNotContain(PostVerificationAnalyzer.Analyze(tabbed).Findings,
            f => f.Kind == PostVerificationKind.PartSupport);
    }

    [Fact]
    public void MigrateTabEveryPart_TabsBySizeOverEveryWidth()
    {
        var parameters = new CuttingParameters { TabsEnabled = true, TabSizeMinWidth = 1, TabSizeMaxWidth = 2 };

        Assert.True(SupportTabSettings.MigrateTabEveryPart(parameters));

        Assert.False(parameters.TabsEnabled);
        Assert.True(parameters.SupportTabs);
        Assert.True(parameters.TabBySize);
        Assert.Equal(0, parameters.TabSizeMinWidth);
        Assert.Equal(SupportTabSettings.TabEveryPartMaxWidth, parameters.TabSizeMaxWidth);
        Assert.True(parameters.TabsBySizeAt(500));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, true)] // a part's copy that Plan Cutting tabbed
    public void MigrateTabEveryPart_LeavesOtherSettingsAlone(bool tabsEnabled, bool supportTabs)
    {
        var parameters = new CuttingParameters { TabsEnabled = tabsEnabled, SupportTabs = supportTabs };

        Assert.False(SupportTabSettings.MigrateTabEveryPart(parameters));
        Assert.Equal(tabsEnabled, parameters.TabsEnabled);
        Assert.Equal(supportTabs, parameters.SupportTabs);
        Assert.False(parameters.TabBySize);
    }

    [Fact]
    public void Serializer_RoundTripsTabBySize_AndNeverMigratesRecordedParameters()
    {
        var bySize = CuttingParametersSerializer.Deserialize(CuttingParametersSerializer.Serialize(Parameters()));
        Assert.True(bySize.TabBySize);
        Assert.Equal(0.5, bySize.TabSizeMinWidth);
        Assert.Equal(2, bySize.TabSizeMaxWidth);

        // A plate's recorded parameters with tabs on every contour replay as planned.
        var recorded = new CuttingParameters { TabsEnabled = true, TabConfig = new NormalTab { Size = TabSize } };
        var read = CuttingParametersSerializer.Deserialize(CuttingParametersSerializer.Serialize(recorded));
        Assert.True(read.TabsEnabled);
        Assert.False(read.SupportTabs);
        Assert.False(read.TabBySize);
    }
}
