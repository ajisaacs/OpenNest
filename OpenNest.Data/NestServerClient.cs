using System.Net;
using System.Net.Http.Json;

namespace OpenNest.Data;

/// <summary>The connection every nest server client holds: its HTTP client and the server's base address.</summary>
public abstract class NestServerClient : IDisposable
{
    private protected readonly HttpClient _httpClient;
    private readonly bool _ownsClient;
    private readonly Uri _baseUri;

    private protected NestServerClient(HttpClient httpClient, string baseUrl, bool ownsClient)
    {
        _httpClient = httpClient;
        _ownsClient = ownsClient;
        _baseUri = ServerUrl.ParseBase(baseUrl, nameof(baseUrl));
    }

    private protected Uri Url(string relative) => new(_baseUri, relative);

    /// <summary>A request that applies only while the stored revision is still <paramref name="expectedRevision"/>.</summary>
    private protected HttpRequestMessage Guarded(HttpMethod method, string relative, int expectedRevision)
    {
        var request = new HttpRequestMessage(method, Url(relative));
        NestServerHttp.AddIfMatch(request, expectedRevision);
        return request;
    }

    /// <summary>
    /// Probes a route every server with the feature answers; a 404 there means the server
    /// predates the feature, so <paramref name="tooOld"/> is raised.
    /// </summary>
    private protected async Task ThrowIfRouteMissing(string probe, Func<ServerTooOldException> tooOld,
        CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(Url(probe), cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
            throw tooOld();
    }

    private protected static void ThrowIfInvalidQuery(string? error)
    {
        if (error is not null)
            throw new ArgumentException(error, "query");
    }

    /// <summary>
    /// One page of a filtered browse: a 404 raises <paramref name="tooOld"/>, and a page that is
    /// missing, over <paramref name="limit"/>, negative or holds a null item raises
    /// <see cref="IOException"/> naming <paramref name="pageName"/>.
    /// </summary>
    private protected async Task<TPage> QueryPageAsync<TPage, TItem>(string relative, int limit, string action,
        string pageName, Func<ServerTooOldException> tooOld, Func<TPage, IReadOnlyCollection<TItem>?> items,
        CancellationToken cancellationToken)
        where TPage : class, IBrowsePage
        where TItem : class
    {
        using var response = await _httpClient.GetAsync(Url(relative), cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
            throw tooOld();
        await NestServerHttp.EnsureSuccessAsync(response, action, cancellationToken);
        var page = await response.Content.ReadFromJsonAsync<TPage>(NestServerHttp.JsonOptions, cancellationToken)
            ?? throw new IOException($"Nest server returned an empty {pageName} page.");
        var list = items(page);
        if (list is null || list.Count > limit || page.Total < 0 || list.Any(item => item is null))
            throw new IOException($"Nest server returned an invalid {pageName} page for a page limited to {limit}.");
        return page;
    }

    /// <summary>
    /// Sends a <see cref="Guarded"/> write and reads the stored record back. A 404 runs
    /// <paramref name="throwIfServerTooOld"/> and otherwise reports the record gone; a 409 raises
    /// <paramref name="conflict"/> with the current record; a reply for another record or an
    /// older revision raises <see cref="IOException"/> with <paramref name="invalidMessage"/>.
    /// </summary>
    private protected async Task<TRecord> SendGuardedAsync<TRecord>(HttpRequestMessage request, Guid id, int expectedRevision,
        string action, string missingMessage, string invalidMessage,
        Func<HttpResponseMessage, CancellationToken, Task<TRecord>> read,
        Func<TRecord, Exception> conflict, Func<TRecord, (Guid Id, int Revision)> identity,
        Func<CancellationToken, Task> throwIfServerTooOld, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            await throwIfServerTooOld(cancellationToken);
            throw new IOException(missingMessage);
        }
        if (response.StatusCode == HttpStatusCode.Conflict)
            throw conflict(await read(response, cancellationToken));
        await NestServerHttp.EnsureSuccessAsync(response, action, cancellationToken);
        var saved = await read(response, cancellationToken);
        var (savedId, savedRevision) = identity(saved);
        if (savedId != id || savedRevision < expectedRevision)
            throw new IOException(invalidMessage);
        return saved;
    }

    public void Dispose()
    {
        if (_ownsClient)
            _httpClient.Dispose();
    }
}
