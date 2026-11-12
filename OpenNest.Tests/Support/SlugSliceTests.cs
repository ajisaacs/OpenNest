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
/// Slug slices (docs/support-tabs.md#slug-slices): the slug of a cutout its support bed cannot
/// hold level is cut along the bed's contact lines, a kerf short of the cutout, before the cutout.
/// </summary>
public class SlugSliceTests
{
    private const double Kerf = 0.06;

    // A 2 inch laser grid with no registration slack, so contacts are exactly on its lines.
    private static readonly SupportGrid Bed = new(2, 2);

    // An 8 x 1 slot at world (3, 3.5)-(11, 4.5): its slug rests on the grid's row at Y = 4 alone.
    private static readonly Vector[] Slot =
        [new(2, 2.5), new(10, 2.5), new(10, 3.5), new(2, 3.5)];

    private static readonly Vector Location = new(1, 1);

    private static CuttingParameters Parameters(bool slice = true, double kerf = Kerf)
    {
        var parameters = ExplicitContourTests.Parameters();
        parameters.Kerf = kerf;
        parameters.SliceTippingSlugs = slice;
        return parameters;
    }

    // A 12 inch square plate part with one rectangular hole, both local.
    private static Drawing Frame(double x1 = 2, double y1 = 2.5, double x2 = 10, double y2 = 3.5)
    {
        var program = LeadPathValidationTests.Rectangle(0, 0, 12, 12);
        var hole = LeadPathValidationTests.Rectangle(x1, y1, x2, y2);
        program.Codes.AddRange(hole.Codes);
        return new Drawing("frame", program);
    }

    private static (Nest Nest, Plate Plate, Part Part) Plate(CuttingParameters parameters, Drawing drawing = null,
        SupportBed bed = null)
    {
        var nest = new Nest("slug slices");
        var plate = new Plate(30, 30) { CuttingParameters = parameters };
        nest.Plates.Add(plate);
        nest.SetSupportBed(bed ?? Bed);
        var part = new Part(drawing ?? Frame(), Location);
        plate.Parts.Add(part);
        return (nest, plate, part);
    }

    private static IReadOnlyList<ExecutionMotion> Motions(FixedProgramPlacement proposal) =>
        ExecutionMotionReader.Read(proposal.CopyProgram(), proposal.Location, null, default).Motions;

    private static double DistanceToSlot(Vector world)
    {
        // The slot in world coordinates: (3, 3.5)-(11, 4.5); inside it, the nearest side.
        return System.Math.Min(System.Math.Min(world.X - 3, 11 - world.X), System.Math.Min(world.Y - 3.5, 4.5 - world.Y));
    }

    [Fact]
    public void ContactLines_AreTheGridsRowsAndColumnsAndTheSlatsCenters()
    {
        var region = new Box(3, 3.5, 8, 1);

        var grid = Bed.ContactLines(region, 1);
        var slats = new SupportSlats(SlatAxis.Y, 3, 0.25, offset: 1.5).ContactLines(region, 1);
        var mirrored = new SupportGrid(2, 2, 1, 0).ContactLines(new Box(-5, 0, 2, 1), 2);

        Assert.Equal([new(true, 4), new(false, 4), new(false, 6), new(false, 8), new(false, 10)], grid);
        Assert.Equal([new(false, 4.5), new(false, 7.5), new(false, 10.5)], slats);
        // A sheet in quadrant 2 sees the grid's X offset mirrored: columns at -1, -3, -5, ...
        Assert.Equal([new(true, 0), new(false, -5), new(false, -3)], mirrored);
    }

    [Fact]
    public void Tips_OnlyForASlugOnContactsTheBedCannotHoldLevel()
    {
        SupportMaterial World(double x1, double y1, double x2, double y2) =>
            SupportMaterial.FromPolygons([new(x1, y1), new(x2, y1), new(x2, y2), new(x1, y2)]);

        // On one row of contacts: it balances on a line and tips.
        Assert.True(SlugSlicer.Tips(Bed, 1, World(3, 3.5, 11, 4.5)));
        // On a block of contacts around its center: it lies level.
        Assert.False(SlugSlicer.Tips(Bed, 1, World(3, 3, 11, 9)));
        // Between contacts: it falls through whole.
        Assert.False(SlugSlicer.Tips(Bed, 1, World(2.5, 2.5, 3.5, 3.5)));
    }

