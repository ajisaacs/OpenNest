using OpenNest.CNC;
using OpenNest.Geometry;

namespace OpenNest.Engine.Tests.Jobs;

public class NestJobCancellationTests
{
    [Fact]
    public void PreTrialCancellationSkipsCandidateWorkAndPreservesInput()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var nester = new CancellableNester(_ => new PlateCandidate(Array.Empty<NestJobPlacement>()));
        var part = new NestJobPart("part", PartGeometrySnapshot.FromProgram(TestDrawingFactory.Rectangle(4, 3)), 1);
        var sourceGeometry = part.Geometry.Motions.ToArray();
        var stock = new NestPlateStock("stock", new Size(20, 30), 1);
        var job = new NestJob(new[] { part }, new[] { stock });

        Assert.Throws<OperationCanceledException>(() => new NestJobRunner(_ => nester).Solve(job, token: cancellation.Token));

        Assert.Equal(0, nester.Calls);
        Assert.Equal(sourceGeometry, job.Parts[0].Geometry.Motions);
        Assert.Equal(1, job.Parts[0].Quantity);
        Assert.Equal(1, job.Plates[0].Quantity);
    }

    [Fact]
    public void CancellationDuringCandidateThrowsWithoutCommitOrInputMutation()
    {
        using var cancellation = new CancellationTokenSource();
        var reports = new List<NestJobProgress>();
        var part = new NestJobPart("part", PartGeometrySnapshot.FromProgram(TestDrawingFactory.Rectangle(4, 3)), 1);
        var sourceGeometry = part.Geometry.Motions.ToArray();
        var stock = new NestPlateStock("stock", new Size(20, 30), 1);
        var job = new NestJob(new[] { part }, new[] { stock });
        var nester = new CancellableNester((_, token) =>
        {
            cancellation.Cancel();
            token.ThrowIfCancellationRequested();
            return new PlateCandidate(Array.Empty<NestJobPlacement>());
        });

        Assert.Throws<OperationCanceledException>(() => new NestJobRunner(_ => nester)
            .Solve(job, new InlineProgress(reports.Add), cancellation.Token));

        Assert.Equal(1, nester.Calls);
        Assert.DoesNotContain(reports, report => report.Stage == NestJobStage.PlateCommitted);
        Assert.Equal(sourceGeometry, job.Parts[0].Geometry.Motions);
        Assert.Equal(1, job.Parts[0].Quantity);
        Assert.Equal(1, job.Plates[0].Quantity);
    }

    [Fact]
    public void LegacyProgressIsWrappedWithCurrentCandidateContext()
    {
        var reports = new List<NestJobProgress>();
        var part = new NestJobPart("part", PartGeometrySnapshot.FromProgram(TestDrawingFactory.Rectangle(4, 3)), 1);
        var job = new NestJob(new[] { part }, new[] { new NestPlateStock("stock", new Size(20, 30), 1) });
        var runner = new NestJobRunner(_ => new LegacyPlateNesterAdapter(plate => new ReportingEngine(plate)));

        var result = runner.Solve(job, new InlineProgress(reports.Add));

        Assert.Equal(NestJobStopReason.NoPlacementFound, result.StopReason);
        var legacy = Assert.Single(reports.Where(report => report.LegacyProgress != null));
        Assert.Equal(NestJobStage.EvaluatingCandidate, legacy.Stage);
        Assert.Equal("stock", legacy.StockId);
        Assert.Equal(0, legacy.PlateIndex);
        Assert.Equal(0, legacy.CommittedPlates);
        Assert.Equal(0, legacy.CommittedParts);
        Assert.Equal("legacy detail", legacy.LegacyProgress!.Description);
    }

    private sealed class InlineProgress(Action<NestJobProgress> report) : IProgress<NestJobProgress>
    {
        public void Report(NestJobProgress value) => report(value);
    }

    private sealed class CancellableNester : IPlateNester
    {
        private readonly Func<PlatePlacementRequest, CancellationToken, PlateCandidate> place;

        public CancellableNester(Func<PlatePlacementRequest, PlateCandidate> place)
        {
            this.place = (request, _) => place(request);
        }

        public CancellableNester(Func<PlatePlacementRequest, CancellationToken, PlateCandidate> place)
        {
            this.place = place;
        }

        public int Calls { get; private set; }

        public PlateCandidate Place(PlatePlacementRequest request, IProgress<NestJobProgress>? progress = null,
            CancellationToken token = default)
        {
            Calls++;
            return place(request, token);
        }
    }

    private sealed class ReportingEngine(Plate plate) : NestEngineBase(plate)
    {
        public override string Name => "reporting";
        public override string Description => "reports progress";

        public override List<Part> Nest(List<NestItem> items, IProgress<NestProgress>? progress,
            CancellationToken token)
        {
            progress?.Report(new NestProgress { Description = "legacy detail" });
            return new List<Part>();
        }
    }
}
