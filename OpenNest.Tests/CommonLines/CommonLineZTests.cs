using OpenNest.CNC;
using OpenNest.CNC.CuttingPlanning;
using OpenNest.CommonLines;
using OpenNest.Diagnostics;
using OpenNest.Engine.CuttingPlanning;
using OpenNest.Geometry;
using static OpenNest.Tests.CommonLines.CommonLinePlanningTests;

namespace OpenNest.Tests.CommonLines;

/// <summary>
/// A Z-shaped common line (docs/common-line-cutting.md): two stepped bars, the second turned half
/// a turn, interlock one kerf apart along three straight legs. The legs' spans chain at their
/// corners into one shared path. The part cut first starts its perimeter on it, entered along its
/// first leg from the scrap, and cuts the whole Z straight into the rest of its perimeter.
/// </summary>
public class CommonLineZTests
{
    private const double K = CommonLinePlanningTests.Kerf;
    private const double Length = 12;
    private const double Step = 5;
    private const double Low = 0.5;
    private const double High = 1;

    // A bar Length long, Low tall for its first Step and High tall after it.
    private static Drawing SteppedBar()
    {
        var program = new Program();
        program.Codes.Add(new RapidMove(0, 0));
        program.Codes.Add(new LinearMove(0, Low));
        program.Codes.Add(new LinearMove(Step, Low));
        program.Codes.Add(new LinearMove(Step, High));
        program.Codes.Add(new LinearMove(Length, High));
        program.Codes.Add(new LinearMove(Length, 0));
        program.Codes.Add(new LinearMove(0, 0));
        return new Drawing("Terminal", program);
    }

    // The bar at (x, y), and its half-turned copy interlocked over its low end, offset from one
    // kerf apart by (dx, dy).
    private static (Plate Plate, Part A, Part B) Pair(double dx = 0, double dy = 0, bool join = true)
    {
        var plate = new Plate(48, 120) { PartSpacing = 0.25 };
        var drawing = SteppedBar();
        var a = new Part(drawing, new Vector(2, 2));
        var b = new Part(drawing);
        b.Rotate(System.Math.PI);
        b.Offset(2 + Step - K + dx - b.Location.X, 2 + High + K + dy - b.Location.Y);
        plate.Parts.Add(a);
        plate.Parts.Add(b);
        if (join)
            plate.JoinCommonLine([a, b], K);
        return (plate, a, b);
    }

    [Fact]
    public void Spans_OfTheZ_ChainIntoOnePathThroughItsCorners()
    {
        var (plate, _, _) = Pair();
        var check = CommonLineGeometry.Check(plate.CommonLines[0]);
        Assert.True(check.IsIntact);
        Assert.Equal(3, check.Spans.Count);

        var path = Assert.Single(CommonLineGeometry.Paths(check.Spans, K));

        Assert.Equal(3, path.Spans.Count);
        Assert.Equal(4, path.Points.Count);
        // The corners lie where the legs' centerlines meet, half a kerf off both parts.
        var corners = path.Points.Skip(1).Take(2).OrderBy(p => p.X).ToArray();
        Assert.Equal(2 - K / 2, corners[0].X, 9);
        Assert.Equal(2 + Low + K / 2, corners[0].Y, 9);
        Assert.Equal(2 + Step - K / 2, corners[1].X, 9);
        Assert.Equal(2 + Low + K / 2, corners[1].Y, 9);
        Assert.Equal(Step + 2 * (Low - K / 2), path.Length, 9);
    }

    [Fact]
    public void SeparateStraightSpans_StayPathsOfTheirOwn()
    {
        var plate = Row(3);
        var check = CommonLineGeometry.Check(plate.CommonLines[0]);

        var paths = CommonLineGeometry.Paths(check.Spans, K);

        Assert.Equal(2, paths.Count);
        Assert.All(paths, path => Assert.Equal(2, path.Points.Count));
    }

    [Fact]
    public void Join_ClosesEveryLegOfTheZ()
    {
        var (plate, a, b) = Pair(dx: 0.06, dy: 0.1, join: false);

        var result = CommonLineJoiner.Join(plate, [a, b], K, 1);

        Assert.True(result.Succeeded, result.Failure);
        var check = CommonLineGeometry.Check(result.Group);
        Assert.True(check.IsIntact);
        Assert.Equal(3, check.Spans.Count);
        Assert.Single(CommonLineGeometry.Paths(check.Spans, K));
    }