    [Fact]
    public void Slices_RunAlongTheContactLinesAKerfShortOfTheCutout()
    {
        var slices = SlugSlicer.Slices(Bed, 1, Slot, Location, Kerf, reach: 0.3, pierceClearance: 0.05);

        // The row at world Y = 4 left to right, then the columns from X = 10 back to 4, back and forth.
        Assert.Equal(5, slices.Count);
        var row = slices[0];
        Assert.Equal(3, row.Start.Y, 9);
        Assert.Equal(3, row.End.Y, 9);
        Assert.True(row.End.X > row.Start.X);
        Assert.Equal([9, 7, 5, 3], slices.Skip(1).Select(s => System.Math.Round(s.Start.X, 9)));
        for (var i = 2; i < slices.Count; i++)
            Assert.NotEqual(slices[i - 1].End.Y > slices[i - 1].Start.Y, slices[i].End.Y > slices[i].Start.Y);
        foreach (var slice in slices)
        {
            // Within the Clipper rounding allowance of the kerf, never closer.
            foreach (var end in new[] { slice.Start, slice.End })
            {
                var distance = DistanceToSlot(end + Location);
                Assert.InRange(distance, Kerf, Kerf + 1e-3);
            }
            // Pierced on the slice, its lead-in reach (at most halfway) from the start.
            var length = slice.Start.DistanceTo(slice.End);
            Assert.Equal(System.Math.Min(0.3, length / 2), slice.Pierce.DistanceTo(slice.Start), 9);
            Assert.Equal(length, slice.Pierce.DistanceTo(slice.Start) + slice.Pierce.DistanceTo(slice.End), 9);
        }
    }

    [Fact]
    public void Slices_LeaveOutSlicesWhosePierceFitsNeitherEnd()
    {
        // Pierced 0.1 along a slice from an end 0.06 from the slot, no pierce keeps 0.2 from it.
        Assert.Empty(SlugSlicer.Slices(Bed, 1, Slot, Location, Kerf, reach: 0.1, pierceClearance: 0.2));
    }

    [Fact]
    public void PlanCutting_SlicesATippingSlugBeforeItsCutout()
    {
        var (nest, plate, _) = Plate(Parameters());

        var proposal = CuttingPlanBatch.Capture([plate], Parameters(), preservePartOrder: false).Plan();

        var result = proposal.Plates[0].Result;
        Assert.True(result.Status == CuttingPlanStatus.Ready,
            string.Join("\n", proposal.Describe("in").Concat(result.Findings.Select(f => f.Message))));
        Assert.True(proposal.CanApply);
        Assert.Empty(result.Findings);
        var motions = Motions(Assert.Single(result.ProposedOrder));
        var slices = motions.Where(m => !m.Rapid && m.Layer == LayerType.Break).ToList();
        Assert.Equal(5, slices.Count);
        foreach (var slice in slices)
        {
            Assert.InRange(DistanceToSlot(slice.Curve.Start), Kerf - 1e-9, 1);
            Assert.InRange(DistanceToSlot(slice.Curve.End), Kerf - 1e-9, 1);
        }
        // Every slice is cut before any contour, so before the slot.
        var lastSlice = motions.ToList().FindLastIndex(m => !m.Rapid && m.Layer == LayerType.Break);
        var firstCut = motions.ToList().FindIndex(m => !m.Rapid && m.Layer is LayerType.Cut or LayerType.Display);
        Assert.InRange(lastSlice, 0, firstCut - 1);

        Assert.Equal(CuttingCommitStatus.Applied, proposal.Apply().Status);
        var review = PostVerificationAnalyzer.Analyze(nest).Findings;
        Assert.DoesNotContain(review, f => f.Kind is PostVerificationKind.Incomplete or PostVerificationKind.RapidCrossing
            or PostVerificationKind.MissingLeadIn or PostVerificationKind.FixedLead or PostVerificationKind.PierceZone);
    }

    [Theory]
    [InlineData(false, Kerf)] // off
    [InlineData(true, 0)]     // no kerf to stop short by
    public void PlanCutting_SlicesNothing_WhenOffOrWithoutAKerf(bool slice, double kerf)
    {
        var (_, plate, _) = Plate(Parameters(slice, kerf));

        var result = CuttingPlanService.Plan(CuttingPlanRequest.ForPlate(plate, Vector.Zero,
            confirmedParameters: Parameters(slice, kerf)));

        Assert.Equal(CuttingPlanStatus.Ready, result.Status);
        Assert.DoesNotContain(Motions(Assert.Single(result.ProposedOrder)), m => m.Layer == LayerType.Break);
    }

    [Fact]
    public void PlanCutting_DoesNotSliceASlugTheBedHoldsOrThatFallsThrough()
    {
        // A 6 x 6 hole rests level on a block of contacts; a 1 x 1 hole between contacts falls through.
        var program = LeadPathValidationTests.Rectangle(0, 0, 12, 12);
        program.Codes.AddRange(LeadPathValidationTests.Rectangle(2, 2, 8, 8).Codes);
        program.Codes.AddRange(LeadPathValidationTests.Rectangle(9.5, 9.5, 10.5, 10.5).Codes);
        var (_, plate, _) = Plate(Parameters(), new Drawing("two holes", program));

        var result = CuttingPlanService.Plan(CuttingPlanRequest.ForPlate(plate, Vector.Zero,
            confirmedParameters: Parameters()));

        Assert.Equal(CuttingPlanStatus.Ready, result.Status);
        Assert.DoesNotContain(Motions(Assert.Single(result.ProposedOrder)), m => m.Layer == LayerType.Break);
    }

