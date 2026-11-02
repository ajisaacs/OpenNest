using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace OpenNest.Data;

/// <summary>
/// Server nesting over <c>/api/nest-jobs</c>: desktops submit, poll, read results and cancel;
/// the worker claims, heartbeats and reports. Packages and results are opaque bytes here
/// (<c>OpenNest.Api.NestJobPackage</c>, <c>NestSolveTranscript</c>). A server without nest jobs
/// raises <see cref="ServerTooOldException"/>; other failures surface as <see cref="IOException"/>.
/// </summary>
public sealed class RemoteNestJobs : NestServerClient
{
    private const string Feature = "server nesting";

    public RemoteNestJobs(string baseUrl)
        : base(new HttpClient(), baseUrl, ownsClient: true)
    {
    }

    /// <summary>For tests and hosts that pool HttpClient instances.</summary>
    public RemoteNestJobs(HttpClient httpClient, string baseUrl)
        : base(httpClient, baseUrl, ownsClient: false)
    {
    }

    public async Task<NestJobRecord> SubmitAsync(NestJobSubmission submission, byte[] package, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(submission);
        ArgumentNullException.ThrowIfNull(package);
        if (package.Length == 0 || package.Length > NestJobRules.MaxPackageBytes)
            throw new ArgumentException("The nest job package is empty or too large.", nameof(package));

        using var content = new MultipartFormDataContent();
        content.Add(new StringContent(JsonSerializer.Serialize(submission, NestServerHttp.JsonOptions), Encoding.UTF8, "application/json"), "metadata");
        var file = new ByteArrayContent(package);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
        content.Add(file, "file", "job.zip");
        using var response = await _httpClient.PostAsync(Url("api/nest-jobs"), content, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
            throw new ServerTooOldException(Feature);
        await NestServerHttp.EnsureSuccessAsync(response, "submit the nest", cancellationToken);
        return await ReadRecordAsync(response, cancellationToken);
    }

    /// <summary>The job, or null when the server does not hold it (removed after <see cref="NestJobRules.Retention"/>).</summary>
    public async Task<NestJobRecord?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync(Url($"api/nest-jobs/{id}"), cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;
        await NestServerHttp.EnsureSuccessAsync(response, "read the nest job", cancellationToken);
        var record = await ReadRecordAsync(response, cancellationToken);
        return record.Id == id ? record : throw new IOException("Nest server returned a different nest job.");
    }

    /// <summary>A succeeded job's result (a <c>NestSolveTranscript</c> as JSON).</summary>
    public async Task<string> GetResultAsync(Guid id, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync(Url($"api/nest-jobs/{id}/result"), cancellationToken);
        await NestServerHttp.EnsureSuccessAsync(response, "read the nest result", cancellationToken);
        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    /// <summary>The header naming the revision <c>GET .../plan</c> returned.</summary>
    public const string PlanRevisionHeader = "X-Plan-Revision";

    /// <summary>The best plan so far and the revision it was stored at, or null when the job has none.</summary>
    public async Task<(string Plan, long Revision)?> GetPlanAsync(Guid id, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync(Url($"api/nest-jobs/{id}/plan"), cancellationToken);
        if (response.StatusCode is HttpStatusCode.NoContent or HttpStatusCode.NotFound)
            return null;
        await NestServerHttp.EnsureSuccessAsync(response, "read the nest job's plan", cancellationToken);
        var revision = response.Headers.TryGetValues(PlanRevisionHeader, out var values)
            && long.TryParse(values.FirstOrDefault(), NumberStyles.None, CultureInfo.InvariantCulture, out var value) ? value : 0;
        return (await response.Content.ReadAsStringAsync(cancellationToken), revision);
    }

    /// <summary>
    /// The job's event stream: an <see cref="NestJobEventKind.Open"/> event, then each plan and
    /// job change after <paramref name="lastEventId"/> (zero: the current job at once), ending
    /// after the job event that is final, or with <see cref="NestJobEventKind.Gone"/>. A stream
    /// that ends without either, or stays silent for <paramref name="idleTimeout"/>, dropped:
    /// reconnect with the last id seen. A server without the stream raises <see cref="ServerTooOldException"/>.
    /// </summary>
    public async IAsyncEnumerable<NestJobEvent> WatchAsync(Guid id, long lastEventId = 0, TimeSpan? idleTimeout = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, Url($"api/nest-jobs/{id}/events"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        if (lastEventId > 0)
            request.Headers.TryAddWithoutValidation("Last-Event-ID", lastEventId.ToString(CultureInfo.InvariantCulture));
        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            if (await GetAsync(id, cancellationToken) is null)
            {
                yield return new NestJobEvent { Kind = NestJobEventKind.Gone };
                yield break;
            }
            throw new ServerTooOldException("server nesting progress");
        }
        await NestServerHttp.EnsureSuccessAsync(response, "watch the nest job", cancellationToken);
        yield return new NestJobEvent { Kind = NestJobEventKind.Open };

        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var idle = idleTimeout ?? NestJobRules.StreamIdleTimeout;
        var type = "";
        var eventId = 0L;
        var data = new StringBuilder();
        while (true)
        {
            string? line;
            using (var silence = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                silence.CancelAfter(idle);
                try { line = await reader.ReadLineAsync(silence.Token); }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    throw new IOException("The nest server stopped sending job updates.");
                }
            }
            if (line is null)
                yield break;
            if (line.Length > 0)
            {
                if (line[0] == ':')
                    continue;
                var colon = line.IndexOf(':');
                var field = colon < 0 ? line : line[..colon];
                var value = colon < 0 ? "" : line[(colon + 1)..];
                if (value.StartsWith(' '))
                    value = value[1..];
                switch (field)
                {
                    case "event": type = value; break;
                    case "id": long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out eventId); break;
                    case "data":
                        if (data.Length > 0)
                            data.Append('\n');
                        data.Append(value);
                        break;
                }
                continue;
            }

            // A blank line dispatches the event.
            var text = data.ToString();
            var kind = type;
            data.Clear();
            type = "";
            switch (kind)
            {
                case "job":
                    var job = JsonSerializer.Deserialize<NestJobRecord>(text, NestServerHttp.JsonOptions)
                        ?? throw new IOException("Nest server sent an empty nest job.");
                    yield return new NestJobEvent { Kind = NestJobEventKind.Job, Id = eventId, Job = job };
                    break;
                case "plan":
                    yield return new NestJobEvent { Kind = NestJobEventKind.Plan, Id = eventId, Plan = text };
                    break;
                case "gone":
                    yield return new NestJobEvent { Kind = NestJobEventKind.Gone, Id = eventId };
                    yield break;
            }
        }
    }

