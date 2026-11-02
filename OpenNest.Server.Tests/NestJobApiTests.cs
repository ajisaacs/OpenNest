using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using OpenNest.Api;
using OpenNest.Data;
using OpenNest.Engine;
using OpenNest.Engine.Jobs;
using OpenNest.Engine.Jobs.Remote;
using OpenNest.Geometry;
using OpenNest.Server.Worker;

namespace OpenNest.Server.Tests;

/// <summary>
/// Server nesting over HTTP: the desktop's client submits a real job package, the worker
/// solves it through the in-memory server, and the desktop replays the result against its own
/// drawings. Queue rules themselves are pinned by <see cref="NestJobQueueTests"/>.
/// </summary>
public class NestJobApiTests
{
    private static OpenNest.CNC.Program Rectangle(double width, double length)
    {
        var program = new OpenNest.CNC.Program();
        program.MoveTo(0, 0);
        program.LineTo(width, 0);
        program.LineTo(width, length);
        program.LineTo(0, length);
        program.LineTo(0, 0);
        return program;
    }

    private static NestPipelineRequest Request(string engine = "Rectangles")
    {
        var template = new Plate(48, 96) { EdgeSpacing = new Spacing(0.25, 0.25, 0.25, 0.25), PartSpacing = 0.25 };
        return new NestPipelineRequest(engine,
            new[]
            {
                new NestItem { Drawing = new Drawing("bracket", Rectangle(10, 20)), Quantity = 9 },
                new NestItem { Drawing = new Drawing("gusset", Rectangle(6.125, 6.125)), Quantity = 14, Priority = 2 },
            },
            NestStockBuilder.FromTemplate(template, null),
            new NestJobOptions(maxPlates: 5));
    }

    private static (RemoteNestJobs Jobs, NestJobWorker Worker) Connect(ServerFactory factory)
    {
        var http = factory.CreateClient();
        var jobs = new RemoteNestJobs(http, http.BaseAddress!.ToString());
        return (jobs, new NestJobWorker(jobs, NullLogger.Instance));
    }

    private static NestJobSubmission Describe(NestPipelineRequest request) =>
        new() { Title = "N26-0042", EngineName = request.EngineName, SubmittedBy = "aj@shop-pc" };

    [Fact]
    public async Task TheWorkerSolvesASubmittedJobAndTheDesktopKeepsTheSameNest()
    {
        using var factory = new ServerFactory();
        var (jobs, worker) = Connect(factory);
        var request = Request();
        var submission = new RemoteNestSubmission(request);

        var job = await jobs.SubmitAsync(Describe(request), NestJobPackage.Write(request));
        Assert.Equal(NestJobState.Queued, job.State);
        Assert.Equal("N26-0042", job.Title);

        Assert.True(await worker.RunOnceAsync(CancellationToken.None));
        Assert.False(await worker.RunOnceAsync(CancellationToken.None));

        var done = (await jobs.GetAsync(job.Id))!;
        Assert.Equal(NestJobState.Succeeded, done.State);
        var result = submission.Complete(NestSolveTranscript.FromJson(await jobs.GetResultAsync(job.Id)));

        Assert.True(result.IsValid, string.Join("; ", result.Violations));
        Assert.Empty(result.Unplaced);
        var parts = result.Plates.SelectMany(p => Enumerable.Repeat(p.Parts, p.Quantity)).SelectMany(p => p).ToList();
        Assert.Equal(9, parts.Count(p => ReferenceEquals(p.BaseDrawing, request.Items[0].Drawing)));
        Assert.Equal(14, parts.Count(p => ReferenceEquals(p.BaseDrawing, request.Items[1].Drawing)));

        // The same nest a local run proposes.
        var local = NestPipeline.Run(request);
        Assert.Equal(local.Plates.SelectMany(p => p.Parts).Select(p => (p.BaseDrawing.Name, p.Location.X, p.Location.Y)),
            result.Plates.SelectMany(p => p.Parts).Select(p => (p.BaseDrawing.Name, p.Location.X, p.Location.Y)));
    }

    [Fact]
    public void APackageReadsBackAsTheSameRequest()
    {
        var request = Request("Default");
        var shared = request.Items[0].Drawing;
        request = request with { Items = request.Items.Append(new NestItem { Drawing = shared, Quantity = 0 }).ToArray() };

        var back = NestJobPackage.Read(NestJobPackage.Write(request, Units.Millimeters));

        Assert.Equal("Default", back.EngineName);
        Assert.Equal(request.Items.Select(i => (i.Drawing.Name, i.Quantity, i.Priority)), back.Items.Select(i => (i.Drawing.Name, i.Quantity, i.Priority)));
        Assert.Same(back.Items[0].Drawing, back.Items[2].Drawing);
        Assert.Equal(request.Stock.Select(s => s.Id), back.Stock.Select(s => s.Id));
        Assert.Equal(5, back.Options!.MaxPlates);
        Assert.Throws<FormatException>(() => NestJobPackage.Read(Encoding.UTF8.GetBytes("not a zip")));
    }

