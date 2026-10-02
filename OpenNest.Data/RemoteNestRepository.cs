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

    public async Task<NestPage> QueryAsync(NestQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var error = query.GetValidationError();
        if (error is not null)
            throw new ArgumentException(error, nameof(query));

        using var response = await _httpClient.GetAsync(
            Url("api/nests/query?" + BuildQueryString(query)), cancellationToken);
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
        + "&offset=" + query.Offset.ToString(CultureInfo.InvariantCulture)
        + "&limit=" + query.Limit.ToString(CultureInfo.InvariantCulture);

    public async Task<NestRecord?> GetMetadataAsync(Guid id, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync(Url($"api/nests/{id}"), cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;
        await EnsureSuccess(response, $"get nest {id}", cancellationToken);
        return await response.Content.ReadFromJsonAsync<NestRecord>(JsonOptions, cancellationToken);
    }

    public async Task<byte[]?> GetFileAsync(Guid id, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync(Url($"api/nests/{id}/file"), cancellationToken);
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
        using var content = BuildMultipart(nestFile, record);
        using var response = await _httpClient.PutAsync(Url($"api/nests/{id}/file"), content, cancellationToken);
        await EnsureSuccess(response, $"update nest {id}", cancellationToken);
        return await ReadRecord(response, cancellationToken);
    }

    public async Task<NestRecord> UpdateMetadataAsync(
        Guid id, NestRecord record, CancellationToken cancellationToken = default)
    {
        using var content = new StringContent(
            JsonSerializer.Serialize(record, JsonOptions), Encoding.UTF8, "application/json");
        using var response = await _httpClient.PutAsync(Url($"api/nests/{id}/metadata"), content, cancellationToken);
        await EnsureSuccess(response, $"update nest metadata {id}", cancellationToken);
        return await ReadRecord(response, cancellationToken);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.DeleteAsync(Url($"api/nests/{id}"), cancellationToken);
        await EnsureSuccess(response, $"delete nest {id}", cancellationToken);
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
