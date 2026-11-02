using System.Net;
using System.Net.Http.Headers;
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
/// My Server Jobs over HTTP: the job list and its ETag, the worker's summary, Keep recorded
/// once however many desktops try, removal, and a result reopened in a later session from the
/// package it was sent as.
/// </summary>
public class NestJobListApiTests
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

    private static NestPipelineRequest Request(Drawing bracket = null, Drawing gusset = null)
    {
        var template = new Plate(48, 96) { EdgeSpacing = new Spacing(0.25, 0.25, 0.25, 0.25), PartSpacing = 0.25 };
        return new NestPipelineRequest("Rectangles",
            new[]
            {
                new NestItem { Drawing = bracket ?? new Drawing("bracket", Rectangle(10, 20.3)), Quantity = 9 },
                new NestItem { Drawing = gusset ?? new Drawing("gusset", Rectangle(6.125, 6.1)), Quantity = 14, Priority = 2 },
            },
            NestStockBuilder.FromTemplate(template, null),
            new NestJobOptions(maxPlates: 5));
    }

    private static (RemoteNestJobs Jobs, NestJobWorker Worker, HttpClient Http) Connect(ServerFactory factory)
    {
        var http = factory.CreateClient();
        var jobs = new RemoteNestJobs(http, http.BaseAddress!.ToString());
        return (jobs, new NestJobWorker(jobs, NullLogger.Instance), http);
    }

    private static NestJobSubmission Describe(string by = "aj@shop-pc", Guid? nest = null) =>
        new() { Title = "N26-0042", EngineName = "Rectangles", SubmittedBy = by, NestId = nest };

    [Fact]
    public async Task TheListShowsFinishedJobsWithTheirSummaryAndSkipsUnchangedPolls()
    {
        using var factory = new ServerFactory();
        var (jobs, worker, http) = Connect(factory);
        var request = Request();
        var nest = Guid.NewGuid();
        var mine = await jobs.SubmitAsync(Describe(nest: nest), NestJobPackage.Write(request));
        var theirs = await jobs.SubmitAsync(Describe("bob@office"), NestJobPackage.Write(request));
        Assert.True(await worker.RunOnceAsync(CancellationToken.None));

        var all = (await jobs.ListAsync())!;
        Assert.Equal(new[] { theirs.Id, mine.Id }, all.Jobs.Select(j => j.Id));
        Assert.Equal(new[] { theirs.Id }, (await jobs.ListAsync(NestJobListFilter.Active))!.Jobs.Select(j => j.Id));
        var finished = (await jobs.ListAsync(NestJobListFilter.Finished, "aj"))!.Jobs.Single();
        Assert.Equal(mine.Id, finished.Id);
        Assert.Equal(nest, finished.NestId);
        Assert.True(finished.IsUnreviewed);

        // The summary is what the review shows: every part on its sheets.
        var local = NestPipeline.Run(request);
        Assert.Equal(23, finished.Summary!.PartsPlaced);
        Assert.Equal(0, finished.Summary.PartsUnplaced);
        Assert.Equal(local.Plates.Sum(p => p.Quantity), finished.Summary.Sheets);
        Assert.InRange(finished.Summary.Utilization, 0.01, 1);

        // An unchanged list answers 304, which the client reports as null.
        Assert.False(string.IsNullOrEmpty(all.ETag));
        Assert.Null(await jobs.ListAsync(etag: all.ETag));
        await jobs.CancelAsync(theirs.Id);
        Assert.NotNull(await jobs.ListAsync(etag: all.ETag));

        Assert.Equal(HttpStatusCode.BadRequest, (await http.GetAsync("api/nest-jobs?filter=someday")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await http.GetAsync("api/nest-jobs?limit=0")).StatusCode);
    }

    [Fact]
    public async Task OnlyOneDesktopKeepsAResult()
    {
        using var factory = new ServerFactory();
        var (jobs, worker, http) = Connect(factory);
        var request = Request();
        var job = await jobs.SubmitAsync(Describe(), NestJobPackage.Write(request));
        await worker.RunOnceAsync(CancellationToken.None);
        var done = (await jobs.GetAsync(job.Id))!;

        // Both desktops read the same finished job; the first Keep wins, the second is told who kept it.
        var kept = await jobs.KeepAsync(done, "aj@shop-pc");
        Assert.Equal(NestJobReview.Kept, kept!.Review);
        var refused = await Assert.ThrowsAsync<NestJobConflictException>(() => jobs.KeepAsync(done, "bob@office"));
        Assert.Equal("aj@shop-pc", refused.Job.ReviewedBy);
        Assert.Contains("already kept by aj@shop-pc", refused.Message);
        await Assert.ThrowsAsync<NestJobConflictException>(() => jobs.DiscardAsync(kept, "bob@office"));

        // The result and package go with the Keep.
        Assert.Equal(HttpStatusCode.Conflict, (await http.GetAsync($"api/nest-jobs/{job.Id}/result")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await http.GetAsync($"api/nest-jobs/{job.Id}/package")).StatusCode);
        Assert.Empty((await jobs.ListAsync(NestJobListFilter.Unreviewed))!.Jobs);

        // Keep and Remove follow the If-Match contract.
        var bare = await http.PostAsync($"api/nest-jobs/{job.Id}/keep", null);
        Assert.Equal((HttpStatusCode)428, bare.StatusCode);
        using var malformed = new HttpRequestMessage(HttpMethod.Delete, $"api/nest-jobs/{job.Id}");
        malformed.Headers.TryAddWithoutValidation("If-Match", "seven");
        Assert.Equal(HttpStatusCode.BadRequest, (await http.SendAsync(malformed)).StatusCode);
        Assert.Null(await jobs.KeepAsync(new NestJobRecord { Id = Guid.NewGuid(), Revision = 1 }, "aj"));
    }

    [Fact]
    public async Task RemovingTakesFinishedJobsOnly()
    {
        using var factory = new ServerFactory();
        var (jobs, worker, _) = Connect(factory);
        var request = Request();
        var done = await jobs.SubmitAsync(Describe(), NestJobPackage.Write(request));
        await worker.RunOnceAsync(CancellationToken.None);
        var queued = await jobs.SubmitAsync(Describe(), NestJobPackage.Write(request));

        var active = await Assert.ThrowsAsync<NestJobConflictException>(() => jobs.DeleteAsync(queued));
        Assert.Contains("stop it first", active.Message);
        await Assert.ThrowsAsync<NestJobConflictException>(() => jobs.DeleteAsync(done)); // stale: it finished since
        Assert.True(await jobs.DeleteAsync((await jobs.GetAsync(done.Id))!));
        Assert.Null(await jobs.GetAsync(done.Id));
        Assert.False(await jobs.DeleteAsync(new NestJobRecord { Id = done.Id, Revision = 1 }));
    }

    [Fact]
    public async Task AResultReopenedInALaterSessionIsTheSameNestAndRefusesChangedDrawings()
    {
        using var factory = new ServerFactory();
        var (jobs, worker, _) = Connect(factory);
        var request = Request();
        var job = await jobs.SubmitAsync(Describe(), NestJobPackage.Write(request));
        await worker.RunOnceAsync(CancellationToken.None);

        // A later session: new drawing objects read back from the nest, plus an unrelated one
        // and a same-named copy with other geometry.
        var bracket = new Drawing("bracket", Rectangle(10, 20.3));
        var gusset = new Drawing("gusset", Rectangle(6.125, 6.1));
        var drawings = new[] { new Drawing("bracket", Rectangle(11, 20)), new Drawing("spare", Rectangle(1, 1)), gusset, bracket };
        var document = NestJobPackage.ReadDocument(await jobs.GetPackageAsync(job.Id));
        var transcript = NestSolveTranscript.FromJson(await jobs.GetResultAsync(job.Id));

        var reopened = RemoteNestSubmission.Reopen(document, drawings).Complete(transcript);
        Assert.True(reopened.IsValid, string.Join("; ", reopened.Violations));
        var local = NestPipeline.Run(Request(bracket, gusset));
        Assert.Equal(local.Plates.SelectMany(p => p.Parts).Select(p => (p.BaseDrawing, p.Location.X, p.Location.Y)),
            reopened.Plates.SelectMany(p => p.Parts).Select(p => (p.BaseDrawing, p.Location.X, p.Location.Y)));

        // A drawing edited since it was sent refuses the result; a missing one is named.
        var edited = new Drawing("gusset", Rectangle(6.125, 6.2));
        var changed = Assert.Throws<NestReplayMismatchException>(() =>
            RemoteNestSubmission.Reopen(document, new[] { bracket, edited }).Complete(transcript));
        Assert.Contains("'gusset' changed", changed.Message);
        var missing = Assert.Throws<NestReplayMismatchException>(() => RemoteNestSubmission.Reopen(document, new[] { bracket }));
        Assert.Contains("no drawing named 'gusset'", missing.Message);

        // A job sent before fingerprints existed can only be reviewed where it was sent.
        document.DrawingFingerprints = null;
        Assert.Throws<NestReplayMismatchException>(() => RemoteNestSubmission.Reopen(document, drawings));
    }
}
