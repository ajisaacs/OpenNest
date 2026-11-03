using OpenNest.CNC;
using OpenNest.CNC.CuttingPlanning;
using OpenNest.CNC.CuttingStrategy;
using OpenNest.Diagnostics;
using OpenNest.Engine.CuttingPlanning;
using OpenNest.Geometry;

namespace OpenNest.Tests.CuttingPlanning;

// A cutout's lead-out ends in its slug, so the rapid after it must leave over the cutout just cut.
// Plan Cutting raises the head for that rapid; without a lead-out the tool leaves from the cut.
public class CutoutLeadOutTests
{
    private static readonly Vector Start = new(-2, -2);

    private static Program SquareCutoutAndHole()
    {
        var clean = LeadPathValidationTests.Rectangle(0, 0, 10, 10);
        clean.MoveTo(3, 3);
        clean.LineTo(5, 3);
        clean.LineTo(5, 5);
        clean.LineTo(3, 5);
        clean.LineTo(3, 3);
        clean.MoveTo(8, 7.5);
        clean.ArcTo(8, 7.5, 7.5, 7.5, RotationType.CCW);
        return clean;
    }

    private static CuttingParameters Parameters(LeadOut internalLeadOut)
    {
        var lead = new LineLeadIn { Length = 0.15, ApproachAngle = 90 };
        return new CuttingParameters
        {
            ExternalLeadIn = lead,
            InternalLeadIn = lead,
            ExternalLeadOut = new LineLeadOut { Length = 0.1, ApproachAngle = 90 },
            InternalLeadOut = internalLeadOut,
            PierceClearance = 0
        };
    }

    private static CuttingPlanResult Plan(LeadOut internalLeadOut, out Part part)
    {
        part = new Part(new Drawing("cutout", SquareCutoutAndHole()));
        return CuttingPlanService.Plan(new CuttingPlanRequest([part], Start,
            confirmedParameters: Parameters(internalLeadOut)));
    }

    private static int Raises(Program program) =>
        program.Codes.Count(HeadRaise.IsMarker);

    [Fact]
    public void CutoutLeadOutsPlanWithTheHeadRaisedOutOfEachSlug()
    {
        var result = Plan(new LineLeadOut { Length = 0.1, ApproachAngle = 90 }, out _);

        Assert.True(result.Status == CuttingPlanStatus.Ready,
            $"{result.Status}: " + string.Join("; ", result.Findings.Select(f => f.Message)));
        Assert.False(result.IsSafestRouteFallback);
        Assert.Equal(0, result.HeadRaises);
        Assert.Empty(result.Findings);
        // Both cutouts are left from their slugs; the perimeter's lead-out ends outside the part.
        Assert.Equal(2, Raises(result.ProposedOrder[0].CopyProgram()));
    }

    [Fact]
    public void CutoutsWithoutLeadOutsKeepTheHeadDown()
    {
        var result = Plan(new NoLeadOut(), out _);

        Assert.True(result.Status == CuttingPlanStatus.Ready,
            $"{result.Status}: " + string.Join("; ", result.Findings.Select(f => f.Message)));
        Assert.Equal(0, Raises(result.ProposedOrder[0].CopyProgram()));
    }

    // Pierce in the slug, cut the square cutout, lead out into the slug, then rapid away.
    private static OwnedExecution SlugExit(bool raised)
    {
        var program = new Program(Mode.Absolute);
        program.Codes.Add(new RapidMove(4, 4));
        program.Codes.Add(new LinearMove(3, 4) { Layer = LayerType.Leadin });
        foreach (var point in new[] { new Vector(3, 3), new Vector(5, 3), new Vector(5, 5), new Vector(3, 5), new Vector(3, 4) })
            program.Codes.Add(new LinearMove(point) { Layer = LayerType.Cut });
        program.Codes.Add(new LinearMove(3.2, 4.2) { Layer = LayerType.Leadout });
        if (raised)
            program.Codes.Add(new Comment(HeadRaise.Marker));
        program.Codes.Add(new RapidMove(0, 8));
        return ExecutionMotionReader.Read(program, Vector.Zero, Vector.Zero, default);
    }

    [Fact]
    public void AHeadDownRapidOutOfASlugStillCrossesItsCutout()
    {
        var findings = new ReleasedContourState().Check(SlugExit(raised: false), Vector.Zero, 1);

        Assert.Contains(findings, f => f.Kind == PostVerificationKind.RapidCrossing);
    }

    [Fact]
    public void ARaisedRapidOutOfASlugIsTheDesignedWayOut()
    {
        var (strict, _, raised) = new ReleasedContourState()
            .Check(SlugExit(raised: true), Vector.Zero, 1, false, null, default);

        Assert.Empty(strict);
        Assert.Empty(raised);
    }

    [Fact]
    public void ARaisedRapidThatCrossesItsCutoutAgainIsNotASlugExit()
    {
        // A C-shaped cutout: the way out of its upper arm runs down across the lower arm too.
        var program = new Program(Mode.Absolute);
        program.Codes.Add(new RapidMove(4, 6.5));
        program.Codes.Add(new LinearMove(4, 7) { Layer = LayerType.Leadin });
        foreach (var point in new[]
        {
            new Vector(3, 7), new Vector(3, 6), new Vector(6, 6), new Vector(6, 4), new Vector(3, 4),
            new Vector(3, 3), new Vector(7, 3), new Vector(7, 7), new Vector(4, 7),
        })
            program.Codes.Add(new LinearMove(point) { Layer = LayerType.Cut });
        program.Codes.Add(new LinearMove(4.2, 6.5) { Layer = LayerType.Leadout });
        program.Codes.Add(new Comment(HeadRaise.Marker));
        program.Codes.Add(new RapidMove(4.2, 2));
        var execution = ExecutionMotionReader.Read(program, Vector.Zero, Vector.Zero, default);

        var (_, _, raised) = new ReleasedContourState().Check(execution, Vector.Zero, 1, false, null, default);

        Assert.Contains(raised, f => f.Kind == PostVerificationKind.RapidCrossing);
    }
}
