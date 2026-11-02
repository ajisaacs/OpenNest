using Clipper2Lib;
using OpenNest.CNC;
using OpenNest.Engine.Jobs;
using OpenNest.Engine.Jobs.Cutouts;
using OpenNest.Engine.Tests.NestingEngines;
using Xunit.Abstractions;
using TestShapes = OpenNest.Engine.Tests.NestingEngines.Shapes;

namespace OpenNest.Engine.Tests.Jobs.Cutouts;

/// <summary>A rectangular hole solved as a knapsack of boxes, then gap-filled.</summary>
public class CutoutBoxProposalTests
{
    private const double Spacing = 0.25;
    private const double HoleWidth = 22.25;
    private const double HoleHeight = 14.85;

    private readonly ITestOutputHelper output;

    public CutoutBoxProposalTests(ITestOutputHelper output) => this.output = output;

    private static NestJobPart HoleFrame(double width = HoleWidth, double height = HoleHeight)
    {
        var program = TestShapes.Rectangle(width + 4, height + 4);
        program.MoveTo(2, 2);
        program.LineTo(2, 2 + height);
        program.LineTo(2 + width, 2 + height);
        program.LineTo(2 + width, 2);
        program.LineTo(2, 2);
        return JobBuilder.Part("frame", program, 1);
    }

    private static NestJobPart Box(string id, double w, double h, int count) =>
        JobBuilder.Part(id, TestShapes.Rectangle(w, h), count, RotationPolicy.Automatic);

    private static NestJobPart Wedge(string id, double w, double h, int count) =>
        JobBuilder.Part(id, TestShapes.Polyline((0, 0), (w, 0), (0, h)), count, RotationPolicy.Automatic);

    private static NestJobPart[] Rectangles() => new[]
    {
        Box("a", 19.12, 12, 1), Box("b", 16.81, 12.38, 1), Box("c", 15.86, 12.38, 1),
        Box("d", 18.5, 9.3, 4), Box("e", 14.62, 7.62, 1), Box("f", 17.88, 6.05, 2),
        Box("g", 13.75, 7.62, 1), Box("h", 16.81, 5.26, 1), Box("i", 21.5, 4.06, 8),
        Box("j", 21, 4.06, 1), Box("k", 12.62, 6.56, 2), Box("l", 15.62, 4.06, 2),
        Box("m", 13.5, 4.06, 2), Box("n", 11.75, 3, 1),
    };

    private static NestJobPart[] Triangles() => new[]
    {
        Wedge("t", 12.25, 6, 1), Wedge("u", 7.88, 7.88, 2), Wedge("v", 12.5, 3.9, 2),
        Wedge("w", 5, 5, 1), Wedge("x", 4.5, 4.5, 4), Wedge("y", 5.75, 2.75, 4),
    };

    private static double Area(IEnumerable<NestJobPlacement> poses, IEnumerable<NestJobPart> parts)
    {
        var geometry = parts.ToDictionary(p => p.Id, p => JobPartGeometry.Read(p.Geometry));
        return CutoutBoxProposal.OutlineArea(poses, id => geometry[id]);
    }

    private static void AssertClear(NestJobPart frame, IReadOnlyList<NestJobPart> parts,
        IReadOnlyList<NestJobPlacement> poses)
    {
        var geometry = parts.ToDictionary(p => p.Id, p => JobPartGeometry.Read(p.Geometry));
        var frameGeometry = JobPartGeometry.Read(frame.Geometry);
        var framePose = new NestJobPlacement(frame.Id, 0, 0, 0, 0);
        foreach (var pose in poses)
            Assert.True(NestLayoutCheck.Clears(frameGeometry, framePose, geometry[pose.PartId], pose, Spacing),
                $"{pose} meets the frame");
        for (var i = 0; i < poses.Count; i++)
            for (var j = i + 1; j < poses.Count; j++)
                Assert.True(NestLayoutCheck.Clears(geometry[poses[i].PartId], poses[i],
                    geometry[poses[j].PartId], poses[j], Spacing), $"{poses[i]} meets {poses[j]}");
        foreach (var part in parts)
        {
            var mine = poses.Where(p => p.PartId == part.Id).Select(p => p.InstanceIndex).ToArray();
            Assert.True(mine.Length <= part.Quantity, $"{part.Id} over demand");
            Assert.Equal(Enumerable.Range(0, mine.Length), mine);
        }
    }

