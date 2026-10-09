using OpenNest.CNC;
using OpenNest.Engine.Jobs;
using OpenNest.Engine.Jobs.Cutouts;
using OpenNest.Geometry;
using TestShapes = OpenNest.Engine.Tests.NestingEngines.Shapes;

namespace OpenNest.Engine.Tests.Jobs.Cutouts;

public class CutoutPipelinePreviewTests
{
    private static NestPipelineRequest Request(string engine = "Irregular")
    {
        var frame = new Drawing("frame", TestShapes.Ring(20, 10));
        var insert = new Drawing("insert", TestShapes.Rectangle(3, 3));
        return new NestPipelineRequest(engine,
            new[] { new NestItem { Drawing = frame, Quantity = 1 },
                new NestItem { Drawing = insert, Quantity = 1 } },
            new[] { new NestPlateStock("sheet", new Size(22, 22), 1, 0.25) });
    }

    [Theory]
    [InlineData("Irregular")]
    [InlineData("Default")]
    public void RegisteredEngineExpandsOnlyPhysicalOriginalDrawings(string engine)
    {
        var request = Request(engine);
        var result = NestPipeline.RunCutoutPreview(request);
        Assert.True(result.IsValid, string.Join("; ", result.Violations));
        Assert.True(result.CanKeep);
        var plate = Assert.Single(result.Plates);
        Assert.Equal(2, plate.Parts.Count);
        Assert.Same(request.Items[0].Drawing, plate.Parts[0].BaseDrawing);
        Assert.Same(request.Items[1].Drawing, plate.Parts[1].BaseDrawing);
        Assert.Equal(new[] { "part-0", "part-1" }, result.Raw.Plates[0].Placements.Select(p => p.PartId));
        Assert.All(result.Raw.Fulfillment, f => Assert.Equal((1, 1, 0), (f.Requested, f.Placed, f.Unplaced)));
        Assert.Equal(0, request.Items[0].Drawing.Quantity.Nested);
        Assert.Equal(0, request.Items[1].Drawing.Quantity.Nested);
    }

    private sealed class Stub(Func<NestJob, NestJobResult> solve) : INestingEngine
    {
        public NestJobResult Solve(NestJob job, IProgress<NestJobProgress>? progress = null,
            CancellationToken token = default) => solve(job);
    }

    private static NestJobResult Result(NestJob job, params NestJobPlacement[] poses)
    {
        var placed = poses.GroupBy(p => p.PartId).ToDictionary(g => g.Key, g => g.Count());
        var complete = job.Parts.All(p => placed.GetValueOrDefault(p.Id) == p.Quantity);
        return new NestJobResult(complete ? NestJobStatus.Complete : NestJobStatus.Incomplete,
            complete ? NestJobStopReason.Completed : NestJobStopReason.NoPlacementFound,
            poses.Length == 0 ? Array.Empty<NestJobPlateResult>() :
                new[] { new NestJobPlateResult(0, job.Plates[0], poses) },
            job.Parts.Select(p => new PartFulfillment(p.Id, p.Quantity,
                placed.GetValueOrDefault(p.Id), p.Quantity - placed.GetValueOrDefault(p.Id))),
            new[] { new StockUsage(job.Plates[0].Id, poses.Length == 0 ? 0 : 1,
                job.Plates[0].Quantity - (poses.Length == 0 ? 0 : 1)) });
    }

    [Fact]
    public void SingleSolveReceivesOneProxyAndNoDuplicatedInsertDemand()
    {
        var called = 0;
        var request = Request("stub");
        var result = NestPipeline.RunCutoutPreview(new Stub(job =>
        {
            called++;
            var proxy = Assert.Single(job.Parts);
            Assert.StartsWith("__cutout-proxy-", proxy.Id);
            Assert.True(proxy.Rotation.Allows(0));
            Assert.False(proxy.Rotation.Allows(System.Math.PI / 2));
            return Result(job, new NestJobPlacement(proxy.Id, 0, 11, 11, 0));
        }), "stub", request);
        Assert.Equal(1, called);
        Assert.True(result.IsValid, string.Join("; ", result.Violations));
        Assert.Equal(2, result.Plates.Single().Parts.Count);
    }

    [Fact]
    public void IncompleteEngineDoesNotBindReservedInsertEvenWithInvalidConsent()
    {
        var request = Request("stub");
        var result = NestPipeline.RunCutoutPreview(new Stub(job => Result(job)), "stub", request);
        Assert.False(result.CanKeep);
        Assert.Empty(result.Plates);
        Assert.Contains(result.Violations, v => v.Contains("did not complete"));
        Assert.Equal(0, request.Items[1].Drawing.Quantity.Nested);
        AssertCannotCommit(result, request);
    }

