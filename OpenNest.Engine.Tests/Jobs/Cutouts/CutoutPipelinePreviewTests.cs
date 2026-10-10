using OpenNest.CNC;
using OpenNest.Engine.Jobs;
using OpenNest.Engine.Jobs.Cutouts;
using OpenNest.Geometry;
using TestShapes = OpenNest.Engine.Tests.NestingEngines.Shapes;

namespace OpenNest.Engine.Tests.Jobs.Cutouts;

[CollectionDefinition("Cutout pipeline registry", DisableParallelization = true)]
public sealed class CutoutPipelineRegistryCollection { }

[Collection("Cutout pipeline registry")]
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
        Assert.All(result.Raw.Fulfillment, f => Assert.Equal((1, 0, 1),
            (f.Requested, f.Placed, f.Unplaced)));
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
    public void RepeatedFrameReservationsExpandOnTwoPhysicalSheets()
    {
        var request = Request("stub");
        request.Items[0].Quantity = 2;
        request.Items[1].Quantity = 2;
        request.Items[1].Drawing.Program = TestShapes.Rectangle(6, 6);
        request = request with
        {
            Stock = new[] { new NestPlateStock("sheet", new Size(22, 22), 2, 0.25) },
            Options = new NestJobOptions(maxPlates: 2)
        };
        var calls = 0;
        var result = NestPipeline.RunCutoutPreview(new Stub(job =>
        {
            calls++;
            var proxies = job.Parts.Where(p => p.Id.StartsWith("__cutout-proxy-")).ToArray();
            Assert.Equal(2, proxies.Length);
            Assert.Equal(2, job.Parts.Count);
            var builder = new NestJobResultBuilder(job);
            foreach (var proxy in proxies)
                builder.AddSheet(job.Plates[0], new[] { (proxy.Id, 11.0, 11.0, 0.0) });
            return builder.Build(NestJobStopReason.Completed);
        }), "stub", request);
        Assert.Equal(1, calls);
        Assert.True(result.IsValid, string.Join("; ", result.Violations));
        Assert.Equal(2, result.Raw.Plates.Count);
        Assert.All(result.Raw.Plates, plate => Assert.Equal(new[] { "part-0", "part-1" },
            plate.Placements.Select(p => p.PartId)));
        Assert.All(result.Raw.Fulfillment, f => Assert.Equal((2, 2, 0),
            (f.Requested, f.Placed, f.Unplaced)));
        Assert.Equal(new[] { 0, 1 }, result.Raw.Plates.SelectMany(p => p.Placements)
            .Where(p => p.PartId == "part-1").Select(p => p.InstanceIndex));
    }

    [Fact]
    public void UnplacedFrameReleasesInsertDemandWithoutABindablePartialProposal()
    {
        var request = Request("stub");
        request.Items[0].Quantity = 2;
        request.Items[1].Quantity = 2;
        request.Items[1].Drawing.Program = TestShapes.Rectangle(6, 6);
        request = request with { Stock = new[] { new NestPlateStock("sheet", new Size(22, 22), 2, 0.25) } };
        var calls = 0;
        var result = NestPipeline.RunCutoutPreview(new Stub(job =>
        {
            calls++;
            var first = job.Parts.Single(p => p.Id.EndsWith("-0"));
            var builder = new NestJobResultBuilder(job);
            builder.AddSheet(job.Plates[0], new[] { (first.Id, 11.0, 11.0, 0.0) });
            return builder.Build(NestJobStopReason.NoPlacementFound);
        }), "stub", request);
        Assert.Equal(1, calls);
        Assert.False(result.CanKeep);
        Assert.Empty(result.Plates);
        Assert.Equal(NestJobStatus.Incomplete, result.Raw.Status);
        Assert.All(result.Raw.Fulfillment, f => Assert.Equal((2, 1, 1),
            (f.Requested, f.Placed, f.Unplaced)));
        Assert.Equal(new[] { "part-0", "part-1" }, result.Raw.Plates.Single().Placements.Select(p => p.PartId));
        AssertCannotCommit(result, request);
    }

    [Fact]
    public void DifferentFrameRequirementsShareScarceInsertsByIdentity()
    {
        var initial = Request("stub");
        var frame2 = new Drawing("second-frame", TestShapes.Ring(18, 10));
        initial.Items[1].Quantity = 2;
        initial.Items[1].Drawing.Program = TestShapes.Rectangle(6, 6);
        var request = initial with
        {
            Items = new[] { initial.Items[0],
            new NestItem { Drawing = frame2, Quantity = 1 }, initial.Items[1] },
            Stock = new[] { new NestPlateStock("sheet", new Size(22, 22), 2, 0.25) }
        };
        var result = NestPipeline.RunCutoutPreview(new Stub(job =>
        {
            var proxies = job.Parts.Where(p => p.Id.StartsWith("__cutout-proxy-")).ToArray();
            Assert.Equal(2, proxies.Length);
            Assert.Equal(2, job.Parts.Count);
            var builder = new NestJobResultBuilder(job);
            foreach (var proxy in proxies)
                builder.AddSheet(job.Plates[0], new[] { (proxy.Id, 11.0, 11.0, 0.0) });
            return builder.Build(NestJobStopReason.Completed);
        }), "stub", request);
        Assert.True(result.IsValid, string.Join("; ", result.Violations));
        Assert.Equal(new[] { "part-0", "part-2", "part-1", "part-2" },
            result.Raw.Plates.SelectMany(p => p.Placements).Select(p => p.PartId));
        Assert.Equal(new[] { 0, 1 }, result.Raw.Plates.SelectMany(p => p.Placements)
            .Where(p => p.PartId == "part-2").Select(p => p.InstanceIndex));
    }

    [Fact]
    public void FixedQuarterTurnComposesLocalInsertRotationAndTranslation()
    {
        var frame = TestShapes.Rectangle(20, 20);
        frame.MoveTo(5, 5);
        frame.LineTo(5, 15);
        frame.LineTo(15, 15);
        frame.LineTo(15, 5);
        frame.LineTo(5, 5);
        var quarter = System.Math.PI / 2;
        var request = new NestPipelineRequest("stub", new[] {
            new NestItem { Drawing = new Drawing("frame", frame), Quantity = 1,
                StepAngle = quarter, RotationStart = quarter, RotationEnd = quarter },
            new NestItem { Drawing = new Drawing("insert", TestShapes.Rectangle(3, 3)), Quantity = 1,
                StepAngle = quarter, RotationStart = quarter, RotationEnd = quarter } },
            new[] { new NestPlateStock("sheet", new Size(22, 22), 1, 0.25) });
        var result = NestPipeline.RunCutoutPreview(new Stub(job =>
        {
            var proxy = Assert.Single(job.Parts);
            Assert.True(proxy.Rotation.Allows(quarter));
            Assert.False(proxy.Rotation.Allows(0));
            return Result(job, new NestJobPlacement(proxy.Id, 0, 21, 1, quarter));
        }), "stub", request);
        Assert.True(result.IsValid, string.Join("; ", result.Violations));
        var poses = result.Raw.Plates.Single().Placements;
        Assert.Equal(quarter, poses[0].Rotation, 8);
        Assert.Equal(quarter, poses[1].Rotation, 8);
        // Independent square-hole oracle, not the production clearance predicate.
        var localX = poses[1].Y - poses[0].Y;
        var localY = -(poses[1].X - poses[0].X);
        foreach (var dx in new[] { 0.0, 3.0 })
            foreach (var dy in new[] { 0.0, 3.0 })
            {
                Assert.InRange(localX + dx, 5.25, 14.75);
                Assert.InRange(localY + dy, 5.25, 14.75);
            }
    }

    [Fact]
    public void HeterogeneousStockUsesConservativeSpacingAndOriginalQuadrant()
    {
        var request = Request("stub") with
        {
            Stock = new[] {
            new NestPlateStock("tight", new Size(22, 22), 1, 0.25),
            new NestPlateStock("wide", new Size(22, 22), 1, 0.6, quadrant: 3) }
        };
        var result = NestPipeline.RunCutoutPreview(new Stub(job =>
        {
            var proxy = Assert.Single(job.Parts);
            var builder = new NestJobResultBuilder(job);
            builder.AddSheet(job.Plates[1], new[] { (proxy.Id, -11.0, -11.0, 0.0) });
            return builder.Build(NestJobStopReason.Completed);
        }), "stub", request);
        Assert.True(result.IsValid, string.Join("; ", result.Violations));
        Assert.Equal("wide", result.Raw.Plates.Single().StockId);
        Assert.Equal(3, result.Plates.Single().Stock.Quadrant);
        Assert.Equal(0.6, result.Plates.Single().Stock.PartSpacing);
        Assert.Equal(2, result.Plates.Single().Parts.Count);
    }

    [Fact]
    public void RegisteredPluginRunsTheSamePhysicalExpansionBoundary()
    {
        var name = "CutoutPlugin-" + Guid.NewGuid().ToString("N");
        var calls = 0;
        NestingEngineRegistry.Register(name, "test plugin", () => new Stub(job =>
        {
            calls++;
            var proxy = Assert.Single(job.Parts);
            return Result(job, new NestJobPlacement(proxy.Id, 0, 11, 11, 0));
        }));
        try
        {
            var result = NestPipeline.RunCutoutPreview(Request(name));
            Assert.Equal(1, calls);
            Assert.True(result.IsValid, string.Join("; ", result.Violations));
            Assert.Equal(new[] { "part-0", "part-1" },
                result.Raw.Plates.Single().Placements.Select(p => p.PartId));
        }
        finally
        {
            var registry = Assert.IsType<List<NestingEngineInfo>>(NestingEngineRegistry.AvailableEngines);
            Assert.Equal(1, registry.RemoveAll(info => info.Name == name));
        }
    }

    [Fact]
    public void TwoCutoutsReserveDifferentCopiesWithinOneFrame()
    {
        var program = TestShapes.Rectangle(20, 20);
        foreach (var origin in new[] { 2.0, 12.0 })
        {
            program.MoveTo(origin, origin);
            program.LineTo(origin, origin + 6);
            program.LineTo(origin + 6, origin + 6);
            program.LineTo(origin + 6, origin);
            program.LineTo(origin, origin);
        }
        var frame = new Drawing("two-hole-frame", program);
        var insert = new Drawing("insert", TestShapes.Rectangle(4, 4));
        var request = new NestPipelineRequest("stub", new[] {
            new NestItem { Drawing = frame, Quantity = 1 },
            new NestItem { Drawing = insert, Quantity = 2 } },
            new[] { new NestPlateStock("sheet", new Size(22, 22), 1, 0.25) });
        var result = NestPipeline.RunCutoutPreview(new Stub(job =>
        {
            var proxy = Assert.Single(job.Parts);
            return Result(job, new NestJobPlacement(proxy.Id, 0, 1, 1, 0));
        }), "stub", request);
        Assert.True(result.IsValid, string.Join("; ", result.Violations));
        var inserts = result.Raw.Plates.Single().Placements.Skip(1).ToArray();
        Assert.Equal(2, inserts.Length);
        Assert.All(inserts, p =>
        {
            var localX = p.X - 1;
            var localY = p.Y - 1;
            Assert.Contains(new[] { 2.0, 12.0 }, origin =>
                localX >= origin + 0.25 && localX + 4 <= origin + 5.75
                && localY >= origin + 0.25 && localY + 4 <= origin + 5.75);
        });
    }

    [Fact]
    public void DifferentPrioritiesRemainIndependentInsteadOfElevatingAnInsert()
    {
        var request = Request("stub");
        request.Items[1].Priority = 3;
        var result = NestPipeline.RunCutoutPreview(new Stub(job =>
        {
            Assert.DoesNotContain(job.Parts, p => p.Id.StartsWith("__cutout-proxy-"));
            return Result(job);
        }), "stub", request);
        Assert.True(result.CanKeep);
        Assert.Empty(result.Plates);
    }

    [Theory]
    [InlineData(1, 11.0, 11.0)]
    [InlineData(2, -11.0, 11.0)]
    [InlineData(3, -11.0, -11.0)]
    [InlineData(4, 11.0, -11.0)]
    public void AllQuadrantsKeepPhysicalStockAndExpansion(int quadrant, double x, double y)
    {
        var request = Request("stub") with
        {
            Stock = new[] {
            new NestPlateStock("sheet", new Size(22, 22), 1, 0.25,
                new Spacing(0.5, 0.5), quadrant) },
            Options = new NestJobOptions(maxPlates: 1)
        };
        var result = NestPipeline.RunCutoutPreview(new Stub(job =>
            Result(job, new NestJobPlacement(Assert.Single(job.Parts).Id, 0, x, y, 0))),
            "stub", request);
        Assert.True(result.IsValid, string.Join("; ", result.Violations));
        Assert.Equal(quadrant, result.Plates.Single().Stock.Quadrant);
        Assert.Equal((x, y), (result.Raw.Plates.Single().Placements[0].X,
            result.Raw.Plates.Single().Placements[0].Y));
    }

    [Fact]
    public void CancellationAfterAnEngineIgnoresStopDiscardsEveryBundle()
    {
        var request = Request("stub");
        using var cts = new CancellationTokenSource();
        Assert.Throws<OperationCanceledException>(() => NestPipeline.RunCutoutPreview(
            new Stub(job =>
            {
                var proxy = Assert.Single(job.Parts);
                cts.Cancel();
                return Result(job, new NestJobPlacement(proxy.Id, 0, 11, 11, 0));
            }), "stub", request, token: cts.Token));
        Assert.All(request.Items, item => Assert.Equal(0, item.Drawing.Quantity.Nested));
    }

    [Fact]
    public void CompleteStatusWithMissingProxyIsUntrustedAndNotBindable()
    {
        var request = Request("stub");
        var result = NestPipeline.RunCutoutPreview(new Stub(job => new NestJobResult(
            NestJobStatus.Complete, NestJobStopReason.Completed,
            Array.Empty<NestJobPlateResult>(),
            job.Parts.Select(p => new PartFulfillment(p.Id, p.Quantity, 0, p.Quantity)),
            job.Plates.Select(s => new StockUsage(s.Id, 0, s.Quantity)))), "stub", request);
        Assert.False(result.CanKeep);
        Assert.Empty(result.Plates);
        Assert.Contains(result.Violations, v => v.Contains("accounting"));
        AssertCannotCommit(result, request);
    }

    [Fact]
    public void SuccessfulCompositeReportsOnlyValidatedPhysicalCommitCounts()
    {
        var request = Request("stub");
        var progress = new CollectProgress();
        var result = NestPipeline.RunCutoutPreview(new ReportingStub(), "stub", request, progress);
        Assert.True(result.IsValid, string.Join("; ", result.Violations));
        Assert.Equal(new[] { NestJobStage.EvaluatingCandidate, NestJobStage.PlateCommitted },
            progress.Events.Select(e => e.Stage));
        Assert.Equal(2, progress.Events.Last().CommittedParts);
        Assert.Equal(1, progress.Events.Last().CommittedPlates);
    }

    private sealed class ReportingStub : INestingEngine
    {
        public NestJobResult Solve(NestJob job, IProgress<NestJobProgress>? progress = null,
            CancellationToken token = default)
        {
            var proxy = Assert.Single(job.Parts);
            progress?.Report(new NestJobProgress(NestJobStage.EvaluatingCandidate,
                job.Plates[0].Id, 0, 0, 0));
            progress?.Report(new NestJobProgress(NestJobStage.PlateCommitted,
                job.Plates[0].Id, 0, 1, 1));
            return Result(job, new NestJobPlacement(proxy.Id, 0, 11, 11, 0));
        }
    }

    [Fact]
    public void AutomaticBundleRetainsStockFeasibleQuarterTurn()
    {
        var program = TestShapes.Rectangle(20, 10);
        program.MoveTo(2, 2);
        program.LineTo(2, 8);
        program.LineTo(18, 8);
        program.LineTo(18, 2);
        program.LineTo(2, 2);
        var request = new NestPipelineRequest("Irregular", new[] {
            new NestItem { Drawing = new Drawing("long-frame", program), Quantity = 1 },
            new NestItem { Drawing = new Drawing("insert", TestShapes.Rectangle(3, 3)), Quantity = 1 } },
            new[] { new NestPlateStock("narrow", new Size(22, 12), 1, 0.25) });
        var result = NestPipeline.RunCutoutPreview(request);
        Assert.True(result.IsValid, string.Join("; ", result.Violations));
        var frame = result.Raw.Plates.Single().Placements[0];
        Assert.Equal(System.Math.PI / 2, frame.Rotation, 7);
        Assert.Equal(new[] { "part-0", "part-1" },
            result.Raw.Plates.Single().Placements.Select(p => p.PartId));
    }

    [Fact]
    public void ChangedLiveInsertDuringSolveNeverBindsUncheckedComposite()
    {
        var request = Request("stub");
        var result = NestPipeline.RunCutoutPreview(new Stub(job =>
        {
            request.Items[1].Drawing.Program = TestShapes.Rectangle(40, 40);
            var proxy = Assert.Single(job.Parts);
            return Result(job, new NestJobPlacement(proxy.Id, 0, 11, 11, 0));
        }), "stub", request);
        Assert.False(result.CanKeep);
        Assert.Empty(result.Plates);
        Assert.Contains(result.Violations, v => v.Contains("changed") && v.Contains("insert"));
        AssertCannotCommit(result, request);
    }

    [Fact]
    public void NondefaultOptionsPassUnchangedAndPlateCapRejectsOtherwiseValidOutput()
    {
        var request = Request("stub");
        request.Items[0].Quantity = 2;
        request.Items[1].Quantity = 2;
        request.Items[1].Drawing.Program = TestShapes.Rectangle(6, 6);
        var options = new NestJobOptions("Strip", maxPlates: 1,
            salvageRate: 0.4, minimumSalvageDimension: 2);
        request = request with
        {
            Stock = new[] { new NestPlateStock("sheet", new Size(22, 22), 2, 0.25) },
            Options = options
        };
        var result = NestPipeline.RunCutoutPreview(new Stub(job =>
        {
            Assert.Same(options, job.Options);
            Assert.Equal("Strip", job.Options.PlacementStrategy);
            Assert.Equal(0.4, job.Options.SalvageRate);
            var builder = new NestJobResultBuilder(job);
            foreach (var proxy in job.Parts)
                builder.AddSheet(job.Plates[0], new[] { (proxy.Id, 11.0, 11.0, 0.0) });
            return builder.Build(NestJobStopReason.Completed);
        }), "stub", request);
        Assert.False(result.CanKeep);
        Assert.Empty(result.Plates);
        Assert.Contains(result.Violations, v => v.Contains("MaxPlates"));
        AssertCannotCommit(result, request);
    }

    [Fact]
    public void ObliqueFramePreservesLegalGlobalInsertRotationAndWallClearance()
    {
        var program = TestShapes.Rectangle(20, 20);
        program.MoveTo(5, 5);
        program.LineTo(5, 15);
        program.LineTo(15, 15);
        program.LineTo(15, 5);
        program.LineTo(5, 5);
        const double angle = 0.3;
        var request = new NestPipelineRequest("stub", new[] {
            new NestItem { Drawing = new Drawing("frame", program), Quantity = 1,
                StepAngle = 1, RotationStart = angle, RotationEnd = angle },
            new NestItem { Drawing = new Drawing("insert", TestShapes.Rectangle(3, 3)),
                Quantity = 1, StepAngle = 1, RotationStart = angle, RotationEnd = angle } },
            new[] { new NestPlateStock("sheet", new Size(40, 40), 1, 0.25) });
        var result = NestPipeline.RunCutoutPreview(new Stub(job =>
            Result(job, new NestJobPlacement(Assert.Single(job.Parts).Id, 0, 15, 5, angle))),
            "stub", request);
        Assert.True(result.IsValid, string.Join("; ", result.Violations));
        var poses = result.Raw.Plates.Single().Placements;
        Assert.Equal(angle, poses[1].Rotation, 8);
        var dx = poses[1].X - poses[0].X;
        var dy = poses[1].Y - poses[0].Y;
        var cosine = System.Math.Cos(angle);
        var sine = System.Math.Sin(angle);
        var localX = dx * cosine + dy * sine;
        var localY = -dx * sine + dy * cosine;
        foreach (var x in new[] { 0.0, 3.0 })
            foreach (var y in new[] { 0.0, 3.0 })
            {
                Assert.InRange(localX + x, 5.25, 14.75);
                Assert.InRange(localY + y, 5.25, 14.75);
            }
    }

    [Fact]
    public void LargeDemandLeavesExcessFramesAndInsertsIndependentAfterBoundedPreparation()
    {
        var frame = TestShapes.Rectangle(20, 20);
        frame.MoveTo(5, 5);
        frame.LineTo(5, 15);
        frame.LineTo(15, 15);
        frame.LineTo(15, 5);
        frame.LineTo(5, 5);
        var request = new NestPipelineRequest("stub", new[] {
            new NestItem { Drawing = new Drawing("frame", frame), Quantity = 40,
                StepAngle = 1, RotationStart = 0, RotationEnd = 0 },
            new NestItem { Drawing = new Drawing("insert", TestShapes.Rectangle(6, 6)),
                Quantity = 40, StepAngle = 1, RotationStart = 0, RotationEnd = 0 } },
            new[] { new NestPlateStock("sheet", new Size(22, 22), 40, 0.25) });
        var result = NestPipeline.RunCutoutPreview(new Stub(job =>
        {
            Assert.Equal(32, job.Parts.Count(p => p.Id.StartsWith("__cutout-proxy-")));
            Assert.Equal(8, job.Parts.Single(p => p.Id == "part-0").Quantity);
            Assert.Equal(8, job.Parts.Single(p => p.Id == "part-1").Quantity);
            return Result(job);
        }), "stub", request);
        Assert.False(result.CanKeep);
        Assert.All(result.Raw.Fulfillment, f => Assert.Equal((40, 0, 40),
            (f.Requested, f.Placed, f.Unplaced)));
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