    /// <summary>Stops the job: a queued job is cancelled at once, a running one at its worker's next heartbeat.</summary>
    public async Task<NestJobRecord?> CancelAsync(Guid id, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.PostAsync(Url($"api/nest-jobs/{id}/cancel"), null, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;
        await NestServerHttp.EnsureSuccessAsync(response, "stop the nest job", cancellationToken);
        return await ReadRecordAsync(response, cancellationToken);
    }

    /// <summary>Takes the oldest queued job for <paramref name="workerId"/>, or null when there is
    /// none or another job is still running.</summary>
    public async Task<NestJobRecord?> ClaimAsync(string workerId, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.PostAsJsonAsync(Url("api/nest-jobs/claim"),
            new NestJobWorkerMessage { WorkerId = workerId }, NestServerHttp.JsonOptions, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NoContent)
            return null;
        if (response.StatusCode == HttpStatusCode.NotFound)
            throw new ServerTooOldException(Feature);
        await NestServerHttp.EnsureSuccessAsync(response, "claim a nest job", cancellationToken);
        return await ReadRecordAsync(response, cancellationToken);
    }

    public async Task<byte[]> GetPackageAsync(Guid id, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync(Url($"api/nest-jobs/{id}/package"), cancellationToken);
        await NestServerHttp.EnsureSuccessAsync(response, "read the nest job package", cancellationToken);
        return await response.Content.ReadAsByteArrayAsync(cancellationToken);
    }

    /// <summary>Keeps the job's lease. Null when the worker no longer holds the job.</summary>
    public async Task<NestJobHeartbeat?> HeartbeatAsync(Guid id, string workerId, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.PostAsJsonAsync(Url($"api/nest-jobs/{id}/heartbeat"),
            new NestJobWorkerMessage { WorkerId = workerId }, NestServerHttp.JsonOptions, cancellationToken);
        if (response.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.NotFound)
            return null;
        await NestServerHttp.EnsureSuccessAsync(response, "report on the nest job", cancellationToken);
        return await response.Content.ReadFromJsonAsync<NestJobHeartbeat>(NestServerHttp.JsonOptions, cancellationToken)
            ?? throw new IOException("Nest server returned an empty heartbeat.");
    }

    /// <summary>
    /// Stores the worker's latest progress report (and renews the lease). Null when the worker
    /// no longer holds the job; otherwise whether someone asked to stop it.
    /// </summary>
    public Task<NestJobHeartbeat?> ReportProgressAsync(Guid id, string workerId, string progress, CancellationToken cancellationToken = default) =>
        PutReportAsync($"api/nest-jobs/{id}/progress", workerId, progress, "report the nest job's progress", cancellationToken);

    /// <summary>Stores the best plan so far, as <see cref="ReportProgressAsync"/> does a progress report.</summary>
    public Task<NestJobHeartbeat?> ReportPlanAsync(Guid id, string workerId, string plan, CancellationToken cancellationToken = default) =>
        PutReportAsync($"api/nest-jobs/{id}/plan", workerId, plan, "report the nest job's plan", cancellationToken);

    private async Task<NestJobHeartbeat?> PutReportAsync(string path, string workerId, string json, string action, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(json);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = await _httpClient.PutAsync(Url($"{path}?workerId={Uri.EscapeDataString(workerId)}"), content, cancellationToken);
        if (response.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.NotFound)
            return null;
        await NestServerHttp.EnsureSuccessAsync(response, action, cancellationToken);
        return await response.Content.ReadFromJsonAsync<NestJobHeartbeat>(NestServerHttp.JsonOptions, cancellationToken)
            ?? throw new IOException("Nest server returned an empty answer.");
    }

    /// <summary>Stores the result. False when the worker no longer holds the job.</summary>
    public async Task<bool> CompleteAsync(Guid id, string workerId, string result, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(result);
        using var content = new StringContent(result, Encoding.UTF8, "application/json");
        using var response = await _httpClient.PutAsync(
            Url($"api/nest-jobs/{id}/result?workerId={Uri.EscapeDataString(workerId)}"), content, cancellationToken);
        return await Held(response, "store the nest result", cancellationToken);
    }

    public async Task<bool> FailAsync(Guid id, string workerId, string error, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.PostAsJsonAsync(Url($"api/nest-jobs/{id}/fail"),
            new NestJobWorkerMessage { WorkerId = workerId, Error = error }, NestServerHttp.JsonOptions, cancellationToken);
        return await Held(response, "report the failed nest job", cancellationToken);
    }

    /// <summary>The worker stopped the job as asked.</summary>
    public async Task<bool> ConfirmCancelledAsync(Guid id, string workerId, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.PostAsJsonAsync(Url($"api/nest-jobs/{id}/cancelled"),
            new NestJobWorkerMessage { WorkerId = workerId }, NestServerHttp.JsonOptions, cancellationToken);
        return await Held(response, "report the stopped nest job", cancellationToken);
    }

    /// <summary>Gives the job back to the queue without spending an attempt (the worker is shutting down).</summary>
    public async Task<bool> ReleaseAsync(Guid id, string workerId, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.PostAsJsonAsync(Url($"api/nest-jobs/{id}/release"),
            new NestJobWorkerMessage { WorkerId = workerId }, NestServerHttp.JsonOptions, cancellationToken);
        return await Held(response, "give back the nest job", cancellationToken);
    }

    private static async Task<bool> Held(HttpResponseMessage response, string action, CancellationToken cancellationToken)
    {
        if (response.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.NotFound)
            return false;
        await NestServerHttp.EnsureSuccessAsync(response, action, cancellationToken);
        return true;
    }

    private static async Task<NestJobRecord> ReadRecordAsync(HttpResponseMessage response, CancellationToken cancellationToken) =>
        await response.Content.ReadFromJsonAsync<NestJobRecord>(NestServerHttp.JsonOptions, cancellationToken)
        ?? throw new IOException("Nest server returned an empty nest job.");
}
