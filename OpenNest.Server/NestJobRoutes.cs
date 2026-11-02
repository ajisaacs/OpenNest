using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Options;
using OpenNest.Data;

namespace OpenNest.Server;

/// <summary>
/// <c>/api/nest-jobs</c>: the server nesting queue. Desktops submit a package, poll its state,
/// read the result and cancel; the worker (<c>OpenNest.Server.Worker</c>) claims a job, reads
/// its package, heartbeats and reports. The server never opens a package or result: the worker
/// solves the job and the desktop validates the result against its own drawings. Progress
/// and plans the worker stores reach desktops through each job's event stream.
/// See <c>docs/server-nesting.md</c>.
/// </summary>
internal static class NestJobRoutes
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    /// <summary>How often an idle event stream sends a keep-alive comment; tests shorten it.</summary>
    internal static TimeSpan KeepAlive { get; set; } = NestJobRules.StreamKeepAlive;

    public static void MapNestJobs(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/nest-jobs", async (HttpContext http, NestDatabase db, TimeProvider clock) =>
        {
            if (http.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } limit)
                limit.MaxRequestBodySize = NestJobRules.MaxPackageBytes + 1024 * 1024;
            if (!http.Request.HasFormContentType)
                return Results.BadRequest("Expected multipart/form-data with metadata and file parts.");

            IFormCollection form;
            try
            {
                form = await http.Request.ReadFormAsync(new FormOptions { MultipartBodyLengthLimit = NestJobRules.MaxPackageBytes + 1024 * 1024 });
            }
            catch (Exception ex) when (ex is InvalidDataException || (ex is IOException && ex is not BadHttpRequestException))
            {
                return Results.BadRequest("Malformed multipart body.");
            }

            NestJobSubmission? submission;
            try { submission = JsonSerializer.Deserialize<NestJobSubmission>(form["metadata"].FirstOrDefault() ?? "", Options); }
            catch (JsonException) { return Results.BadRequest("Invalid metadata JSON."); }
            if (submission is null || string.IsNullOrWhiteSpace(submission.EngineName))
                return Results.BadRequest("The job must name an engine.");
            if (new[] { submission.Title, submission.EngineName, submission.SubmittedBy }.Any(t => (t?.Length ?? 0) > NestJobRules.MaxTextLength))
                return Results.BadRequest("A job title, engine or submitter is too long.");
            submission.Title ??= "";
            submission.SubmittedBy ??= "";

            var file = form.Files["file"];
            if (file is null || file.Length == 0)
                return Results.BadRequest("Missing or empty 'file' part.");
            if (file.Length > NestJobRules.MaxPackageBytes)
                return Results.BadRequest("The nest job package is too large.");
            using var stream = file.OpenReadStream();
            using var buffer = new MemoryStream((int)file.Length);
            await stream.CopyToAsync(buffer);
            return Results.Ok(db.InsertJob(Guid.NewGuid(), submission, buffer.ToArray(), clock.GetUtcNow()));
        });

        app.MapGet("/api/nest-jobs/{id:guid}", (Guid id, NestDatabase db) =>
            db.GetJob(id) is { } job ? Results.Ok(job) : Results.NotFound());

        app.MapGet("/api/nest-jobs/{id:guid}/result", (Guid id, NestDatabase db) =>
        {
            if (db.GetJobResult(id) is { } result)
                return Results.Text(result, "application/json", Encoding.UTF8);
            return db.GetJob(id) is { } job
                ? Results.Conflict($"The job has no result: it is {job.State}.")
                : Results.NotFound();
        });

        app.MapGet("/api/nest-jobs/{id:guid}/plan", (Guid id, HttpResponse response, NestDatabase db) =>
        {
            if (db.GetJobPlan(id) is { } plan)
            {
                response.Headers[RemoteNestJobs.PlanRevisionHeader] = plan.Revision.ToString(CultureInfo.InvariantCulture);
                return Results.Text(plan.Plan, "application/json", Encoding.UTF8);
            }
            return db.GetJob(id) is null ? Results.NotFound() : Results.NoContent();
        });

        app.MapGet("/api/nest-jobs/{id:guid}/events", StreamEvents);

        app.MapPost("/api/nest-jobs/{id:guid}/cancel", (Guid id, NestDatabase db, TimeProvider clock) =>
            db.CancelJob(id, clock.GetUtcNow()) is { } job ? Results.Ok(job) : Results.NotFound());

        // Worker side.
        app.MapPost("/api/nest-jobs/claim", async (HttpRequest request, NestDatabase db, TimeProvider clock) =>
        {
            var (message, error) = await ReadWorkerMessage(request);
            if (error is not null) return error;
            return db.ClaimJob(message!.WorkerId, clock.GetUtcNow()) is { } job ? Results.Ok(job) : Results.NoContent();
        });

        app.MapGet("/api/nest-jobs/{id:guid}/package", (Guid id, NestDatabase db) =>
            db.GetJobPackage(id) is { } package
                ? Results.File(package, "application/zip", $"{id}.zip")
                : Results.NotFound());

        app.MapPost("/api/nest-jobs/{id:guid}/heartbeat", async (Guid id, HttpRequest request, NestDatabase db, TimeProvider clock) =>
        {
            var (message, error) = await ReadWorkerMessage(request);
            if (error is not null) return error;
            return db.HeartbeatJob(id, message!.WorkerId, clock.GetUtcNow()) is { } beat
                ? Results.Ok(beat)
                : Results.Conflict("This worker no longer holds the job.");
        });

        app.MapPut("/api/nest-jobs/{id:guid}/result", async (Guid id, string? workerId, HttpContext http, NestDatabase db, TimeProvider clock) =>
        {
            if (string.IsNullOrWhiteSpace(workerId))
                return Results.BadRequest("workerId is required.");
            var (result, error) = await ReadJsonBody(http, NestJobRules.MaxResultBytes, "nest result");
            if (error is not null) return error;
            return db.CompleteJob(id, workerId, result!, clock.GetUtcNow())
                ? Results.Ok(db.GetJob(id))
                : Results.Conflict("This worker no longer holds the job.");
        });

        // Progress and plans renew the lease like a heartbeat and answer as one.
        app.MapPut("/api/nest-jobs/{id:guid}/progress", async (Guid id, string? workerId, HttpContext http, NestDatabase db, TimeProvider clock) =>
        {
            if (string.IsNullOrWhiteSpace(workerId))
                return Results.BadRequest("workerId is required.");
            var (progress, error) = await ReadJsonBody(http, NestJobRules.MaxProgressBytes, "progress report");
            if (error is not null) return error;
            return db.ReportJobProgress(id, workerId, progress!, clock.GetUtcNow()) is { } beat
                ? Results.Ok(beat)
                : Results.Conflict("This worker no longer holds the job.");
        });

        app.MapPut("/api/nest-jobs/{id:guid}/plan", async (Guid id, string? workerId, HttpContext http, NestDatabase db, TimeProvider clock) =>
        {
            if (string.IsNullOrWhiteSpace(workerId))
                return Results.BadRequest("workerId is required.");
            var (plan, error) = await ReadJsonBody(http, NestJobRules.MaxPlanBytes, "nest plan");
            if (error is not null) return error;
            return db.ReportJobPlan(id, workerId, plan!, clock.GetUtcNow()) is { } beat
                ? Results.Ok(beat)
                : Results.Conflict("This worker no longer holds the job.");
        });

        app.MapPost("/api/nest-jobs/{id:guid}/fail", async (Guid id, HttpRequest request, NestDatabase db, TimeProvider clock) =>
        {
            var (message, error) = await ReadWorkerMessage(request);
            if (error is not null) return error;
            return db.FailJob(id, message!.WorkerId, message.Error, clock.GetUtcNow())
                ? Results.Ok(db.GetJob(id))
                : Results.Conflict("This worker no longer holds the job.");
        });

        app.MapPost("/api/nest-jobs/{id:guid}/release", async (Guid id, HttpRequest request, NestDatabase db, TimeProvider clock) =>
        {
            var (message, error) = await ReadWorkerMessage(request);
            if (error is not null) return error;
            return db.ReleaseJob(id, message!.WorkerId, clock.GetUtcNow())
                ? Results.Ok(db.GetJob(id))
                : Results.Conflict("This worker no longer holds the job.");
        });

        app.MapPost("/api/nest-jobs/{id:guid}/cancelled", async (Guid id, HttpRequest request, NestDatabase db, TimeProvider clock) =>
        {
            var (message, error) = await ReadWorkerMessage(request);
            if (error is not null) return error;
            return db.ConfirmJobCancelled(id, message!.WorkerId, clock.GetUtcNow())
                ? Results.Ok(db.GetJob(id))
                : Results.Conflict("This worker no longer holds the job.");
        });
    }

    /// <summary>
    /// <c>text/event-stream</c> of one job until it is final. Each change sends <c>event: job</c>
    /// (the record, <c>id</c> its revision), preceded by <c>event: plan</c> (<c>id</c> the plan's
    /// revision) when a newer best plan was stored. A reconnect's <c>Last-Event-ID</c> skips what
    /// it already has; a final job is always sent, then the stream ends. <c>event: gone</c> ends
    /// it when the job is removed. Idle streams send a comment every <see cref="KeepAlive"/>.
    /// </summary>
    private static async Task StreamEvents(Guid id, HttpContext http, NestDatabase db, IOptions<Microsoft.AspNetCore.Http.Json.JsonOptions> json)
    {
        var response = http.Response;
        if (db.GetJob(id) is null)
        {
            response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }
        var seen = long.TryParse(http.Request.Headers["Last-Event-ID"].FirstOrDefault(), NumberStyles.None,
            CultureInfo.InvariantCulture, out var last) ? last : 0;
        var sentPlan = seen;
        var sentQueue = -1;

        http.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();
        response.ContentType = "text/event-stream";
        response.Headers.CacheControl = "no-cache";
        response.Headers["X-Accel-Buffering"] = "no";
        var token = http.RequestAborted;
        try
        {
            await response.Body.FlushAsync(token);
            while (true)
            {
                var changed = db.JobsChanged;
                var job = db.GetJob(id);
                if (job is null)
                {
                    await WriteEvent(response, "gone", null, "{}", token);
                    return;
                }
                if (job.PlanRevision > sentPlan && db.GetJobPlan(id) is { } plan && plan.Revision > sentPlan)
                {
                    await WriteEvent(response, "plan", plan.Revision, plan.Plan, token);
                    sentPlan = plan.Revision;
                }
                if (job.Revision > seen || job.QueuePosition != sentQueue && sentQueue >= 0 || job.IsFinished)
                {
                    await WriteEvent(response, "job", job.Revision, JsonSerializer.Serialize(job, json.Value.SerializerOptions), token);
                    seen = job.Revision;
                }
                sentQueue = job.QueuePosition;
                if (job.IsFinished)
                    return;

                // Changes in this process wake the stream at once; the keep-alive also re-reads.
                if (await Task.WhenAny(changed, Task.Delay(KeepAlive, token)) != changed)
                {
                    await response.WriteAsync(": keep-alive\n\n", token);
                    await response.Body.FlushAsync(token);
                }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // The desktop went away.
        }
    }

    private static async Task WriteEvent(HttpResponse response, string type, long? id, string data, CancellationToken token)
    {
        var text = new StringBuilder();
        text.Append("event: ").Append(type).Append('\n');
        if (id is { } value)
            text.Append("id: ").Append(value.ToString(CultureInfo.InvariantCulture)).Append('\n');
        foreach (var line in data.Split('\n'))
            text.Append("data: ").Append(line.TrimEnd('\r')).Append('\n');
        text.Append('\n');
        await response.WriteAsync(text.ToString(), token);
        await response.Body.FlushAsync(token);
    }

    private static async Task<(string? Json, IResult? Error)> ReadJsonBody(HttpContext http, long maxBytes, string what)
    {
        if (http.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } limit)
            limit.MaxRequestBodySize = maxBytes + 1;
        if (!http.Request.HasJsonContentType())
            return (null, Results.BadRequest($"Expected an application/json {what}."));
        using var body = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await http.Request.Body.ReadAsync(chunk, http.RequestAborted)) > 0)
        {
            if (body.Length + read > maxBytes)
                return (null, Results.BadRequest($"The {what} is too large."));
            body.Write(chunk, 0, read);
        }
        return (Encoding.UTF8.GetString(body.GetBuffer(), 0, (int)body.Length), null);
    }

    private static async Task<(NestJobWorkerMessage? Message, IResult? Error)> ReadWorkerMessage(HttpRequest request)
    {
        var (json, error) = await DrawingSetRoutes.ReadJson(request, "worker message");
        if (error is not null) return (null, error);
        NestJobWorkerMessage? message;
        try { message = JsonSerializer.Deserialize<NestJobWorkerMessage>(json!, Options); }
        catch (JsonException) { return (null, Results.BadRequest("Invalid worker message JSON.")); }
        if (message is null || string.IsNullOrWhiteSpace(message.WorkerId) || message.WorkerId.Length > NestJobRules.MaxTextLength)
            return (null, Results.BadRequest("workerId is required."));
        return (message, null);
    }
}
