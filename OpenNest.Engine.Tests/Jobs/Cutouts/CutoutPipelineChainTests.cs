using OpenNest.CNC;
using OpenNest.Engine.Jobs;
using OpenNest.Engine.Jobs.Cutouts;
using OpenNest.Geometry;
using TestShapes = OpenNest.Engine.Tests.NestingEngines.Shapes;

namespace OpenNest.Engine.Tests.Jobs.Cutouts;

/// <summary>Round frames nest inside larger round frames: a flange's bore holds a washer whose
/// bore holds a smaller washer, and so on, as one bundle the engine places as its outer frame.</summary>
[Collection("Cutout pipeline registry")]
public class CutoutPipelineChainTests
{
    private const double Sheet = 42;

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

    // Each ring's bore holds the next part with room for the 0.25 spacing.
    private static NestPipelineRequest Request(params Program[] programs) =>
        new("stub",
            programs.Select((p, i) => new NestItem { Drawing = new Drawing($"d{i}", p), Quantity = 1 }).ToArray(),
            new[] { new NestPlateStock("sheet", new Size(Sheet, Sheet), 1, 0.25) });

    private static readonly Program[] Chain =
    {
        TestShapes.Ring(40, 30), TestShapes.Ring(28, 18), TestShapes.Ring(16, 8), TestShapes.Disc(3),
    };

    private static NestJobResult PlaceProxies(NestJob job)
    {
        var builder = new NestJobResultBuilder(job);
        builder.AddSheet(job.Plates[0], job.Parts.Where(p => p.Id.StartsWith("__cutout-proxy-"))
            .Select(p => (p.Id, Sheet / 2, Sheet / 2, p.Rotation.EnumerateAngles(1)[0])));
        var complete = job.Parts.All(p => p.Id.StartsWith("__cutout-proxy-"));
        return builder.Build(complete ? NestJobStopReason.Completed : NestJobStopReason.NoPlacementFound);
    }

    [Fact]
    public void FourLevelChainIsOneBundleThatCutsInnermostFirst()
    {
        var request = Request(Chain);
        var engine = new Stub(PlaceProxies);
        var result = NestPipeline.RunCutoutTrial(engine, "stub", request);

        var proxy = Assert.Single(Assert.Single(engine.Jobs).Parts);
        Assert.Equal("__cutout-proxy-part-0-0", proxy.Id);
        Assert.True(result.IsValid, string.Join("; ", result.Violations));
        Assert.Equal(NestJobStatus.Complete, result.Status);
        Assert.Equal(new[] { "part-3", "part-2", "part-1", "part-0" },
            Assert.Single(result.Raw.Plates).Placements.Select(p => p.PartId));
        // A single insert sits concentric at every level.
        Assert.All(result.Raw.Plates[0].Placements, p =>
            Assert.True(new Vector(p.X, p.Y).DistanceTo(new Vector(Sheet / 2, Sheet / 2)) < 1e-6));
    }

    [Fact]
    public void RegisteredEngineNestsTheWholeChainOnOneSheet()
    {
        var request = Request(Chain) with { EngineName = "Irregular" };
        var result = NestPipeline.Run(request);

        Assert.True(result.IsValid, string.Join("; ", result.Violations));
        Assert.Equal(NestJobStatus.Complete, result.Status);
        Assert.Equal(request.Items.Reverse().Select(i => i.Drawing),
            Assert.Single(result.Plates).Parts.Select(p => p.BaseDrawing));
    }

    [Fact]
    public void RingWithAnEmptyBoreIsAnInsert()
    {
        var engine = new Stub(PlaceProxies);
        var result = NestPipeline.RunCutoutTrial(engine, "stub", Request(Chain[0], Chain[1]));

        Assert.Single(Assert.Single(engine.Jobs).Parts);
        Assert.True(result.IsValid, string.Join("; ", result.Violations));
        Assert.Equal(new[] { "part-1", "part-0" },
            Assert.Single(result.Raw.Plates).Placements.Select(p => p.PartId));
    }

    [Fact]
    public void UnplacedOuterFrameReleasesItsWholeChain()
    {
        var request = Request(Chain);
        var result = NestPipeline.RunCutoutTrial(new Stub(job =>
            new NestJobResultBuilder(job).Build(NestJobStopReason.NoPlacementFound)), "stub", request);

        Assert.True(result.IsValid, string.Join("; ", result.Violations));
        Assert.All(result.Raw.Fulfillment, f => Assert.Equal((1, 0, 1), (f.Requested, f.Placed, f.Unplaced)));
        Assert.Equal(new[] {
                ("part-0", NestUnplacedReason.NoPlacementFound),
                ("part-1", NestUnplacedReason.FrameUnplaced),
                ("part-2", NestUnplacedReason.FrameUnplaced),
                ("part-3", NestUnplacedReason.FrameUnplaced) },
            result.Unplaced.Select(u => (u.PartId, u.Reason)).OrderBy(u => u.PartId));
    }

    [Fact]
    public void ChainsStopAtTheLevelLimit()
    {
        // Five levels: the outermost flange cannot take the four-level bundle, so it stays
        // independent and the engine places both.
        var programs = new[] { TestShapes.Ring(40, 34) }.Concat(new[] {
            TestShapes.Ring(33, 26), TestShapes.Ring(25, 18), TestShapes.Ring(17, 10), TestShapes.Disc(4.5) }).ToArray();
        var engine = new Stub(job => new NestJobResultBuilder(job).Build(NestJobStopReason.NoPlacementFound));
        var request = Request(programs) with
        {
            Stock = new[] { new NestPlateStock("sheet", new Size(Sheet, Sheet), 2, 0.25) },
        };
        NestPipeline.RunCutoutTrial(engine, "stub", request);

        var job = Assert.Single(engine.Jobs);
        Assert.Equal(new[] { "part-0", "__cutout-proxy-part-1-0" }, job.Parts.Select(p => p.Id));
        Assert.Equal(CutoutPipelinePrepass.MaxLevels, programs.Length - 1);
    }

    [Fact]
    public void DifferentPriorityStaysIndependentAtEveryLevel()
    {
        var request = Request(Chain);
        request.Items[3].Priority = 1;
        var engine = new Stub(job => new NestJobResultBuilder(job).Build(NestJobStopReason.NoPlacementFound));
        NestPipeline.RunCutoutTrial(engine, "stub", request);

        Assert.Equal(new[] { "__cutout-proxy-part-0-0", "part-3" },
            Assert.Single(engine.Jobs).Parts.Select(p => p.Id));
    }
}