    [Fact]
    public async Task APlugInEngineFailsTheJobWithAReason()
    {
        using var factory = new ServerFactory();
        var (jobs, worker) = Connect(factory);
        var request = Request() with { EngineName = "MyPlugIn" };

        var job = await jobs.SubmitAsync(Describe(request), NestJobPackage.Write(request));
        await worker.RunOnceAsync(CancellationToken.None);

        var failed = (await jobs.GetAsync(job.Id))!;
        Assert.Equal(NestJobState.Failed, failed.State);
        Assert.Contains("MyPlugIn", failed.Error);
        await Assert.ThrowsAsync<IOException>(() => jobs.GetResultAsync(job.Id));
    }

    [Fact]
    public async Task AnUnreadablePackageFailsTheJob()
    {
        using var factory = new ServerFactory();
        var (jobs, worker) = Connect(factory);

        var job = await jobs.SubmitAsync(new NestJobSubmission { EngineName = "Default" }, Encoding.UTF8.GetBytes("junk"));
        await worker.RunOnceAsync(CancellationToken.None);

        var failed = (await jobs.GetAsync(job.Id))!;
        Assert.Equal(NestJobState.Failed, failed.State);
        Assert.Contains("could not be read", failed.Error);
    }

    [Fact]
    public async Task CancellingOverHttpStopsAQueuedJobAndFlagsARunningOne()
    {
        using var factory = new ServerFactory();
        var (jobs, _) = Connect(factory);
        var request = Request();
        var running = await jobs.SubmitAsync(Describe(request), NestJobPackage.Write(request));
        var queued = await jobs.SubmitAsync(Describe(request), NestJobPackage.Write(request));
        Assert.Equal(1, queued.QueuePosition);

        var claimed = await jobs.ClaimAsync("w1");
        Assert.Equal(running.Id, claimed!.Id);
        Assert.Null(await jobs.ClaimAsync("w2"));

        Assert.Equal(NestJobState.Cancelled, (await jobs.CancelAsync(queued.Id))!.State);
        Assert.True((await jobs.CancelAsync(running.Id))!.CancelRequested);
        Assert.True((await jobs.HeartbeatAsync(running.Id, "w1"))!.CancelRequested);
        Assert.Null(await jobs.HeartbeatAsync(running.Id, "w2"));
        Assert.True(await jobs.ConfirmCancelledAsync(running.Id, "w1"));
        Assert.Equal(NestJobState.Cancelled, (await jobs.GetAsync(running.Id))!.State);
        Assert.False(await jobs.CompleteAsync(running.Id, "w1", "{}"));
    }

    [Fact]
    public async Task UnknownJobsAndBadRequestsAreRefused()
    {
        using var factory = new ServerFactory();
        var http = factory.CreateClient();
        var (jobs, _) = Connect(factory);

        Assert.Null(await jobs.GetAsync(Guid.NewGuid()));
        Assert.Null(await jobs.CancelAsync(Guid.NewGuid()));
        Assert.Null(await jobs.ClaimAsync("w1"));
        Assert.Equal(HttpStatusCode.NotFound, (await http.GetAsync($"api/nest-jobs/{Guid.NewGuid()}/package")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await http.PostAsync("api/nest-jobs", new StringContent("{}", Encoding.UTF8, "application/json"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await http.PostAsync("api/nest-jobs/claim", new StringContent("{}", Encoding.UTF8, "application/json"))).StatusCode);

        using var noEngine = new MultipartFormDataContent
        {
            { new StringContent("{\"title\":\"x\"}", Encoding.UTF8, "application/json"), "metadata" },
            { new ByteArrayContent(new byte[] { 1 }), "file", "job.zip" },
        };
        Assert.Equal(HttpStatusCode.BadRequest, (await http.PostAsync("api/nest-jobs", noEngine)).StatusCode);

        var request = Request();
        var job = await jobs.SubmitAsync(Describe(request), NestJobPackage.Write(request));
        var early = await http.GetAsync($"api/nest-jobs/{job.Id}/result");
        Assert.Equal(HttpStatusCode.Conflict, early.StatusCode);
    }
}