    [Fact]
    public void ForgedFulfillmentOrDuplicateIndexDoesNotBindComposite()
    {
        var request = Request("stub");
        var result = NestPipeline.RunCutoutPreview(new Stub(job =>
        {
            var id = job.Parts.Single(p => p.Id.StartsWith("__cutout-proxy-")).Id;
            var pose = new NestJobPlacement(id, 1, 11, 11, 0);
            return Result(job, pose);
        }), "stub", request);
        Assert.False(result.CanKeep);
        Assert.Empty(result.Plates);
        Assert.Contains(result.Violations, v => v.Contains("accounting"));
    }

    [Fact]
    public void PartialBundleKeepsIndependentResidualByOriginalRequirement()
    {
        var request = Request("stub");
        request.Items[1].Quantity = 2;
        request = request with { Stock = new[] { new NestPlateStock("sheet", new Size(26, 26), 1, 0.25) } };
        var result = NestPipeline.RunCutoutPreview(new Stub(job =>
        {
            var proxy = job.Parts.Single(p => p.Id.StartsWith("__cutout-proxy-"));
            var residual = job.Parts.Single(p => p.Id == "part-1");
            Assert.Equal(1, residual.Quantity);
            return Result(job, new NestJobPlacement(proxy.Id, 0, 11, 11, 0),
                new NestJobPlacement(residual.Id, 0, 22, 1, 0));
        }), "stub", request);
        Assert.True(result.IsValid, string.Join("; ", result.Violations));
        Assert.Equal(new[] { "part-0", "part-1", "part-1" },
            result.Raw.Plates.Single().Placements.Select(p => p.PartId));
        Assert.Equal(new[] { 0, 1 }, result.Raw.Plates.Single().Placements
            .Where(p => p.PartId == "part-1").Select(p => p.InstanceIndex));
        Assert.All(result.Plates.Single().Parts.Where(p => p.BaseDrawing == request.Items[1].Drawing),
            p => Assert.Same(request.Items[1].Drawing, p.BaseDrawing));
    }

    [Fact]
    public void UntrustedStockOverdrawOrRotationCannotBindAComposite()
    {
        var request = Request("stub");
        var result = NestPipeline.RunCutoutPreview(new Stub(job =>
        {
            var proxy = job.Parts.Single(p => p.Id.StartsWith("__cutout-proxy-"));
            return Result(job, new NestJobPlacement(proxy.Id, 0, 11, 11,
                System.Math.PI / 2));
        }), "stub", request);
        Assert.False(result.CanKeep);
        Assert.Empty(result.Plates);
        Assert.Contains(result.Violations, v => v.Contains("rotation constraint"));

        var overdraw = NestPipeline.RunCutoutPreview(new Stub(job => new NestJobResult(
            NestJobStatus.Complete, NestJobStopReason.Completed,
            new[] { new NestJobPlateResult(0, job.Plates[0], new[] {
                    new NestJobPlacement(job.Parts[0].Id, 0, 11, 11, 0) }),
                new NestJobPlateResult(1, job.Plates[0], new[] {
                    new NestJobPlacement(job.Parts[0].Id, 1, 11, 11, 0) }) },
            Array.Empty<PartFulfillment>(), Array.Empty<StockUsage>())), "stub", request);
        Assert.False(overdraw.CanKeep);
        Assert.Empty(overdraw.Plates);
        Assert.Contains(overdraw.Violations, v => v.Contains("only 1 are available"));
    }

