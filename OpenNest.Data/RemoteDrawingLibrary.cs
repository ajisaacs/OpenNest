using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenNest.Data;

/// <summary>
/// Talks to the drawing library on the nest server (<c>/api/drawings</c>). Uploads are
/// multipart: a "metadata" JSON part and a "package" part (a drawing package). Requests
/// are checked against <see cref="DrawingLibraryRules"/> before sending. A server without
/// the library raises <see cref="ServerTooOldException"/>; this client never falls back
/// to another store. Other failures surface as <see cref="IOException"/>.
/// </summary>
public sealed class RemoteDrawingLibrary : IDisposable
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

    public RemoteDrawingLibrary(string baseUrl)
        : this(new HttpClient(), baseUrl)
    {
        _ownsClient = true;
    }

    /// <summary>For tests and hosts that pool HttpClient instances.</summary>
    public RemoteDrawingLibrary(HttpClient httpClient, string baseUrl)
    {
        _httpClient = httpClient;
        _baseUri = ServerUrl.ParseBase(baseUrl, nameof(baseUrl));
    }

    private Uri Url(string relative) => new(_baseUri, relative);

    /// <summary>
    /// Stores revision 1 under <c>record.Id</c>, which the caller allocates first so an
    /// ambiguous failure can be reconciled by id. An existing id raises
    /// <see cref="DrawingLibraryConflictException"/> with the stored record.
    /// </summary>
    public async Task<DrawingLibraryRecord> UploadAsync(DrawingLibraryRecord record, byte[] package,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        ValidateId(record.Id);
        ValidateUpload(record, package);
        using var content = BuildMultipart(record, package);
        using var response = await _httpClient.PostAsync(Url("api/drawings"), content, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
            throw new ServerTooOldException();
        if (response.StatusCode == HttpStatusCode.Conflict)
            throw new DrawingLibraryConflictException(await ReadRecord(response, cancellationToken));
        await EnsureSuccess(response, $"save drawing '{record.Name}' to the library", cancellationToken);
        var saved = await ReadRecord(response, cancellationToken);
        if (saved.Id != record.Id || saved.Revision != 1 || saved.PackageSha256 != Sha256(package))
            throw new IOException("The nest server returned a different drawing than was saved. Reload the library before saving again.");
        return saved;
    }

    public async Task<DrawingLibraryPage> QueryAsync(DrawingLibraryQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var error = query.GetValidationError();
        if (error is not null)
            throw new ArgumentException(error, nameof(query));

        using var response = await _httpClient.GetAsync(Url("api/drawings/query?" + BuildQueryString(query)), cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
            throw new ServerTooOldException();
        await EnsureSuccess(response, "browse the drawing library", cancellationToken);
        var page = await response.Content.ReadFromJsonAsync<DrawingLibraryPage>(JsonOptions, cancellationToken)
            ?? throw new IOException("Nest server returned an empty drawing page.");
        if (page.Items is null || page.Items.Count > query.Limit || page.Total < 0 || page.Items.Any(item => item is null))
            throw new IOException($"Nest server returned an invalid drawing page for a page limited to {query.Limit}.");
        return page;
    }

    /// <summary>The current record, including an archived one, or null for an unknown id.</summary>
    public async Task<DrawingLibraryRecord?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        ValidateId(id);
        using var response = await _httpClient.GetAsync(Url($"api/drawings/{id}"), cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            await ThrowIfServerTooOld(cancellationToken);
            return null;
        }
        await EnsureSuccess(response, $"read library drawing {id}", cancellationToken);
        var record = await ReadRecord(response, cancellationToken);
        return record.Id == id ? record : throw new IOException("Nest server returned a different library drawing.");
    }

    /// <summary>The current package bytes, or null for an unknown id.</summary>
    public Task<byte[]?> GetPackageAsync(Guid id, CancellationToken cancellationToken = default)
    {
        ValidateId(id);
        return GetBytesAsync($"api/drawings/{id}/file", $"download library drawing {id}", cancellationToken);
    }

    /// <summary>One page of immutable revisions, newest first, or null for an unknown id.</summary>
    public async Task<DrawingLibraryPage?> GetRevisionsAsync(Guid id, int offset = 0,
        int limit = DrawingLibraryRules.DefaultRevisionLimit, CancellationToken cancellationToken = default)
    {
        ValidateId(id);
        if (offset < 0) throw new ArgumentOutOfRangeException(nameof(offset));
        if (limit < 1 || limit > DrawingLibraryRules.MaxRevisionLimit) throw new ArgumentOutOfRangeException(nameof(limit));
        using var response = await _httpClient.GetAsync(
            Url($"api/drawings/{id}/revisions?offset={offset.ToString(CultureInfo.InvariantCulture)}&limit={limit.ToString(CultureInfo.InvariantCulture)}"),
            cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            await ThrowIfServerTooOld(cancellationToken);
            return null;
        }
        await EnsureSuccess(response, $"read library drawing history {id}", cancellationToken);
        var page = await response.Content.ReadFromJsonAsync<DrawingLibraryPage>(JsonOptions, cancellationToken)
            ?? throw new IOException("Nest server returned an empty drawing history page.");
        if (page.Items is null || page.Items.Count > limit || page.Offset != offset || page.Limit != limit
            || page.Total < 0 || page.Items.Count > System.Math.Max(0L, (long)page.Total - offset)
            || page.Items.Any(item => item is null || item.Revision < 1 || item.Id != id)
            || page.Items.Zip(page.Items.Skip(1)).Any(pair => pair.First.Revision <= pair.Second.Revision))
            throw new IOException("Nest server returned an invalid or unbounded drawing history page.");
        return page;
    }

    /// <summary>The exact package of one revision, or null when the drawing or revision is unknown.</summary>
    public Task<byte[]?> GetRevisionPackageAsync(Guid id, int revision, CancellationToken cancellationToken = default)
    {
        ValidateId(id);
        ValidateRevision(revision);
        return GetBytesAsync($"api/drawings/{id}/revisions/{revision.ToString(CultureInfo.InvariantCulture)}/file",
            $"download library drawing {id} revision {revision}", cancellationToken);
    }

    /// <summary>Saves new geometry and metadata as the next revision when <paramref name="expectedRevision"/> is current.</summary>
    public async Task<DrawingLibraryRecord> UpdatePackageAsync(Guid id, DrawingLibraryRecord record, byte[] package,
        int expectedRevision, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        ValidateId(id);
        ValidateRevision(expectedRevision);
        ValidateUpload(record, package);
        using var request = Guarded(HttpMethod.Put, $"api/drawings/{id}/file", expectedRevision);
        request.Content = BuildMultipart(record, package);
        var saved = await SendGuarded(request, id, expectedRevision, $"save library drawing {id}", cancellationToken);
        if (saved.PackageSha256 != Sha256(package))
            throw new IOException("The nest server stored different drawing geometry. Reload the library drawing before saving again.");
        return saved;
    }

    /// <summary>Saves new metadata, keeping the geometry, as the next revision.</summary>
    public async Task<DrawingLibraryRecord> UpdateMetadataAsync(Guid id, DrawingLibraryRecord record, int expectedRevision,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        ValidateId(id);
        ValidateRevision(expectedRevision);
        var error = DrawingLibraryRules.GetMetadataError(record);
        if (error is not null)
            throw new ArgumentException(error, nameof(record));
        using var request = Guarded(HttpMethod.Put, $"api/drawings/{id}/metadata", expectedRevision);
        request.Content = JsonContent.Create(record, options: JsonOptions);
        return await SendGuarded(request, id, expectedRevision, $"edit library drawing {id}", cancellationToken);
    }

    public Task<DrawingLibraryRecord> ArchiveAsync(Guid id, int expectedRevision, CancellationToken cancellationToken = default) =>
        SetArchivedAsync(id, expectedRevision, "archive", cancellationToken);

    public Task<DrawingLibraryRecord> RestoreAsync(Guid id, int expectedRevision, CancellationToken cancellationToken = default) =>
        SetArchivedAsync(id, expectedRevision, "restore", cancellationToken);

    private async Task<DrawingLibraryRecord> SetArchivedAsync(Guid id, int expectedRevision, string action,
        CancellationToken cancellationToken)
    {
        ValidateId(id);
        ValidateRevision(expectedRevision);
        using var request = Guarded(HttpMethod.Post, $"api/drawings/{id}/{action}", expectedRevision);
        var changed = await SendGuarded(request, id, expectedRevision, $"{action} library drawing {id}", cancellationToken);
        if (changed.Archived != (action == "archive"))
            throw new IOException($"Nest server did not {action} library drawing {id}. Reload the library.");
        return changed;
    }

    private HttpRequestMessage Guarded(HttpMethod method, string relative, int expectedRevision)
    {
        var request = new HttpRequestMessage(method, Url(relative));
        request.Headers.IfMatch.Add(new EntityTagHeaderValue($"\"{expectedRevision.ToString(CultureInfo.InvariantCulture)}\""));
        return request;
    }

    private async Task<DrawingLibraryRecord> SendGuarded(HttpRequestMessage request, Guid id, int expectedRevision,
        string action, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            await ThrowIfServerTooOld(cancellationToken);
            throw new IOException($"Library drawing {id} no longer exists on the nest server.");
        }
        if (response.StatusCode == HttpStatusCode.Conflict)
            throw new DrawingLibraryConflictException(await ReadRecord(response, cancellationToken));
        await EnsureSuccess(response, action, cancellationToken);
        var saved = await ReadRecord(response, cancellationToken);
        if (saved.Id != id || saved.Revision < expectedRevision)
            throw new IOException("Nest server returned an invalid library drawing revision. Reload the library before saving again.");
        return saved;
    }

    private async Task<byte[]?> GetBytesAsync(string relative, string action, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(Url(relative), cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            await ThrowIfServerTooOld(cancellationToken);
            return null;
        }
        await EnsureSuccess(response, action, cancellationToken);
        return await response.Content.ReadAsByteArrayAsync(cancellationToken);
    }

    // A 404 for one drawing is ambiguous; the query route exists on every library-aware server.
    private async Task ThrowIfServerTooOld(CancellationToken cancellationToken)
    {
        using var probe = await _httpClient.GetAsync(Url("api/drawings/query?limit=1"), cancellationToken);
        if (probe.StatusCode == HttpStatusCode.NotFound)
            throw new ServerTooOldException();
    }

    private static void ValidateUpload(DrawingLibraryRecord record, byte[] package)
    {
        ArgumentNullException.ThrowIfNull(package);
        var error = DrawingLibraryRules.GetMetadataError(record) ?? DrawingLibraryRules.GetPackageError(package);
        if (error is not null)
            throw new ArgumentException(error, nameof(record));
    }

    private static void ValidateId(Guid id)
    {
        if (id == Guid.Empty) throw new ArgumentException("A library drawing id is required.", nameof(id));
    }

    private static void ValidateRevision(int revision)
    {
        if (revision < 1) throw new ArgumentOutOfRangeException(nameof(revision));
    }

    private static string Sha256(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static string BuildQueryString(DrawingLibraryQuery query)
    {
        var text = new StringBuilder()
            .Append("search=").Append(Uri.EscapeDataString(query.NormalizedSearch))
            .Append("&sort=").Append(JsonNamingPolicy.CamelCase.ConvertName(query.Sort.ToString()))
            .Append("&order=").Append(query.Descending ? "desc" : "asc")
            .Append("&offset=").Append(query.Offset.ToString(CultureInfo.InvariantCulture))
            .Append("&limit=").Append(query.Limit.ToString(CultureInfo.InvariantCulture));
        AppendFilter(text, "customer", query.Customer);
        AppendFilter(text, "materialName", query.MaterialName);
        AppendFilter(text, "materialGrade", query.MaterialGrade);
        if (query.Thickness is { } thickness)
            text.Append("&thickness=").Append(DrawingLibraryRules.NormalizeThickness(thickness).ToString("0.####", CultureInfo.InvariantCulture));
        if (query.CutType is { } cutType)
            text.Append("&cutType=").Append(cutType.ToString());
        if (query.IncludeArchived)
            text.Append("&includeArchived=true");
        return text.ToString();
    }

    private static void AppendFilter(StringBuilder text, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            text.Append('&').Append(key).Append('=').Append(Uri.EscapeDataString(value));
    }

    private static MultipartFormDataContent BuildMultipart(DrawingLibraryRecord record, byte[] package)
    {
        var content = new MultipartFormDataContent();
        content.Add(new StringContent(JsonSerializer.Serialize(record, JsonOptions), Encoding.UTF8, "application/json"), "metadata");
        var file = new ByteArrayContent(package);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
        content.Add(file, "package", "drawing.nest");
        return content;
    }

    private static async Task<DrawingLibraryRecord> ReadRecord(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var record = await response.Content.ReadFromJsonAsync<DrawingLibraryRecord>(JsonOptions, cancellationToken);
        return record ?? throw new IOException("Nest server returned an empty library drawing record.");
    }

    private static async Task EnsureSuccess(HttpResponseMessage response, string action, CancellationToken cancellationToken)
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

    public void Dispose()
    {
        if (_ownsClient)
            _httpClient.Dispose();
    }
}

/// <summary>The nest server predates the drawing library (it answered HTTP 404).</summary>
public sealed class ServerTooOldException : IOException
{
    public ServerTooOldException()
        : base("This nest server does not support the drawing library (HTTP 404). Update the nest server to this OpenNest version.")
    {
    }
}

/// <summary>A guarded library write found a newer or different current state; nothing was changed.</summary>
public sealed class DrawingLibraryConflictException : IOException
{
    public DrawingLibraryConflictException(DrawingLibraryRecord current)
        : base(current.Archived
            ? $"Library drawing '{current.Name}' is archived or was changed (current revision {current.Revision}). Reload before saving."
            : $"Library drawing '{current.Name}' was changed by someone else (current revision {current.Revision}). Reload before saving.")
    {
        Current = current;
    }

    /// <summary>The drawing's current server record.</summary>
    public DrawingLibraryRecord Current { get; }
}
