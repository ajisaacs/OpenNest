using OpenNest.CNC;
using OpenNest.Engine.Jobs;
using OpenNest.Geometry;
using TestShapes = OpenNest.Engine.Tests.NestingEngines.Shapes;

namespace OpenNest.Engine.Tests.Jobs.Cutouts;

/// <summary>An insert with holes of its own goes into a frame's cutout when none of its holes
/// fits any part of the job (bolt holes in a strap); otherwise it stays independent, since a
/// later insert could land in its hole.</summary>
[Collection("Cutout pipeline registry")]
public class CutoutPipelineHoledInsertTests
{
    private sealed class Stub(Func<NestJob, NestJobResult> solve) : INestingEngine
    {
        public List<NestJob> Jobs { get; } = new();

        public NestJobResult Solve(NestJob job, IProgress<NestJobProgress>? progress = null,
            CancellationToken token = default)
        {
            Jobs.Add(job);
            return solve(job);
        }
    }

    // A 20 x 20 frame with a 12 x 14 window.
    private static Program Frame() => WithSquareHoles(TestShapes.Rectangle(20, 20), (4, 3, 12, 14));

    // A 13 x 1.25 strap with two 0.25 bolt holes: it fits the window only upright.
    private static Program Strap()
    {
        var program = TestShapes.Rectangle(13, 1.25);
        foreach (var x in new[] { 1.0, 12.0 })
        {
            program.Codes.Add(new RapidMove(x + 0.125, 0.625));
            program.Codes.Add(new ArcMove(x - 0.125, 0.625, x, 0.625, RotationType.CCW));
            program.Codes.Add(new ArcMove(x + 0.125, 0.625, x, 0.625, RotationType.CCW));
        }
        return program;
    }

    private static Program WithSquareHoles(Program program, params (double X, double Y, double W, double H)[] holes)
    {
        foreach (var (x, y, w, h) in holes)
        {
            program.Codes.Add(new RapidMove(x, y));
            program.Codes.Add(new LinearMove(x + w, y));
            program.Codes.Add(new LinearMove(x + w, y + h));
            program.Codes.Add(new LinearMove(x, y + h));
            program.Codes.Add(new LinearMove(x, y));
        }
        return program;
    }

    private static NestPipelineRequest Request(string engine, params Program[] programs) =>
        new(engine,
            programs.Select((p, i) => new NestItem { Drawing = new Drawing($"d{i}", p), Quantity = 1 }).ToArray(),
            // Only the frame fits beside nothing else: the strap must go in its window.
            new[] { new NestPlateStock("sheet", new Size(20.5, 20.5), 1, 0.25) });

    [Theory]
    [InlineData("Irregular")]
    [InlineData("Default")]
    public void StrapWithBoltHolesGoesInTheFrameWindow(string engine)
    {
        var request = Request(engine, Frame(), Strap());
        Assert.Equal(NestJobStatus.Incomplete, NestPipeline.RunWithoutCutouts(request).Status);

        var result = NestPipeline.Run(request);

        Assert.True(result.IsValid, string.Join("; ", result.Violations));
        Assert.Equal(NestJobStatus.Complete, result.Status);
        Assert.Equal(new[] { request.Items[1].Drawing, request.Items[0].Drawing },
            Assert.Single(result.Plates).Parts.Select(p => p.BaseDrawing));
    }

    [Fact]
    public void InsertWhoseHoleFitsAnotherPartStaysIndependent()
    {
        // The 10 x 10 part's 4 x 4 hole fits the 2 x 2 part, so it is never reserved in the
        // frame's window; the 2 x 2 part goes into that hole, the smaller frame, instead.
        var holed = WithSquareHoles(TestShapes.Rectangle(10, 10), (3, 3, 4, 4));
        var engine = new Stub(job => new NestJobResultBuilder(job).Build(NestJobStopReason.NoPlacementFound));
        NestPipeline.RunCutoutTrial(engine, "stub", Request("stub", Frame(), holed, TestShapes.Rectangle(2, 2)));

        Assert.Equal(new[] { "part-0", "__cutout-proxy-part-1-0" },
            Assert.Single(engine.Jobs).Parts.Select(p => p.Id));
    }
}