    [Fact]
    public void CancellationBeforePreparationDoesNotCallEngine()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var engine = new Stub(_ => throw new Exception("Must not call Solve"));
        Assert.Throws<OperationCanceledException>(() =>
            NestPipeline.RunCutoutPreview(engine, "stub", Request("stub"), token: cts.Token));
    }

    [Fact]
    public void EnvelopeValidResidualInsideReservedHoleFailsAfterPhysicalExpansion()
    {
        var request = Request("stub");
        request.Items[1].Quantity = 2;
        var result = NestPipeline.RunCutoutPreview(new Stub(job =>
        {
            var proxy = job.Parts.Single(p => p.Id.StartsWith("__cutout-proxy-"));
            return Result(job, new NestJobPlacement(proxy.Id, 0, 11, 11, 0),
                new NestJobPlacement("part-1", 0, 11, 11, 0));
        }), "stub", request);
        Assert.False(result.CanKeep);
        Assert.Empty(result.Plates);
        Assert.Contains(result.Violations, v => v.Contains("required spacing"));
        AssertCannotCommit(result, request);
    }

    private static void AssertCannotCommit(NestPipelineResult result, NestPipelineRequest request)
    {
        var nest = new Nest();
        foreach (var item in request.Items)
            nest.Drawings.Add(item.Drawing);
        var empty = nest.CreatePlate();
        using var manager = new PlateManager(nest);
        Assert.Throws<InvalidOperationException>(() =>
            NestPipelineCommit.ApplyToEmptyPlates(result, manager, allowInvalid: true));
        Assert.Same(empty, Assert.Single(nest.Plates));
        Assert.Empty(empty.Parts);
        Assert.All(request.Items, item => Assert.Equal(0, item.Drawing.Quantity.Nested));
    }

    private sealed class CollectProgress : IProgress<NestJobProgress>
    {
        public List<NestJobProgress> Events { get; } = new();
        public void Report(NestJobProgress value) => Events.Add(value);
    }

    [Fact]
    public void EvaluationProgressSurvivesButUnverifiedCommitsNeverEscape()
    {
        var request = Request("stub");
        var progress = new CollectProgress();
        var result = NestPipeline.RunCutoutPreview(new ProgressStub(), "stub", request, progress);
        Assert.False(result.CanKeep);
        Assert.Single(progress.Events);
        Assert.Equal(NestJobStage.EvaluatingCandidate, progress.Events[0].Stage);
        Assert.Equal(0, progress.Events[0].CommittedPlates);
        Assert.Equal(0, progress.Events[0].CommittedParts);
    }

    private sealed class ProgressStub : INestingEngine
    {
        public NestJobResult Solve(NestJob job, IProgress<NestJobProgress>? progress = null,
            CancellationToken token = default)
        {
            progress?.Report(new NestJobProgress(NestJobStage.EvaluatingCandidate,
                job.Plates[0].Id, 0, 1, 1));
            progress?.Report(new NestJobProgress(NestJobStage.PlateCommitted,
                job.Plates[0].Id, 0, 1, 1));
            return Result(job);
        }
    }

    [Fact]
    public void ReorderedButContiguousEngineIndicesAreAccepted()
    {
        var request = Request("stub");
        request.Items[1].Quantity = 3;
        request.Items[1].Drawing.Program = TestShapes.Rectangle(6, 6);
        request = request with { Stock = new[] { new NestPlateStock("sheet", new Size(30, 30), 1, 0.25) } };
        var result = NestPipeline.RunCutoutPreview(new Stub(job =>
        {
            var proxy = job.Parts.Single(p => p.Id.StartsWith("__cutout-proxy-"));
            Assert.Equal(2, job.Parts.Single(p => p.Id == "part-1").Quantity);
            return Result(job, new NestJobPlacement(proxy.Id, 0, 11, 11, 0),
                new NestJobPlacement("part-1", 1, 22, 1, 0),
                new NestJobPlacement("part-1", 0, 22, 9, 0));
        }), "stub", request);
        Assert.True(result.IsValid, string.Join("; ", result.Violations));
        Assert.Equal(new[] { 0, 1, 2 }, result.Raw.Plates.Single().Placements
            .Where(p => p.PartId == "part-1").Select(p => p.InstanceIndex));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ForgedFulfillmentOrStockMetadataCannotBind(bool fulfillment)
    {
        var request = Request("stub");
        var result = NestPipeline.RunCutoutPreview(new Stub(job =>
        {
            var proxy = job.Parts.Single(p => p.Id.StartsWith("__cutout-proxy-"));
            var good = Result(job, new NestJobPlacement(proxy.Id, 0, 11, 11, 0));
            return new NestJobResult(good.Status, good.StopReason, good.Plates,
                fulfillment ? new[] { new PartFulfillment(proxy.Id, 1, 0, 1) } : good.Fulfillment,
                fulfillment ? good.StockUsage : new[] { new StockUsage("sheet", 0, 1) });
        }), "stub", request);
        Assert.False(result.CanKeep);
        Assert.Empty(result.Plates);
        Assert.Contains(result.Violations, v => v.Contains("accounting"));
    }

    [Fact]
    public void SameDrawingInTwoRequirementRowsRetainsSeparateIds()
    {
        var initial = Request("stub");
        var request = initial with
        {
            Items = new[] { initial.Items[0], initial.Items[1],
            new NestItem { Drawing = initial.Items[1].Drawing, Quantity = 1 } },
            Stock = new[] { new NestPlateStock("sheet", new Size(30, 30), 1, 0.25) }
        };
        var result = NestPipeline.RunCutoutPreview(new Stub(job =>
        {
            var proxy = job.Parts.Single(p => p.Id.StartsWith("__cutout-proxy-"));
            var residual = job.Parts.Where(p => p.Id != proxy.Id)
                .Select((p, i) => new NestJobPlacement(p.Id, 0, 22, 1 + 5 * i, 0));
            return Result(job, new[] { new NestJobPlacement(proxy.Id, 0, 11, 11, 0) }
                .Concat(residual).ToArray());
        }), "stub", request);
        Assert.True(result.IsValid, string.Join("; ", result.Violations));
        Assert.Equal(new[] { "part-0", "part-1", "part-2" },
            result.Raw.Plates.Single().Placements.Select(p => p.PartId));
        Assert.Same(result.Plates.Single().Parts[1].BaseDrawing,
            result.Plates.Single().Parts[2].BaseDrawing);
    }

    [Fact]
    public void OrdinaryPipelineIsStillUnchanged()
    {
        var request = Request("stub");
        var result = NestPipeline.Run(new Stub(job =>
        {
            Assert.DoesNotContain(job.Parts, p => p.Id.StartsWith("__cutout-proxy-"));
            return Result(job);
        }), "stub", request);
        Assert.True(result.CanKeep);
        Assert.Empty(result.Plates);
    }
}
