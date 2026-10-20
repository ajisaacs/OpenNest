using OpenNest.CNC;
using OpenNest.CNC.CuttingPlanning;
using OpenNest.CNC.CuttingStrategy;
using OpenNest.Diagnostics;
using OpenNest.Engine;
using OpenNest.Engine.CuttingPlanning;
using OpenNest.Engine.Sequencing;
using OpenNest.Geometry;

namespace OpenNest.Tests.CuttingPlanning;

/// <summary>
/// The pre-post review reports a part whose active pins its program does not apply, plus fixed
/// (locked or pinned) lead violations.
/// </summary>
public class DrawingPinReviewTests
{
    [Fact]
    public void PrePost_PinsAppliedByPlanCutting_AreNotReported()
    {
        var (nest, plate, part) = PinnedNest();
        var proposal = CuttingPlanBatch.Capture([plate], PinnedContourTests.NestSettings(), false).Plan();
        Assert.Equal(CuttingCommitStatus.Applied, proposal.Apply().Status);

        var review = PostVerificationAnalyzer.Analyze(nest);

        Assert.DoesNotContain(review.Findings, f => f.Kind is PostVerificationKind.FixedLead or PostVerificationKind.Incomplete);
        Assert.True(part.HasManualLeadIns);
    }

    [Fact]
    public void PrePost_PinsNotApplied_IsAWarning()
    {
        var (nest, _, _) = PinnedNest(); // The program is the drawing's own: no pinned lead-in.

        var finding = Assert.Single(PostVerificationAnalyzer.Analyze(nest).Findings, f => f.Kind == PostVerificationKind.FixedLead);

        Assert.Equal(1, finding.PartNumber);
        Assert.Equal("Drawing pins not applied: drawing pin 1 is not cut as pinned; plan cutting to apply it.", finding.Message);
    }

    [Fact]
    public void PrePost_PinsOff_IsNotReported()
    {
        var (nest, _, part) = PinnedNest();
        part.DrawingPinsOff = true;

        Assert.DoesNotContain(PostVerificationAnalyzer.Analyze(nest).Findings, f => f.Kind == PostVerificationKind.FixedLead);
    }

    [Fact]
    public void PrePost_LockedLeadInsOverridingPins_IsAWarning()
    {
        var (nest, _, part) = PinnedNest();
        var prepared = PreparedContours.Capture(part.BaseDrawing.Program, ExplicitContourTests.Parameters());
        Assert.True(part.RestoreLeadInProgram(prepared.Emit([prepared.ClosestEntry(0, new Vector(-1, 5))]), true));

        var finding = Assert.Single(PostVerificationAnalyzer.Analyze(nest).Findings, f => f.Kind == PostVerificationKind.FixedLead);

        Assert.StartsWith("Drawing pins not applied: the part's locked lead-ins override them.", finding.Message);
    }

    [Fact]
    public void PrePost_UnresolvedPin_IsAWarning()
    {
        var nest = new Nest();
        var plate = nest.CreatePlate();
        plate.Parts.Add(new Part(PinnedContourTests.SquareWithPin(new Vector(5, 5), PinnedContourTests.Corner()),
            new Vector(5, 5)));

        var finding = Assert.Single(PostVerificationAnalyzer.Analyze(nest).Findings, f => f.Kind == PostVerificationKind.FixedLead);

        Assert.Equal("Drawing pins not applied: drawing pin 1 no longer matches the geometry.", finding.Message);
    }

    [Fact]
    public void PrePost_PinnedLeadIntoNeighbour_IsReportedAgainAfterAcceptance()
    {
        var nest = new Nest();
        var plate = nest.CreatePlate();
        var drawing = new Drawing("pinned", ExplicitContourTests.Square(false));
        drawing.CutPins.Add(new ContourCutPin(new Vector(10, 5), new LineLeadIn { Length = 1.0, ApproachAngle = 90 },
            new NoLeadOut()));
        plate.Parts.Add(new Part(drawing, Vector.Zero));
        plate.Parts.Add(new Part(new Drawing("neighbour", LeadPathValidationTests.Rectangle(0, 0, 2, 2)), new Vector(10.3, 4)));
        var proposal = CuttingPlanBatch.Capture([plate], PinnedContourTests.NestSettings(), false).Plan();
        Assert.Equal(CuttingCommitStatus.Applied, proposal.Apply(acceptWarnings: true).Status);
        var pinned = plate.Parts.Single(p => p.BaseDrawing == drawing);
        var neighbour = plate.Parts.Single(p => p.BaseDrawing != drawing);

        var finding = Assert.Single(PostVerificationAnalyzer.Analyze(nest).Findings, f => f.Kind == PostVerificationKind.FixedLead);

        Assert.Equal((plate.Parts.IndexOf(pinned) + 1, plate.Parts.IndexOf(neighbour) + 1),
            (finding.PartNumber!.Value, finding.OtherPartNumber!.Value));
        Assert.Equal($"Drawing pin 1 lead on cutting contour 1 contacts or enters the material of part "
            + $"{plate.Parts.IndexOf(neighbour) + 1}. Planning kept it as given; review it before posting.", finding.Message);
    }

    [Fact]
    public void PrePost_LockedLeadTouchingAClamp_IsReported()
    {
        var nest = new Nest();
        var plate = nest.CreatePlate();
        plate.Size = new Size(100, 100);
        var part = new Part(new Drawing("square", ExplicitContourTests.Square(false)), new Vector(5, 5));
        var program = new Program();
        program.MoveTo(-0.3, 5);
        program.Codes.Add(new LinearMove(0, 5) { Layer = LayerType.Leadin });
        program.LineTo(0, 10); program.LineTo(10, 10); program.LineTo(10, 0); program.LineTo(0, 0); program.LineTo(0, 5);
        Assert.True(part.RestoreLeadInProgram(program, true));
        plate.Parts.Add(part);
        plate.ClampZones.Add(new ClampZone("left", new Box(4.6, 9.9, 0.3, 0.2)));

        var findings = PostVerificationAnalyzer.Analyze(nest).Findings;

        var finding = Assert.Single(findings, f => f.Kind == PostVerificationKind.FixedLead);
        Assert.Equal("left", finding.ClampId);
        Assert.Equal("Locked lead on cutting contour 1 touches active clamp 'left'. Planning kept it as given; "
            + "review it before posting.", finding.Message);
    }

    private static (Nest Nest, Plate Plate, Part Part) PinnedNest()
    {
        var nest = new Nest();
        var plate = nest.CreatePlate();
        var part = new Part(PinnedContourTests.SquareWithPin(Vector.Zero, PinnedContourTests.Corner()), new Vector(5, 5));
        plate.Parts.Add(part);
        return (nest, plate, part);
    }
}
