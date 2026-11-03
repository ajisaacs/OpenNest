using OpenNest.Engine.Jobs;
using OpenNest.Engine.Jobs.Remote;
using OpenNest.Geometry;

namespace OpenNest.Engine.Tests.Jobs;

/// <summary>
/// The nest server worker's live progress: the recording engine passes each solve's progress
/// on, and a plan sent over the wire is drawn on the desktop as the same sheets a local run
/// shows in its best-plan-so-far strip.
/// </summary>
public class RemoteNestProgressTests
{
    private sealed class Collect : IProgress<NestJobProgress>
    {
        public readonly List<NestJobProgress> Reports = new();

        public void Report(NestJobProgress value)
        {
            lock (Reports)
                Reports.Add(value);
        }
    }

    private static NestPipelineRequest Request(string engine)
    {
        var template = new Plate(48, 96) { EdgeSpacing = new Spacing(0.25, 0.25, 0.25, 0.25), PartSpacing = 0.25 };
        return new NestPipelineRequest(engine,
            [
                new NestItem { Drawing = new Drawing("bracket", TestDrawingFactory.Rectangle(10, 20)), Quantity = 9 },
                new NestItem { Drawing = new Drawing("ell", TestDrawingFactory.LShape()), Quantity = 6, Priority = -1 },
            ],
            NestStockBuilder.FromTemplate(template, null),
            new NestJobOptions(maxPlates: 5));
    }

    private static IEnumerable<(string Name, double X, double Y, double Rotation)> Poses(IEnumerable<ProposedPlate> sheets) =>
        sheets.SelectMany(s => s.Parts.Select(p => (p.BaseDrawing.Name, p.Location.X, p.Location.Y, p.Rotation)));

    [Theory]
    [InlineData("Rectangles")]
    [InlineData("Default")]
    public void AWirePlanDrawsAsTheLocalPlanSheets(string engine)
    {
        var request = Request(engine);
        var local = new Collect();
        NestPipeline.Run(request, local);
        var plans = local.Reports.Where(r => r.Stage == NestJobStage.PlanImproved && r.PlanSheets != null).ToList();
        Assert.NotEmpty(plans);

        var preview = new RemoteNestPlanPreview(request);
        foreach (var plan in plans)
        {
            var sheets = preview.Sheets(RemoteNestPlan.From(plan.Plan).ToJson());
            Assert.NotNull(sheets);
            Assert.Equal(plan.PlanSheets.Select(s => (s.Stock.Id, s.Quantity)), sheets.Select(s => (s.Stock.Id, s.Quantity)));
            Assert.Equal(Poses(plan.PlanSheets), Poses(sheets));
            // Detached drawings: the caller's are never touched.
            Assert.All(sheets.SelectMany(s => s.Parts), p => Assert.DoesNotContain(request.Items, i => ReferenceEquals(i.Drawing, p.BaseDrawing)));
        }
    }

    [Fact]
    public void ACutoutTrialPlanDrawsItsFrameProxies()
    {
        // A frame whose window takes the strap: the trial's plans name a frame proxy, which the
        // preview bundles on the desktop the first time it sees one.
        var frame = TestDrawingFactory.Rectangle(20, 20);
        frame.Codes.Add(new OpenNest.CNC.RapidMove(4, 3));
        frame.Codes.Add(new OpenNest.CNC.LinearMove(16, 3));
        frame.Codes.Add(new OpenNest.CNC.LinearMove(16, 17));
        frame.Codes.Add(new OpenNest.CNC.LinearMove(4, 17));
        frame.Codes.Add(new OpenNest.CNC.LinearMove(4, 3));
        var request = new NestPipelineRequest("Default",
            [
                new NestItem { Drawing = new Drawing("frame", frame), Quantity = 2 },
                new NestItem { Drawing = new Drawing("strap", TestDrawingFactory.Rectangle(13, 1.25)), Quantity = 2 },
            ],
            new[] { new NestPlateStock("sheet", new Size(48, 48), 2, 0.25) });
        var tap = new Collect();
        NestPipeline.Run(new RecordingNestingEngine(NestingEngineRegistry.Create("Default")) { Progress = tap }, "Default", request);
        var proxyPlans = tap.Reports.Where(r => r.Stage == NestJobStage.PlanImproved && r.Plan != null
            && r.Plan.Plates.Any(p => p.Placements.Any(x => x.PartId.StartsWith("__cutout-proxy-")))).ToList();
        Assert.NotEmpty(proxyPlans);

        var preview = new RemoteNestPlanPreview(request);
        var sheets = preview.Sheets(RemoteNestPlan.From(proxyPlans[^1].Plan).ToJson());

        Assert.NotNull(sheets);
        Assert.Equal(proxyPlans[^1].Plan.Plates.Sum(p => p.Placements.Count), sheets.Sum(s => s.Quantity * s.Parts.Count));
    }

    [Fact]
    public void UnreadableOrForeignPlansDrawNothing()
    {
        var preview = new RemoteNestPlanPreview(Request("Rectangles"));
        Assert.Null(preview.Sheets(""));
        Assert.Null(preview.Sheets("not json"));
        Assert.Null(preview.Sheets("""{"version":99,"plan":{"plates":[]}}"""));
        var stranger = new NestJobResult(NestJobStatus.Complete, NestJobStopReason.Completed,
            [new NestJobPlateResult(0, Request("Rectangles").Stock[0], [new NestJobPlacement("part-7", 0, 1, 1, 0)])],
            [], []);
        Assert.Null(preview.Sheets(RemoteNestPlan.From(stranger).ToJson()));
    }

    [Fact]
    public void TheRecorderPassesEachSolvesProgressOn()
    {
        var request = Request("Rectangles");
        var tap = new Collect();
        var recorder = new RecordingNestingEngine(NestingEngineRegistry.Create(request.EngineName)) { Progress = tap };

        NestPipeline.Run(recorder, request.EngineName, request);

        Assert.Contains(tap.Reports, r => r.Stage == NestJobStage.PlanImproved && r.Plan != null);
        // Before the pipeline draws it: the worker sends placements, not parts.
        Assert.All(tap.Reports, r => Assert.Null(r.PlanSheets));
    }

    [Fact]
    public void AProgressReportSurvivesTheWire()
    {
        var report = new NestJobProgress(NestJobStage.PlateCommitted, "stock-1", 2, 3, 41);
        var back = RemoteNestProgress.FromJson(RemoteNestProgress.From(report).ToJson())!.ToProgress();

        Assert.Equal(report, back);
        Assert.Null(RemoteNestProgress.FromJson(""));
        Assert.Null(RemoteNestProgress.FromJson("{\"version\":2}"));
    }
}
