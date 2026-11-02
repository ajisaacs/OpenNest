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
/// Support tabs on a bed for parts short enough for their tab alone to hold
/// (<see cref="CuttingParameters.TabHeldMaxLength"/>): a part the bed cannot hold level is still
/// tabbed, but any tab clear of inside corners steadies it, so it is not reported.
/// </summary>
public class TabHeldSupportTabTests
{
    private const double TabSize = 0.2;

    // A 2 inch laser grid; a 1.5 inch square rests on one of its points at most.
    private static readonly SupportGrid Bed = new(2, 2, 0, 0, registrationTolerance: 0.125, stabilityMargin: 0.25);

    private static CuttingParameters Parameters(double tabHeldMaxLength)
    {
        var parameters = ExplicitContourTests.Parameters();
        parameters.SupportTabs = true;
        parameters.TabConfig = new NormalTab { Size = TabSize };
        parameters.TabHeldMaxLength = tabHeldMaxLength;
        return parameters;
    }

    // A square `size` on a side at (3, 3) on the 2 inch grid.
    private static (Nest Nest, Plate Plate, Part Part) Square(CuttingParameters parameters, double size = 1.5)
    {
        var nest = new Nest("tab held");
        var plate = new Plate(20, 20) { CuttingParameters = parameters };
        nest.Plates.Add(plate);
        nest.SetSupportBed(Bed);
        var part = new Part(new Drawing("square", LeadPathValidationTests.Rectangle(0, 0, size, size)), new Vector(3, 3));
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
    [InlineData(0, false)]      // off
    [InlineData(1, false)]
    [InlineData(1.5, true)]     // the bound is inclusive
    [InlineData(2, true)]
    public void TheLimitHoldsPartsUpToIt(double max, bool holdsALengthOfOneAndAHalf)
    {
        Assert.Equal(holdsALengthOfOneAndAHalf, new CuttingParameters { TabHeldMaxLength = max }.TabHoldsAt(1.5));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(45)]
    [InlineData(90)]
    [InlineData(135)]
    [InlineData(180)]
    [InlineData(270)]
    [InlineData(315)]
    public void LengthOfATriangleWithTwoLeastBoxes_DoesNotDependOnItsRotation(double degrees)
    {
        // A 5 x 5 right triangle fits its legs' square and its hypotenuse's 7.07 x 3.54 box, both
        // of area 25: the squarer box is the one measured, whatever way the part is turned.
        var triangle = new Program();
        triangle.MoveTo(0, 0); triangle.LineTo(5, 0); triangle.LineTo(0, 5); triangle.LineTo(0, 0);
        var part = new Part(new Drawing("gusset", triangle), new Vector(10, 10));
        part.Rotate(degrees * System.Math.PI / 180);
        var material = SupportMaterial.FromPart(part);

        Assert.Equal(5, SupportAnalyzer.Length(material), 6);
        Assert.Equal(5, SupportAnalyzer.Width(material), 6);
    }

    [Fact]
    public void NewSettingsHoldPartsUpToSixLong()
    {
        Assert.Equal(6, new CuttingParameters().TabHeldMaxLength);
        Assert.True(new CuttingParameters().TabHoldsAt(5));
    }

    [Fact]
    public void Analyze_HoldsAShortPartTheBedCannotByAnyClearTab()
    {
        var (_, _, part) = Square(Parameters(2));
        var material = SupportMaterial.FromPart(part);

        var held = SupportTabSettings.Analyze(Parameters(2), Bed, 1, material);
        var bedOnly = SupportTabSettings.Analyze(Parameters(0), Bed, 1, material);

        Assert.Equal(1.5, SupportAnalyzer.Length(material), 6);
        Assert.False(bedOnly.IsHeldByTab);
        Assert.False(bedOnly.TabCanSupport);
        Assert.True(held.IsHeldByTab);
        Assert.True(held.HasBed);
        Assert.False(held.IsSupported);
        Assert.Equal(bedOnly.Margin, held.Margin);
        Assert.True(held.TabCanSupport);
        Assert.True(held.AcceptsTabAt(new Vector(3, 3.75)));
        Assert.True(held.IsSteadiedByTabAt(new Vector(4.5, 3.75)));
        Assert.False(SupportTabSettings.Analyze(Parameters(1), Bed, 1, material).IsHeldByTab);
    }

    [Fact]
    public void Analyze_LeavesAPartTheBedHoldsAlone()
    {
        // A 5 inch square rests on a 2 x 2 block of grid points around its center.
        var (_, _, part) = Square(Parameters(6), 5);

        var support = SupportTabSettings.Analyze(Parameters(6), Bed, 1, SupportMaterial.FromPart(part));

        Assert.True(support.IsSupported);
        Assert.False(support.IsHeldByTab);
    }

    [Fact]
    public void PlanCutting_WarnsAboutAShortPartNoTabCanSteady_WithoutTheLimit()
    {
        var (_, plate, _) = Square(Parameters(0));

        var result = CuttingPlanService.Plan(CuttingPlanRequest.ForPlate(plate, Vector.Zero,
            confirmedParameters: Parameters(0)));

        Assert.Equal(CuttingPlanStatus.BestEffort, result.Status);
        Assert.Contains("no single tab can steady it", Assert.Single(result.Findings).Message);
    }

    [Fact]
    public void PlanCutting_TabsAShortPartWithoutAWarning()
    {
        var (_, plate, _) = Square(Parameters(2));

        var result = CuttingPlanService.Plan(CuttingPlanRequest.ForPlate(plate, Vector.Zero,
            confirmedParameters: Parameters(2)));

        Assert.True(result.Status == CuttingPlanStatus.Ready, string.Join("\n", result.Findings.Select(f => f.Message)));
        Assert.Equal(TabSize, TabGap(Assert.Single(result.ProposedOrder)), 6);
        Assert.Empty(result.Findings);
    }

    [Fact]
    public void PlanCutting_TabsARoundPartsOneCircleOutline()
    {
        // A 2 inch disc whose outline is one circle: the plan must leave the tab open, and the
        // pre-post review must find it.
        var disc = new Program();
        disc.MoveTo(2, 1);
        disc.ArcTo(2, 1, 1, 1, RotationType.CCW);
        var nest = new Nest("round");
        var plate = new Plate(20, 20) { CuttingParameters = Parameters(2) };
        nest.Plates.Add(plate);
        nest.SetSupportBed(Bed);
        plate.Parts.Add(new Part(new Drawing("disc", disc), new Vector(3, 3)));

        var proposal = CuttingPlanBatch.Capture([plate], Parameters(2), preservePartOrder: false).Plan();

        Assert.True(proposal.CanApply, string.Join("\n", proposal.Describe("in")));
        Assert.Equal(TabSize, TabGap(Assert.Single(proposal.Plates[0].Result.ProposedOrder)), 6);
        Assert.Equal(CuttingCommitStatus.Applied, proposal.Apply().Status);
        Assert.DoesNotContain(PostVerificationAnalyzer.Analyze(nest).Findings,
            f => f.Kind == PostVerificationKind.PartSupport);
    }

    [Fact]
    public void PrePost_WarnsOnlyAboutAnUntabbedShortPart()
    {
        var lead = new Line(new Vector(0, 0), new Vector(0, 1.5));
        var (untabbed, _, bare) = Square(Parameters(2));
        bare.ApplySingleLeadIn(Parameters(2), new Vector(0, 0.75), lead, ContourType.External);
        var (tabbed, _, held) = Square(Parameters(2));
        var withTabs = Parameters(2);
        withTabs.TabsEnabled = true;
        held.ApplySingleLeadIn(withTabs, new Vector(0, 0.75), lead, ContourType.External);

        var finding = Assert.Single(PostVerificationAnalyzer.Analyze(untabbed).Findings,
            f => f.Kind == PostVerificationKind.PartSupport);
        Assert.Contains("short enough for its tab alone to hold", finding.Message);
        Assert.Contains("no tab", finding.Message);
        Assert.DoesNotContain(PostVerificationAnalyzer.Analyze(tabbed).Findings,
            f => f.Kind == PostVerificationKind.PartSupport);
    }
}
