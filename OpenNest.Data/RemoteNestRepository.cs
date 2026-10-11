using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenNest.Data;

/// <summary>
/// Talks to the central OpenNest nest server over HTTP. Uploads are multipart:
/// a "metadata" JSON part and a "file" part holding the .nest archive.
/// Connection and HTTP failures surface as HttpRequestException/IOException to the caller.
/// </summary>
public sealed class RemoteNestRepository : INestRepository, IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private readonly HttpClient _httpClient;
    private readonly bool _ownsClient;
    private readonly Uri _baseUri;

    public RemoteNestRepository(string baseUrl)
        : this(new HttpClient(), baseUrl)
    {
        _ownsClient = true;
    }

    /// <summary>For tests and hosts that pool HttpClient instances.</summary>
    public RemoteNestRepository(HttpClient httpClient, string baseUrl)
    {
        _httpClient = httpClient;
        _ownsClient = false;
        var url = (baseUrl ?? "").Trim().TrimEnd('/');
        if (!Uri.TryCreate(url, UriKind.Absolute, out var baseUri)
            || (baseUri.Scheme != Uri.UriSchemeHttp && baseUri.Scheme != Uri.UriSchemeHttps))
        {
            throw new ArgumentException($"Invalid nest server URL: '{baseUrl}'", nameof(baseUrl));
        }
        // Keep the trailing slash so relative combines replace no path segment.
        _baseUri = new Uri(baseUri.AbsoluteUri.TrimEnd('/') + "/");
    }

    private Uri Url(string relative) => new(_baseUri, relative);

    public async Task<IReadOnlyList<NestRecord>> ListAsync(CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync(Url("api/nests"), cancellationToken);
        await EnsureSuccess(response, "list nests", cancellationToken);
        var items = await response.Content
            .ReadFromJsonAsync<List<NestRecord>>(JsonOptions, cancellationToken);
        return items ?? new List<NestRecord>();
    }

    public Task<NestPage> QueryAsync(NestQuery query, CancellationToken cancellationToken = default) =>
        QueryCoreAsync(query, false, cancellationToken);

    public Task<NestPage> QueryDeletedAsync(NestQuery query, CancellationToken cancellationToken = default) =>
        QueryCoreAsync(query, true, cancellationToken);

    private async Task<NestPage> QueryCoreAsync(NestQuery query, bool deleted, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var error = query.GetValidationError();
        if (error is not null)
            throw new ArgumentException(error, nameof(query));

        using var response = await _httpClient.GetAsync(
            Url((deleted ? "api/nests/deleted/query?" : "api/nests/query?") + BuildQueryString(query)), cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            throw new IOException(
                "This nest server does not support filtered browsing (HTTP 404). Update the nest server to this OpenNest version.");
        }

        await EnsureSuccess(response, "query nests", cancellationToken);
        var page = await response.Content.ReadFromJsonAsync<NestPage>(JsonOptions, cancellationToken)
            ?? throw new IOException("Nest server returned an empty page.");
        if (page.Items.Count > query.Limit)
        {
            throw new IOException(
                $"Nest server returned {page.Items.Count} records for a page limited to {query.Limit}.");
        }

        return page;
    }

    private static string BuildQueryString(NestQuery query) =>
        "search=" + Uri.EscapeDataString(query.NormalizedSearch)
        + "&sort=" + JsonNamingPolicy.CamelCase.ConvertName(query.Sort.ToString())
        + "&order=" + (query.Descending ? "desc" : "asc")
        + "&offset=" + query.Offset.ToString(CultureInfo.InvariantCulture)
        + "&limit=" + query.Limit.ToString(CultureInfo.InvariantCulture);

    public Task<NestRecord?> GetMetadataAsync(Guid id, CancellationToken cancellationToken = default) =>
        GetMetadataCoreAsync(id, false, cancellationToken);

    public Task<NestRecord?> GetDeletedMetadataAsync(Guid id, CancellationToken cancellationToken = default) =>
        GetMetadataCoreAsync(id, true, cancellationToken);

    private async Task<NestRecord?> GetMetadataCoreAsync(Guid id, bool deleted, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(Url($"api/nests/{(deleted ? "deleted/" : "")}{id}"), cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;
        await EnsureSuccess(response, $"get nest {id}", cancellationToken);
        return await response.Content.ReadFromJsonAsync<NestRecord>(JsonOptions, cancellationToken);
    }

    public Task<byte[]?> GetFileAsync(Guid id, CancellationToken cancellationToken = default) =>
        GetFileCoreAsync(id, false, cancellationToken);

    public Task<byte[]?> GetDeletedFileAsync(Guid id, CancellationToken cancellationToken = default) =>
        GetFileCoreAsync(id, true, cancellationToken);

    private async Task<byte[]?> GetFileCoreAsync(Guid id, bool deleted, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(Url($"api/nests/{(deleted ? "deleted/" : "")}{id}/file"), cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;
        await EnsureSuccess(response, $"download nest {id}", cancellationToken);
        return await response.Content.ReadAsByteArrayAsync(cancellationToken);
    }

    public async Task<NestRecord> UploadAsync(
        byte[] nestFile, NestRecord record, CancellationToken cancellationToken = default)
    {
        using var content = BuildMultipart(nestFile, record);
        using var response = await _httpClient.PostAsync(Url("api/nests"), content, cancellationToken);
        await EnsureSuccess(response, $"upload nest '{record.Name}'", cancellationToken);
        return await ReadRecord(response, cancellationToken);
    }

    public async Task<NestRecord> UpdateFileAsync(
        Guid id, byte[] nestFile, NestRecord record, CancellationToken cancellationToken = default)
    {
        return await UpdateFileAsync(id, nestFile, record, record.Revision, cancellationToken);
    }

    public async Task<NestRecord> UpdateMetadataAsync(
        Guid id, NestRecord record, CancellationToken cancellationToken = default)
    {
        return await UpdateMetadataAsync(id, record, record.Revision, cancellationToken);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;
        throw new IOException("Deletion requires guarded soft-delete support and is not available yet. Upgrade the client before deleting a server nest.");
    }

    public Task<NestRevisionPage> GetRevisionsAsync(Guid id, int offset = 0, int limit = 100,
        CancellationToken cancellationToken = default) => GetHistoryAsync(id, offset, limit, false, cancellationToken);

    public Task<NestRevisionPage> GetDeletedRevisionsAsync(Guid id, int offset = 0, int limit = 100,
        CancellationToken cancellationToken = default) => GetHistoryAsync(id, offset, limit, true, cancellationToken);

    private async Task<NestRevisionPage> GetHistoryAsync(Guid id, int offset, int limit, bool deleted,
        CancellationToken cancellationToken)
    {
        ValidateId(id);
        if (offset < 0) throw new ArgumentOutOfRangeException(nameof(offset));
        if (limit < 1 || limit > NestRevisionPage.MaxLimit) throw new ArgumentOutOfRangeException(nameof(limit));
        using var response = await _httpClient.GetAsync(
            Url($"api/nests/{(deleted ? "deleted/" : "")}{id}/revisions?offset={offset.ToString(CultureInfo.InvariantCulture)}&limit={limit.ToString(CultureInfo.InvariantCulture)}"), cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
            throw new IOException("Nest history is unavailable or this nest no longer exists. Upgrade the nest server if it does not support version history, then reload.");
        await EnsureSuccess(response, $"read nest history {id}", cancellationToken);
        var page = await response.Content.ReadFromJsonAsync<NestRevisionPage>(JsonOptions, cancellationToken)
            ?? throw new IOException("Nest server returned an empty history page.");
        if (page.Items is null || page.Items.Count > limit || page.Offset != offset || page.Limit != limit
            || page.Total < 0 || page.Items.Count > System.Math.Max(0L, (long)page.Total - offset)
            || page.Items.Any(item => item is null || item.Revision < 1 || item.Id != id)
            || page.Items.Zip(page.Items.Skip(1)).Any(pair => pair.First.Revision <= pair.Second.Revision))
            throw new IOException("Nest server returned an invalid or unbounded history page.");
        return page;
    }

    public Task<byte[]?> GetRevisionFileAsync(Guid id, int revisionNumber,
        CancellationToken cancellationToken = default) => GetHistoricalFileAsync(id, revisionNumber, false, cancellationToken);

    public Task<byte[]?> GetDeletedRevisionFileAsync(Guid id, int revisionNumber,
        CancellationToken cancellationToken = default) => GetHistoricalFileAsync(id, revisionNumber, true, cancellationToken);

    private async Task<byte[]?> GetHistoricalFileAsync(Guid id, int revisionNumber, bool deleted,
        CancellationToken cancellationToken)
    {
        ValidateId(id);
        ValidateRevision(revisionNumber);
        using var response = await _httpClient.GetAsync(Url($"api/nests/{(deleted ? "deleted/" : "")}{id}/revisions/{revisionNumber.ToString(CultureInfo.InvariantCulture)}/file"), cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        await EnsureSuccess(response, $"download nest {id} revision {revisionNumber}", cancellationToken);
        return await response.Content.ReadAsByteArrayAsync(cancellationToken);
    }

    public async Task<NestRecord> UpdateFileAsync(Guid id, byte[] nestFile, NestRecord record, int expectedRevision,
        CancellationToken cancellationToken = default)
    {
        ValidateId(id);
        if (expectedRevision < 1)
            throw new IOException("This nest has no known revision. Upgrade the nest server and reload before saving, or Save As a new nest.");
        // A legacy server can ignore If-Match and overwrite data. Prove history support
        // before sending any mutation; never infer revision 1 from a legacy read.
        var history = await GetRevisionsAsync(id, limit: 1, cancellationToken: cancellationToken);
        if (history.Items.Count == 0)
            throw new IOException("Nest server returned no revision history. Upgrade the nest server and reload before saving.");
        using var request = new HttpRequestMessage(HttpMethod.Put, Url($"api/nests/{id}/file"));
        request.Headers.IfMatch.Add(new EntityTagHeaderValue($"\"{expectedRevision.ToString(CultureInfo.InvariantCulture)}\""));
        request.Content = BuildMultipart(nestFile, record);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccess(response, $"update nest {id}; reload the current version or Save As after a conflict", cancellationToken);
        var saved = await ReadRecord(response, cancellationToken);
        if (saved.Id != id || saved.Revision < expectedRevision)
            throw new IOException("Nest server returned an invalid saved revision. Reload to reconcile before saving again.");
        return saved;
    }

    public async Task<NestRecord> UpdateMetadataAsync(Guid id, NestRecord record, int expectedRevision,
        CancellationToken cancellationToken = default)
    {
        ValidateId(id);
        ValidateRevision(expectedRevision);
        await GetRevisionsAsync(id, limit: 1, cancellationToken: cancellationToken);
        using var request = new HttpRequestMessage(HttpMethod.Put, Url($"api/nests/{id}/metadata"));
        request.Headers.IfMatch.Add(new EntityTagHeaderValue($"\"{expectedRevision.ToString(CultureInfo.InvariantCulture)}\""));
        request.Content = JsonContent.Create(record, options: JsonOptions);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccess(response, $"edit nest {id}; reload after a conflict", cancellationToken);
        return await ReadRecord(response, cancellationToken);
    }

    public Task<NestRecord> DeleteAsync(Guid id, int expectedRevision, CancellationToken cancellationToken = default) =>
        ChangeVisibilityAsync(id, expectedRevision, true, cancellationToken);

    public Task<NestRecord> RecoverAsync(Guid id, int expectedRevision, CancellationToken cancellationToken = default) =>
        ChangeVisibilityAsync(id, expectedRevision, false, cancellationToken);

    private async Task<NestRecord> ChangeVisibilityAsync(Guid id, int expectedRevision, bool deleted,
        CancellationToken cancellationToken)
    {
        ValidateId(id);
        ValidateRevision(expectedRevision);
        // A legacy server cannot be trusted to preserve history on DELETE.
        await GetHistoryAsync(id, 0, 1, !deleted, cancellationToken);
        using var request = new HttpRequestMessage(deleted ? HttpMethod.Delete : HttpMethod.Post,
            Url(deleted ? $"api/nests/{id}" : $"api/nests/deleted/{id}/recover"));
        request.Headers.IfMatch.Add(new EntityTagHeaderValue($"\"{expectedRevision.ToString(CultureInfo.InvariantCulture)}\""));
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccess(response, $"change nest visibility {id}; reload after a conflict", cancellationToken);
        return await ReadRecord(response, cancellationToken);
    }

    private static void ValidateId(Guid id)
    {
        if (id == Guid.Empty) throw new ArgumentException("A nest id is required.", nameof(id));
    }

    private static void ValidateRevision(int revisionNumber)
    {
        if (revisionNumber < 1) throw new ArgumentOutOfRangeException(nameof(revisionNumber));
    }

    public void Dispose()
    {
        if (_ownsClient)
            _httpClient.Dispose();
    }

    private static MultipartFormDataContent BuildMultipart(byte[] nestFile, NestRecord record)
    {
        var content = new MultipartFormDataContent();
        var metadata = new StringContent(
            JsonSerializer.Serialize(record, JsonOptions), Encoding.UTF8, "application/json");
        content.Add(metadata, "metadata");
        var file = new ByteArrayContent(nestFile);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
        content.Add(file, "file", record.Name.Length > 0 ? $"{record.Name}.nest" : "nest.nest");
        return content;
    }

    private static async Task<NestRecord> ReadRecord(
        HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var record = await response.Content.ReadFromJsonAsync<NestRecord>(JsonOptions, cancellationToken);
        return record ?? throw new IOException("Nest server returned an empty record.");
    }

    private static async Task EnsureSuccess(
        HttpResponseMessage response, string action, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
            return;

        var detail = "";
        try
        {
            detail = await response.Content.ReadAsStringAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        {
            // The status code is the useful part; the body is best-effort.
        }

        if (detail.Length > 200)
            detail = detail[..200] + "…";
        throw new IOException(
            $"Could not {action} on the nest server: HTTP {(int)response.StatusCode} {response.ReasonPhrase}. {detail}".TrimEnd());
    }
}