    [Fact]
    public void Plan_TheFirstPartStartsOnTheZAndRunsStraightIntoItsPerimeter()
    {
        var (plate, a, _) = Pair();

        var result = Plan(plate);

        Assert.True(result.Status == CuttingPlanStatus.Ready, Describe(result));
        Assert.True(result.IndependentlyReplayed);
        Assert.DoesNotContain(result.ProposedOrder.SelectMany(Motions), m => m.Layer == LayerType.CommonLine);
        var owner = result.ProposedOrder[0];
        Assert.Same(a, owner.SourcePart);
        var motions = Motions(owner).ToList();
        // One lead-in, up the Z's first leg from below, then its three legs in a row.
        var lead = Assert.Single(motions, m => m.Layer == LayerType.Leadin);
        Assert.Equal(2, lead.Start!.Value.X, 9);
        Assert.Equal(2, lead.End.X, 9);
        Assert.Equal(2, lead.End.Y, 9);
        var cuts = motions.Where(IsCut).ToList();
        Assert.Equal(new Vector(2, 2 + Low), cuts[0].End);
        Assert.Equal(new Vector(2 + Step, 2 + Low), cuts[1].End);
        Assert.Equal(new Vector(2 + Step, 2 + High), cuts[2].End);
        Assert.Equal(0, cuts[^1].End.DistanceTo(lead.End), 9);
        Assert.DoesNotContain(motions, m => m.Layer == LayerType.Leadout);
        // The partner cuts only its remainder.
        var partner = Motions(result.ProposedOrder[1]).Where(IsCut).Sum(m => m.Length);
        Assert.True(partner < 2 * (Length + High) - (Step + 2 * Low) + 2 * K);
    }

    private static bool IsCut(ExecutionMotion m) => !m.Rapid && m.Layer is LayerType.Cut or LayerType.Display;

    [Fact]
    public void Members_CutTheirPerimeterExactlyOnce_TheZByTheFirstPart()
    {
        var (plate, _, _) = Pair();
        var order = CommonLineCutting.Order(plate.CommonLines[0], CommonLineGeometry.Check(plate.CommonLines[0]));
        var result = Plan(plate);
        Assert.True(result.Status == CuttingPlanStatus.Ready, Describe(result));

        foreach (var proposal in result.ProposedOrder)
        {
            var part = proposal.SourcePart;
            var cutBefore = order.PathsCutBefore(part).ToArray();
            var marked = CommonLineCutting.MarkSharedSpans(part.Program, part.Location, cutBefore, K);
            // The partner's Z is one tab run: its legs join at its own corners.
            var layers = marked.Codes.OfType<LinearMove>().Select(m => m.Layer).ToList();
            Assert.Equal(cutBefore.Length, layers.Where((layer, i) => layer == LayerType.Tab
                && layers[(i + layers.Count - 1) % layers.Count] != LayerType.Tab).Count());
            var material = LeadMaterialSnapshot.Capture(marked, part.Location, default, sharedSpans: true);
            Assert.True(material.IsComplete, material.Reason);
            var planned = ExecutionMotionReader.Read(proposal.CopyProgram(), part.Location, null, default);
            Assert.True(ContourProgramVerifier.Verify(planned, material, null, default));
        }
    }

    [Fact]
    public void Applied_PassesPrePostReview()
    {
        var nest = new Nest("Z");
        var (plate, _, _) = Pair();
        plate.CuttingParameters = Parameters();
        nest.Plates.Add(plate);
        var result = Plan(plate);
        Assert.True(result.Status == CuttingPlanStatus.Ready, Describe(result));
        Assert.Equal(CuttingCommitStatus.Applied, CuttingPlanService.Apply([result]).Status);

        var findings = PostVerificationAnalyzer.Analyze(nest).Findings;

        Assert.DoesNotContain(findings, f => f.Kind is PostVerificationKind.CommonLine or PostVerificationKind.Incomplete
            or PostVerificationKind.RapidCrossing or PostVerificationKind.MissingLeadIn or PostVerificationKind.PartSpacing);
    }
}