    [Fact]
    public void RectanglesFillTheHoleLikeAKnapsack()
    {
        var frame = HoleFrame();
        var parts = Rectangles();
        var poses = CutoutBoxProposal.Fill(frame, 0, parts, Spacing);
        var area = Area(poses, parts);
        output.WriteLine($"rectangles: {area:F1} of {HoleWidth * HoleHeight:F1}, {string.Join(" ", poses.Select(p => p.PartId))}");
        AssertClear(frame, parts, poses);
        Assert.True(area >= 0.84 * HoleWidth * HoleHeight, $"seated {area:F1}");
    }

    [Fact]
    public void MixedInsertsFillTheHole()
    {
        var frame = HoleFrame();
        var parts = Rectangles().Concat(Triangles()).ToArray();
        var poses = CutoutBoxProposal.Fill(frame, 0, parts, Spacing);
        var area = Area(poses, parts);
        output.WriteLine($"mixed: {area:F1} of {HoleWidth * HoleHeight:F1}, {string.Join(" ", poses.Select(p => p.PartId))}");
        AssertClear(frame, parts, poses);
        Assert.True(area >= 0.84 * HoleWidth * HoleHeight, $"seated {area:F1}");
    }

    [Fact]
    public void TrianglesPairIntoBoxes()
    {
        var frame = HoleFrame(8.5, 8.5);
        var wedge = Wedge("t", 7.5, 7.5, 2);
        var poses = CutoutBoxProposal.Fill(frame, 0, new[] { wedge }, Spacing);
        Assert.Equal(2, poses.Count);
        AssertClear(frame, new[] { wedge }, poses);
    }

    [Fact]
    public void RouterTakesTheBoxProposalWhenItSeatsMoreArea()
    {
        var frame = HoleFrame();
        var parts = Rectangles();
        var routed = CutoutRouter.Fill(frame, 0, parts, Spacing);
        var boxed = CutoutBoxProposal.Fill(frame, 0, parts, Spacing);
        output.WriteLine($"router {Area(routed, parts):F1}, boxes {Area(boxed, parts):F1}");
        AssertClear(frame, parts, routed);
        Assert.True(Area(routed, parts) >= Area(boxed, parts) - 1e-6);
    }

    [Fact]
    public void SameInputGivesTheSameLayout()
    {
        var frame = HoleFrame();
        var parts = Rectangles();
        Assert.Equal(CutoutBoxProposal.Fill(frame, 0, parts, Spacing), CutoutBoxProposal.Fill(frame, 0, parts, Spacing));
    }

    [Fact]
    public void QuantityAndRotationPolicyAreRespected()
    {
        var frame = HoleFrame();
        var fixedBar = JobBuilder.Rectangle("bar", 4, 13, 3, RotationPolicy.Fixed(0));
        var poses = CutoutBoxProposal.Fill(frame, 0, new[] { fixedBar }, Spacing);
        Assert.Equal(3, poses.Count);
        Assert.All(poses, p => Assert.Equal(0, p.Rotation));
        AssertClear(frame, new[] { fixedBar }, poses);
    }

    [Fact]
    public void RoundHoleIsNotARectangle()
    {
        var frame = JobBuilder.Part("frame", TestShapes.Ring(30, 20), 1);
        Assert.Empty(CutoutBoxProposal.Fill(frame, 0, new[] { Box("a", 5, 5, 2) }, Spacing));
    }

    [Fact]
    public void RoundedCornersCostAFractionOfTheirRadius()
    {
        const double radius = 0.25;
        var hole = new PathD();
        for (var corner = 0; corner < 4; corner++)
        {
            var cx = corner is 0 or 3 ? 20 - radius : radius;
            var cy = corner < 2 ? 10 - radius : radius;
            for (var step = 0; step <= 12; step++)
            {
                var angle = (corner + step / 12.0) * System.Math.PI / 2;
                hole.Add(new PointD(cx + radius * System.Math.Cos(angle), cy + radius * System.Math.Sin(angle)));
            }
        }
        var rectangle = CutoutBoxProposal.InscribedRectangle(hole);
        Assert.NotNull(rectangle);
        Assert.InRange(rectangle.Value.W, 20 - 2 * 0.45 * radius, 20);
        Assert.InRange(rectangle.Value.H, 10 - 2 * 0.45 * radius, 10);
    }
}
