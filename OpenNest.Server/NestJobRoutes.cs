using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http.Features;
using OpenNest.Data;

namespace OpenNest.Server;

/// <summary>
/// <c>/api/nest-jobs</c>: the server nesting queue. Desktops submit a package, poll its state,
/// read the result and cancel; the worker (<c>OpenNest.Server.Worker</c>) claims a job, reads
/// its package, heartbeats and reports. The server never opens a package or result: the worker
/// solves the job and the desktop validates the result against its own drawings.
/// See <c>docs/server-nesting.md</c>.
/// </summary>
internal static class NestJobRoutes
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

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
            if (http.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } limit)
                limit.MaxRequestBodySize = NestJobRules.MaxResultBytes + 1;
            if (!http.Request.HasJsonContentType())
                return Results.BadRequest("Expected an application/json result.");
            using var body = new MemoryStream();
            var chunk = new byte[81920];
            int read;
            while ((read = await http.Request.Body.ReadAsync(chunk, http.RequestAborted)) > 0)
            {
                if (body.Length + read > NestJobRules.MaxResultBytes)
                    return Results.BadRequest("The nest result is too large.");
                body.Write(chunk, 0, read);
            }
            var result = Encoding.UTF8.GetString(body.GetBuffer(), 0, (int)body.Length);
            return db.CompleteJob(id, workerId, result, clock.GetUtcNow())
                ? Results.Ok(db.GetJob(id))
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
