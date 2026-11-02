using System.Drawing;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Forms;
using OpenNest.Data;
using OpenNest.Forms;
using OpenNest.Forms.Commands;
using static OpenNest.WinForms.Tests.FormTesting;

namespace OpenNest.WinForms.Tests.Forms;

/// <summary>
/// My Server Jobs against an in-memory job server: the rows and what each row allows, Cancel
/// Job, Remove and Open, the status-bar count of results to review, and a Keep refused because
/// another desktop kept the result first.
/// </summary>
public class ServerJobsFormTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    /// <summary>Just enough of <c>/api/nest-jobs</c> for the desktop: list, cancel, remove, keep.</summary>
    private sealed class FakeServer : HttpMessageHandler
    {
        public readonly List<NestJobRecord> Jobs = new();
        public readonly List<string> Requests = new();
        public bool Offline { get; set; }
        private int version = 1;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (Offline)
                throw new HttpRequestException("connection refused");
            var path = request.RequestUri!.AbsolutePath.TrimStart('/');
            var query = request.RequestUri.Query;
            lock (Jobs)
            {
                Requests.Add($"{request.Method} {path}{query} {request.Headers.IfMatch}".TrimEnd());
                if (request.Method == HttpMethod.Get && path == "api/nest-jobs")
                {
                    var etag = $"\"{version}\"";
                    if (request.Headers.IfNoneMatch.Any(t => t.Tag == etag))
                        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotModified));
                    var listed = query.Contains("filter=unreviewed") ? Jobs.Where(j => j.IsUnreviewed) : Jobs;
                    var response = Answer(HttpStatusCode.OK, listed.ToList());
                    response.Headers.ETag = new System.Net.Http.Headers.EntityTagHeaderValue(etag);
                    return Task.FromResult(response);
                }
                var job = Jobs.FirstOrDefault(j => path.StartsWith($"api/nest-jobs/{j.Id}", StringComparison.Ordinal));
                if (job == null)
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
                version++;
                if (request.Method == HttpMethod.Delete)
                {
                    Jobs.Remove(job);
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
                }
                if (path.EndsWith("/cancel", StringComparison.Ordinal))
                {
                    if (job.State == NestJobState.Queued)
                        job.State = NestJobState.Cancelled;
                    else
                        job.CancelRequested = true;
                    job.Revision++;
                    return Task.FromResult(Answer(HttpStatusCode.OK, job));
                }
                if (path.EndsWith("/keep", StringComparison.Ordinal))
                {
                    if (!job.IsUnreviewed)
                        return Task.FromResult(Answer(HttpStatusCode.Conflict, job));
                    job.Review = NestJobReview.Kept;
                    job.Revision++;
                    return Task.FromResult(Answer(HttpStatusCode.OK, job));
                }
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            }
        }

        private static HttpResponseMessage Answer(HttpStatusCode status, object body) =>
            new(status) { Content = new StringContent(JsonSerializer.Serialize(body, Json), Encoding.UTF8, "application/json") };
    }

    private static (FakeServer Server, Func<RemoteNestJobs> Connect) Server(params NestJobRecord[] jobs)
    {
        var server = new FakeServer();
        server.Jobs.AddRange(jobs);
        var http = new HttpClient(server);
        return (server, () => new RemoteNestJobs(http, "http://nest-server/"));
    }

    private static NestJobRecord Ready(string title = "N26-0042") => new()
    {
        Id = Guid.NewGuid(),
        Title = title,
        EngineName = "Rectangles",
        SubmittedBy = "aj@shop-pc",
        State = NestJobState.Succeeded,
        SubmittedAt = new DateTime(2026, 11, 2, 13, 0, 0, DateTimeKind.Utc),
        FinishedAt = new DateTime(2026, 11, 2, 13, 5, 0, DateTimeKind.Utc),
        Revision = 7,
        Summary = new NestJobSummary { PartsPlaced = 23, PartsUnplaced = 2, Sheets = 3, Utilization = 0.784 },
    };

    private static NestJobRecord Queued() => new()
    {
        Id = Guid.NewGuid(),
        Title = "N26-0050",
        EngineName = "Default",
        SubmittedBy = "aj@other-pc",
        State = NestJobState.Queued,
        QueuePosition = 1,
        Revision = 1,
    };

    private static NestJobRecord Kept() => new()
    {
        Id = Guid.NewGuid(),
        Title = "N26-0040",
        EngineName = "Default",
        SubmittedBy = "aj@shop-pc",
        State = NestJobState.Succeeded,
        Review = NestJobReview.Kept,
        ReviewedBy = "bob@office",
        Revision = 9,
    };

    private static string Cell(ServerJobsForm form, Guid id, int column) =>
        form.JobsList.Items.Cast<ListViewItem>().Single(i => ((NestJobRecord)i.Tag).Id == id).SubItems[column].Text;

    [Fact]
    public void RowsDescribeEachJobAndAllowOnlyWhatItsStateAllows()
    {
        var ready = Ready();
        var queued = Queued();
        var kept = Kept();
        var (server, connect) = Server(ready, queued, kept);

        StaTestThread.Run(() =>
        {
            using var form = new ServerJobsForm(_ => Task.CompletedTask, connect, "aj");
            Show(form);
            PumpUntil(() => form.Jobs.Count == 3, "the job list");

            Assert.Equal("Ready to review", Cell(form, ready.Id, 1));
            Assert.StartsWith("23 parts on 3 sheets, ", Cell(form, ready.Id, 2));
            Assert.EndsWith(", 2 not placed", Cell(form, ready.Id, 2));
            Assert.Equal("Queued (1 ahead)", Cell(form, queued.Id, 1));
            Assert.Equal("Kept", Cell(form, kept.Id, 1));
            Assert.Contains("Kept by bob@office", form.JobsList.Items[2].ToolTipText);
            Assert.Contains(server.Requests, r => r.StartsWith("GET api/nest-jobs?filter=all", StringComparison.Ordinal) && r.Contains("submittedBy=aj"));

            form.Select(ready.Id);
            Assert.True(form.OpenButton.Enabled);
            Assert.False(form.CancelJobButton.Enabled);
            Assert.True(form.RemoveButton.Enabled);

            form.Select(queued.Id);
            Assert.False(form.OpenButton.Enabled);
            Assert.True(form.CancelJobButton.Enabled);
            Assert.False(form.RemoveButton.Enabled);

            // A kept result can't be committed again; it can only leave the list.
            form.Select(kept.Id);
            Assert.False(form.OpenButton.Enabled);
            Assert.False(form.CancelJobButton.Enabled);
            Assert.True(form.RemoveButton.Enabled);
        }, TimeSpan.FromSeconds(30), "The STA test did not complete.");
    }

    [Fact]
    public void CancelRemoveAndOpenActOnTheSelectedJob()
    {
        var ready = Ready();
        var queued = Queued();
        var (server, connect) = Server(ready, queued);
        var opened = new List<Guid>();

        StaTestThread.Run(() =>
        {
            using var form = new ServerJobsForm(job =>
            {
                opened.Add(job.Id);
                return Task.CompletedTask;
            }, connect, "aj");
            Show(form);
            PumpUntil(() => form.Jobs.Count == 2, "the job list");

            form.Select(queued.Id);
            form.CancelJobButton.PerformClick();
            PumpUntil(() => form.Jobs.Any(j => j.Id == queued.Id && j.State == NestJobState.Cancelled), "the cancelled job");
            Assert.Equal("Cancelled", Cell(form, queued.Id, 1));

            form.Select(ready.Id);
            form.OpenButton.PerformClick();
            PumpUntil(() => opened.Count == 1, "the review to open");
            Assert.Equal(ready.Id, opened[0]);

            // Removing a result nobody kept asks first; declining keeps it.
            var asked = 0;
            form.Confirm = _ => ++asked > 1;
            form.Select(ready.Id);
            form.RemoveButton.PerformClick();
            Assert.Equal(1, asked);
            Assert.Contains(ready, server.Jobs);
            form.RemoveButton.PerformClick();
            PumpUntil(() => form.Jobs.All(j => j.Id != ready.Id), "the removed job to leave the list");
            Assert.Contains(server.Requests, r => r == $"DELETE api/nest-jobs/{ready.Id} \"7\"");
        }, TimeSpan.FromSeconds(30), "The STA test did not complete.");
    }

    [Fact]
    public void TheStatusBarCountsResultsWaitingForReview()
    {
        var (server, connect) = Server(Ready("a"), Ready("b"), Queued(), Kept());
        var enabled = true;

        StaTestThread.Run(() =>
        {
            SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
            using var strip = new StatusStrip();
            using var status = new ServerJobsStatus(strip, 0, () => { }, SystemFonts.StatusFont!, () => enabled, connect, "aj");

            Run(status.RefreshAsync());
            Assert.True(status.Label.Available);
            Assert.Equal("2 server nests to review", status.Label.Text);
            Assert.Contains(server.Requests, r => r.Contains("filter=unreviewed") && r.Contains("submittedBy=aj"));

            server.Jobs.RemoveAll(j => j.Title == "a");
            Run(status.RefreshAsync());
            Assert.Equal("1 server nest to review", status.Label.Text);

            server.Offline = true;
            Run(status.RefreshAsync());
            Assert.False(status.Label.Available);

            server.Offline = false;
            enabled = false;
            Run(status.RefreshAsync());
            Assert.False(status.Label.Available);
        }, TimeSpan.FromSeconds(30), "The STA test did not complete.");

        static void Run(Task task) => PumpUntil(() => task.IsCompleted, "the status refresh");
    }

    [Fact]
    public async Task AKeepIsRefusedWhenAnotherDesktopKeptTheResultFirst()
    {
        var job = Ready();
        var (server, connect) = Server(job);
        var seen = JsonSerializer.Deserialize<NestJobRecord>(JsonSerializer.Serialize(job, Json), Json)!;
        var warnings = new List<string>();

        Assert.True(await new ServerNestDecision(seen, connect) { Warn = warnings.Add }.KeepAsync(null));
        Assert.Empty(warnings);
        lock (server.Jobs)
            server.Jobs[0].ReviewedBy = "aj@shop-pc";

        // A second desktop that reviewed the same result is told, and commits nothing.
        Assert.False(await new ServerNestDecision(seen, connect) { Warn = warnings.Add }.KeepAsync(null));
        Assert.Contains("already kept by aj@shop-pc", Assert.Single(warnings));
        Assert.Contains("Nothing was added", warnings[0]);

        // A job the server no longer has is refused too.
        server.Jobs.Clear();
        Assert.False(await new ServerNestDecision(seen, connect) { Warn = warnings.Add }.KeepAsync(null));
        Assert.Contains("no longer has this job", warnings[1]);
    }

    [Fact]
    public void JobTextNamesQueuePlacesAndReviews()
    {
        Assert.Equal("Queued (next)", ServerJobText.State(new NestJobRecord { State = NestJobState.Queued }));
        Assert.Equal("Stopping", ServerJobText.State(new NestJobRecord { State = NestJobState.Running, CancelRequested = true }));
        Assert.Equal("Discarded", ServerJobText.State(new NestJobRecord { State = NestJobState.Succeeded, Review = NestJobReview.Discarded }));
        Assert.Equal("Cancelled", ServerJobText.State(new NestJobRecord { State = NestJobState.Cancelled }));
        Assert.Equal("out of memory", ServerJobText.Result(new NestJobRecord { State = NestJobState.Failed, Error = "out of memory" }));
        Assert.Equal("", ServerJobText.Result(new NestJobRecord { State = NestJobState.Succeeded }));
        Assert.Equal("(unnamed nest)", ServerJobText.Title(new NestJobRecord()));
        Assert.Null(ServerJobText.Unreviewed(0));
        Assert.Equal("1 server nest to review", ServerJobText.Unreviewed(1));
    }
}
