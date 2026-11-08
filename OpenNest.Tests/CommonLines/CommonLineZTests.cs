using OpenNest.CNC;
using OpenNest.CNC.CuttingPlanning;
using OpenNest.Diagnostics;
using OpenNest.Engine.CuttingPlanning;
using OpenNest.Geometry;
using static OpenNest.Tests.CommonLines.CommonLinePlanningTests;

namespace OpenNest.Tests.CommonLines;

/// <summary>
/// A Z-shaped common line (docs/common-line-cutting.md): two stepped bars, the second turned half
/// a turn, interlock one kerf apart along three straight legs. The legs' spans chain at their
/// corners into one shared path, cut once as one open centerline cut.
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
    public void Plan_CutsTheZOnceAsOneOpenPath()
    {
        var (plate, _, _) = Pair();

        var result = Plan(plate);

        Assert.True(result.Status == CuttingPlanStatus.Ready, Describe(result));
        Assert.True(result.IndependentlyReplayed);
        var owner = result.ProposedOrder[0];
        var motions = Motions(owner).ToList();
        var shared = motions.Where(m => m.Layer == LayerType.CommonLine).ToList();
        Assert.Equal(3, shared.Count);
        // One pierce: the three legs are cut in a row, each starting where the last ended.
        var first = motions.IndexOf(shared[0]);
        Assert.Equal(LayerType.Leadin, motions[first - 1].Layer);
        for (var i = 1; i < shared.Count; i++)
        {
            Assert.Same(shared[i], motions[first + i]);
            Assert.Equal(0, shared[i - 1].End.DistanceTo(shared[i].Curve.Start), 9);
        }
        Assert.Equal(Step + 2 * (Low - K / 2), shared.Sum(m => m.Length), 9);
        // The partner cuts none of it.
        Assert.DoesNotContain(Motions(result.ProposedOrder[1]), m => m.Layer == LayerType.CommonLine);
    }

    [Fact]
    public void Members_CutTheirPerimeterExactlyOnce_TheZByTheSharedCut()
    {
        var (plate, _, _) = Pair();
        var order = CommonLineCutting.Order(plate.CommonLines[0], CommonLineGeometry.Check(plate.CommonLines[0]));
        var result = Plan(plate);
        Assert.True(result.Status == CuttingPlanStatus.Ready, Describe(result));

        foreach (var proposal in result.ProposedOrder)
        {
            var part = proposal.SourcePart;
            var marked = CommonLineCutting.MarkSharedSpans(part.Program, part.Location, order.PathsOf(part).ToArray(), K);
            // The Z is one tab run on each member: its legs join at the member's own corners.
            var layers = marked.Codes.OfType<LinearMove>().Select(m => m.Layer).ToList();
            Assert.Single(layers.Where((layer, i) => layer == LayerType.Tab
                && layers[(i + layers.Count - 1) % layers.Count] != LayerType.Tab));
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
