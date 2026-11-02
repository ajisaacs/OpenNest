using System.Net;
using System.Threading.Channels;
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
/// Live progress for server nesting: the worker's reports, each job's event stream (order,
/// the final event, reconnecting with Last-Event-ID), the desktop's watch with its polling
/// fallback, and how soon a worker sees a cancel request.
/// </summary>
public class NestJobEventsTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(20);

    private static NestPipelineRequest Request()
    {
        var program = new OpenNest.CNC.Program();
        program.MoveTo(0, 0);
        program.LineTo(10, 0);
        program.LineTo(10, 20);
        program.LineTo(0, 20);
        program.LineTo(0, 0);
        var template = new Plate(48, 96) { EdgeSpacing = new Spacing(0.25, 0.25, 0.25, 0.25), PartSpacing = 0.25 };
        return new NestPipelineRequest("Rectangles",
            new[] { new NestItem { Drawing = new Drawing("bracket", program), Quantity = 9 } },
            NestStockBuilder.FromTemplate(template, null),
            new NestJobOptions(maxPlates: 5));
    }

    private static NestJobSubmission Describe() => new() { Title = "N26-0042", EngineName = "Rectangles", SubmittedBy = "aj@pc" };

    private static RemoteNestJobs Client(ServerFactory factory, DelegatingHandler? handler = null)
    {
        var http = handler is null ? factory.CreateClient() : factory.CreateDefaultClient(handler);
        return new RemoteNestJobs(http, http.BaseAddress!.ToString());
    }

    /// <summary>Reads a stream into a channel, so a test can act between events.</summary>
    private static (ChannelReader<NestJobEvent> Events, Task Done) Listen(RemoteNestJobs jobs, Guid id, long lastEventId = 0)
    {
        var channel = Channel.CreateUnbounded<NestJobEvent>();
        var done = Task.Run(async () =>
        {
            try
            {
                await foreach (var update in jobs.WatchAsync(id, lastEventId))
                    await channel.Writer.WriteAsync(update);
                channel.Writer.Complete();
            }
            catch (Exception ex)
            {
                channel.Writer.Complete(ex);
            }
        });
        return (channel.Reader, done);
    }

    private static async Task<NestJobEvent> Next(ChannelReader<NestJobEvent> events)
    {
        using var timeout = new CancellationTokenSource(Wait);
        return await events.ReadAsync(timeout.Token);
    }

    private static async Task<NestJobEvent> NextJob(ChannelReader<NestJobEvent> events)
    {
        var update = await Next(events);
        Assert.Equal(NestJobEventKind.Job, update.Kind);
        return update;
    }

    [Fact]
    public async Task TheStreamSendsEachChangeInOrderAndEndsAtTheFinalState()
    {
        using var factory = new ServerFactory();
        var jobs = Client(factory);
        var job = await jobs.SubmitAsync(Describe(), new byte[] { 1 });
        var (events, done) = Listen(jobs, job.Id);

        Assert.Equal(NestJobEventKind.Open, (await Next(events)).Kind);
        var queued = await NextJob(events);
        Assert.Equal(NestJobState.Queued, queued.Job!.State);

        await jobs.ClaimAsync("w1");
        var running = await NextJob(events);
        Assert.Equal(NestJobState.Running, running.Job!.State);

        var progress = RemoteNestProgress.From(new NestJobProgress(NestJobStage.PlateCommitted, "s", 0, 1, 9)).ToJson();
        Assert.False((await jobs.ReportProgressAsync(job.Id, "w1", progress))!.CancelRequested);
        var reported = await NextJob(events);
        Assert.Equal(progress, reported.Job!.Progress);

        Assert.NotNull(await jobs.ReportPlanAsync(job.Id, "w1", "{\"plan\":1}"));
        var plan = await Next(events);
        Assert.Equal(NestJobEventKind.Plan, plan.Kind);
        Assert.Equal("{\"plan\":1}", plan.Plan);
        var planned = await NextJob(events);
        Assert.Equal(plan.Id, planned.Job!.PlanRevision);
        Assert.Equal(plan.Id, planned.Id);

        Assert.True(await jobs.CompleteAsync(job.Id, "w1", "{}"));
        var final = await NextJob(events);
        Assert.Equal(NestJobState.Succeeded, final.Job!.State);

        // Nothing after the final state: the server ended the stream.
        await done.WaitAsync(Wait);
        Assert.False(await events.WaitToReadAsync());
        var ids = new[] { queued, running, reported, planned, final }.Select(e => e.Id).ToList();
        Assert.Equal(ids.OrderBy(i => i).Distinct(), ids);
    }

    [Fact]
    public async Task AReconnectSkipsWhatItAlreadyHas()
    {
        using var factory = new ServerFactory();
        var jobs = Client(factory);
        var job = await jobs.SubmitAsync(Describe(), new byte[] { 1 });
        await jobs.ClaimAsync("w1");
        await jobs.ReportPlanAsync(job.Id, "w1", "{\"plan\":1}");
        var current = (await jobs.GetAsync(job.Id))!;

        // Up to date: only the stream opening, then whatever changes next.
        var (events, _) = Listen(jobs, job.Id, current.Revision);
        Assert.Equal(NestJobEventKind.Open, (await Next(events)).Kind);
        await jobs.ReportProgressAsync(job.Id, "w1", "{\"version\":1}");
        var next = await NextJob(events);
        Assert.Equal(current.Revision + 1, next.Id);

        // Behind the plan: the plan again, then the job.
        var (behind, _) = Listen(jobs, job.Id, current.PlanRevision - 1);
        Assert.Equal(NestJobEventKind.Open, (await Next(behind)).Kind);
        Assert.Equal(NestJobEventKind.Plan, (await Next(behind)).Kind);
        Assert.Equal(next.Id, (await NextJob(behind)).Id);

        // A finished job sends its final state even to a stream that has it, then ends.
        await jobs.CompleteAsync(job.Id, "w1", "{}");
        var finished = (await jobs.GetAsync(job.Id))!;
        var (late, lateDone) = Listen(jobs, job.Id, finished.Revision);
        Assert.Equal(NestJobEventKind.Open, (await Next(late)).Kind);
        Assert.Equal(NestJobState.Succeeded, (await NextJob(late)).Job!.State);
        await lateDone.WaitAsync(Wait);
    }

    [Fact]
    public async Task UnknownJobsHaveNoStream()
    {
        using var factory = new ServerFactory();
        var jobs = Client(factory);
        var events = new List<NestJobEvent>();
        await foreach (var update in jobs.WatchAsync(Guid.NewGuid()))
            events.Add(update);
        Assert.Equal(NestJobEventKind.Gone, Assert.Single(events).Kind);
        Assert.Null(await jobs.GetPlanAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task OnlyTheWorkerHoldingAJobReportsOnIt()
    {
        using var factory = new ServerFactory();
        var jobs = Client(factory);
        var job = await jobs.SubmitAsync(Describe(), new byte[] { 1 });
        Assert.Null(await jobs.ReportProgressAsync(job.Id, "w1", "{}"));
        await jobs.ClaimAsync("w1");
        Assert.Null(await jobs.ReportPlanAsync(job.Id, "w2", "{}"));
        Assert.Null(await jobs.GetPlanAsync(job.Id));

        await jobs.ReportPlanAsync(job.Id, "w1", "{\"a\":1}");
        Assert.Equal("{\"a\":1}", (await jobs.GetPlanAsync(job.Id))!.Value.Plan);
        await jobs.CancelAsync(job.Id);
        Assert.True((await jobs.ReportProgressAsync(job.Id, "w1", "{}"))!.CancelRequested);

        // Giving the job back starts the next attempt without the last one's plan or stage.
        await jobs.ReleaseAsync(job.Id, "w1");
        var back = (await jobs.GetAsync(job.Id))!;
        Assert.Equal(NestJobState.Cancelled, back.State);
        Assert.Null(await jobs.GetPlanAsync(job.Id));
        Assert.Equal("", back.Progress);
    }

    /// <summary>Lets a test break the event stream or hide it, as a proxy or an older server would.</summary>
    private sealed class StreamTamper : DelegatingHandler
    {
        public bool Hide { get; set; }

        /// <summary>The first this many streams drop right after their first job event.</summary>
        public int Cuts { get; set; }
        public List<string?> LastEventIds { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/events"))
            {
                LastEventIds.Add(request.Headers.TryGetValues("Last-Event-ID", out var ids) ? ids.Single() : null);
                if (Hide)
                    return new HttpResponseMessage(HttpStatusCode.NotFound);
                var response = await base.SendAsync(request, cancellationToken);
                if (Cuts-- > 0)
                    response.Content = new StreamContent(new CutStream(await response.Content.ReadAsStreamAsync(cancellationToken)));
                return response;
            }
            return await base.SendAsync(request, cancellationToken);
        }
    }

    private sealed class CutStream(Stream inner) : Stream
    {
        private readonly System.Text.StringBuilder seen = new();
        private int read;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => read; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => ReadAsync(buffer, offset, count).GetAwaiter().GetResult();
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var text = seen.ToString();
            var job = text.IndexOf("event: job", StringComparison.Ordinal);
            if (job >= 0 && text.IndexOf("\n\n", job, StringComparison.Ordinal) >= 0)
                throw new IOException("Connection reset.");
            // A byte at a time, so nothing past the first job event is delivered.
            var n = await inner.ReadAsync(buffer[..System.Math.Min(buffer.Length, 1)], cancellationToken);
            seen.Append(System.Text.Encoding.UTF8.GetString(buffer.Span[..n]));
            read += n;
            return n;
        }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing)
        {
            if (disposing)
                inner.Dispose();
            base.Dispose(disposing);
        }
    }

    private sealed class Sink<T> : IProgress<T>
    {
        public readonly List<T> Items = new();

        public void Report(T value)
        {
            lock (Items)
                Items.Add(value);
        }
    }

    private static async Task<NestJobRecord> WatchWhileTheWorkerRuns(ServerFactory factory, StreamTamper tamper,
        Sink<NestJobRecord> seen, Sink<string> plans)
    {
        var desktop = Client(factory, tamper);
        var job = await desktop.SubmitAsync(Describe(), NestJobPackage.Write(Request()));
        var watch = new NestJobWatch(desktop) { PollInterval = TimeSpan.FromMilliseconds(50), Job = seen, Plan = plans };
        var watching = watch.RunAsync(job);

        var worker = Client(factory);
        await worker.ClaimAsync("w1");
        await worker.ReportPlanAsync(job.Id, "w1", "{\"plan\":1}");
        await worker.ReportProgressAsync(job.Id, "w1", "{\"version\":1}");
        await worker.CompleteAsync(job.Id, "w1", "{}");
        return await watching.WaitAsync(Wait);
    }

    [Fact]
    public async Task TheWatchReconnectsADroppedStreamFromItsLastEvent()
    {
        using var factory = new ServerFactory();
        // The first two streams drop after their first job event.
        var tamper = new StreamTamper { Cuts = 2 };
        var seen = new Sink<NestJobRecord>();
        var plans = new Sink<string>();

        var final = await WatchWhileTheWorkerRuns(factory, tamper, seen, plans);

        Assert.Equal(NestJobState.Succeeded, final.State);
        Assert.True(tamper.LastEventIds.Count >= 2, "The stream should have been opened again.");
        Assert.Null(tamper.LastEventIds[0]);
        Assert.Contains(tamper.LastEventIds.Skip(1), id => id is not null);
        Assert.Equal(NestJobState.Succeeded, seen.Items[^1].State);
    }

    [Fact]
    public async Task TheWatchPollsAServerWithoutTheStream()
    {
        using var factory = new ServerFactory();
        var tamper = new StreamTamper { Hide = true };
        var seen = new Sink<NestJobRecord>();
        var plans = new Sink<string>();

        var final = await WatchWhileTheWorkerRuns(factory, tamper, seen, plans);

        Assert.Equal(NestJobState.Succeeded, final.State);
        Assert.Single(tamper.LastEventIds);
        Assert.Equal(NestJobState.Succeeded, seen.Items[^1].State);
    }

    [Fact]
    public async Task AnEnginePlanReachesTheDesktopAsTheSheetsItDraws()
    {
        using var factory = new ServerFactory();
        var desktop = Client(factory);
        var request = Request();
        var job = await desktop.SubmitAsync(Describe(), NestJobPackage.Write(request));
        var plans = new Sink<string>();
        var watching = new NestJobWatch(desktop) { Plan = plans }.RunAsync(job);

        // The engine's plans, as the worker's recording engine passes them on.
        var reporter = new NestJobReporter();
        var engine = new RecordingNestingEngine(NestingEngineRegistry.Create(request.EngineName)) { Progress = reporter };
        var worker = Client(factory);
        await worker.ClaimAsync("w1");
        using var stop = new CancellationTokenSource();
        var reporting = reporter.RunAsync(worker, job.Id, "w1", TimeSpan.FromMilliseconds(20), NullLogger.Instance, stop.Token);
        var local = NestPipeline.Run(engine, request.EngineName, request);
        var placed = local.Plates.Sum(p => p.Quantity * p.Parts.Count);

        // The engine's last plan is its answer: drawn here, it holds every part of the result.
        var preview = new RemoteNestPlanPreview(request);
        using (var timeout = new CancellationTokenSource(Wait))
        {
            while (!plans.Items.ToList().Any(plan => preview.Sheets(plan)?.Sum(s => s.Quantity * s.Parts.Count) == placed))
                await Task.Delay(20, timeout.Token);
        }
        stop.Cancel();
        Assert.Equal(NestJobWorker.StopReason.None, await reporting);
        await worker.CompleteAsync(job.Id, "w1", engine.Transcript.ToJson());
        Assert.Equal(NestJobState.Succeeded, (await watching.WaitAsync(Wait)).State);
    }

    [Fact]
    public async Task AWorkerSeesACancelRequestWithinOneReportInterval()
    {
        using var factory = new ServerFactory();
        var jobs = Client(factory);
        var job = await jobs.SubmitAsync(Describe(), new byte[] { 1 });
        await jobs.ClaimAsync("w1");
        var reporter = new NestJobReporter();
        using var stop = new CancellationTokenSource(Wait);
        var interval = TimeSpan.FromMilliseconds(100);
        var reporting = reporter.RunAsync(jobs, job.Id, "w1", interval, NullLogger.Instance, stop.Token);

        reporter.Report(new NestJobProgress(NestJobStage.EvaluatingCandidate, "s", 0, 0, 0));
        using (var timeout = new CancellationTokenSource(Wait))
        {
            while (!(await jobs.GetAsync(job.Id))!.Progress.Contains("evaluatingCandidate"))
                await Task.Delay(50, timeout.Token);
        }
        Assert.False(reporting.IsCompleted);

        var asked = DateTime.UtcNow;
        await jobs.CancelAsync(job.Id);
        Assert.Equal(NestJobWorker.StopReason.CancelRequested, await reporting.WaitAsync(Wait));
        Assert.True(DateTime.UtcNow - asked < TimeSpan.FromSeconds(5));
    }
}