    [Fact]
    public void PlanCutting_DoesNotSliceACutoutAnotherPartIsNestedIn()
    {
        // A 7.5 x 0.6 strip nested in the slot would be cut through by the slices.
        var (_, plate, _) = Plate(Parameters());
        plate.Parts.Add(new Part(new Drawing("strip", LeadPathValidationTests.Rectangle(0, 0, 7.5, 0.6)),
            new Vector(3.25, 3.7)));

        var result = CuttingPlanService.Plan(CuttingPlanRequest.ForPlate(plate, Vector.Zero,
            confirmedParameters: Parameters()));

        Assert.True(result.Status == CuttingPlanStatus.Ready, string.Join("\n", result.Findings.Select(f => f.Message)));
        Assert.All(result.ProposedOrder, proposal =>
            Assert.DoesNotContain(Motions(proposal), m => m.Layer == LayerType.Break));
    }

    [Fact]
    public void PlanCutting_SlicesAlongSlats()
    {
        // Slats along X every 2 inches: the slot's slug rests on the one at Y = 4 alone, too
        // narrow under it for the stability margin.
        var (_, plate, _) = Plate(Parameters(), bed: new SupportSlats(SlatAxis.X, 2, 0.125, stabilityMargin: 0.25));

        var result = CuttingPlanService.Plan(CuttingPlanRequest.ForPlate(plate, Vector.Zero,
            confirmedParameters: Parameters()));

        Assert.Equal(CuttingPlanStatus.Ready, result.Status);
        var slice = Assert.Single(Motions(Assert.Single(result.ProposedOrder)), m => !m.Rapid && m.Layer == LayerType.Break);
        Assert.Equal(4, slice.Curve.Start.Y, 9);
        Assert.Equal(4, slice.Curve.End.Y, 9);
    }

    [Fact]
    public void Settings_RoundTripAndAreLeftOutWhenOff()
    {
        var on = OpenNest.IO.CuttingParametersSerializer.Serialize(Parameters());
        var off = OpenNest.IO.CuttingParametersSerializer.Serialize(Parameters(slice: false));

        Assert.True(OpenNest.IO.CuttingParametersSerializer.Deserialize(on).SliceTippingSlugs);
        Assert.False(OpenNest.IO.CuttingParametersSerializer.Deserialize(off).SliceTippingSlugs);
        Assert.DoesNotContain("sliceTippingSlugs", off);
        Assert.True(OwnedCuttingParameters.Copy(Parameters()).SliceTippingSlugs);
    }

    [Fact]
    public void ReleasedContours_CrossingCutsInsideAHoleCutOnlyItsSlug()
    {
        // Crossing break cuts in the slot, then the slot, then the outside cut in two open pieces,
        // so the open-cut proof runs.
        var findings = new ReleasedContourState().Check(
            ExecutionMotionReader.Read(TwoPieceFrameWithBreaks(inHole: true), Vector.Zero, null, default), Vector.Zero, 1);

        Assert.DoesNotContain(findings, f => f.Kind == PostVerificationKind.Incomplete);
    }

    [Fact]
    public void ReleasedContours_CrossingBreakCutsInMaterialStillNeedReview()
    {
        var findings = new ReleasedContourState().Check(
            ExecutionMotionReader.Read(TwoPieceFrameWithBreaks(inHole: false), Vector.Zero, null, default), Vector.Zero, 1);

        Assert.Contains(findings, f => f.Kind == PostVerificationKind.Incomplete
            && f.Message.Contains("Multiple interrupted/open"));
    }

    // A 14 inch square cut in two open pieces with a slot at (3, 3.5)-(11, 4.5), and a row and two
    // columns of break cuts crossing either inside the slot or in the material above it.
    private static Program TwoPieceFrameWithBreaks(bool inHole)
    {
        var y = inHole ? 4 : 8;
        var program = new Program();
        program.MoveTo(3.1, y);
        program.Codes.Add(new LinearMove(10.9, y) { Layer = LayerType.Break });
        foreach (var x in new[] { 4.0, 6.0 })
        {
            program.MoveTo(x, y - 0.4);
            program.Codes.Add(new LinearMove(x, y + 0.4) { Layer = LayerType.Break });
        }
        program.MoveTo(3, 3.5);
        program.LineTo(11, 3.5);
        program.LineTo(11, 4.5);
        program.LineTo(3, 4.5);
        program.LineTo(3, 3.5);
        program.MoveTo(0.2, 0);
        program.LineTo(14, 0);
        program.LineTo(14, 14);
        program.LineTo(7.2, 14);
        program.MoveTo(6.8, 14);
        program.LineTo(0, 14);
        program.LineTo(0, 0.2);
        return program;
    }
}
