using System.Net;
using System.Net.Http.Json;

namespace OpenNest.Data;

/// <summary>
/// Talks to the settings every workstation shares on the nest server (<c>/api/settings</c>).
/// A server without them raises <see cref="ServerTooOldException"/>; a stale write raises
/// <see cref="SharedSettingSaveConflictException"/>; other failures surface as <see cref="IOException"/>.
/// </summary>
public sealed class RemoteSharedSettings : IDisposable
{
    private const string Feature = "shared machine settings";

    private readonly HttpClient _httpClient;
    private readonly bool _ownsClient;
    private readonly Uri _baseUri;

    /// <summary>A client that gives up after <paramref name="timeout"/>, so an unreachable server falls back quickly.</summary>
    public RemoteSharedSettings(string baseUrl, TimeSpan timeout)
        : this(new HttpClient { Timeout = timeout }, baseUrl)
    {
        _ownsClient = true;
    }

    /// <summary>For tests and hosts that pool HttpClient instances.</summary>
    public RemoteSharedSettings(HttpClient httpClient, string baseUrl)
    {
        _httpClient = httpClient;
        _baseUri = ServerUrl.ParseBase(baseUrl, nameof(baseUrl));
    }

    private Uri Url(string relative) => new(_baseUri, relative);

    private static string Path(string kind, string key) =>
        $"api/settings/{Uri.EscapeDataString(kind)}/{Uri.EscapeDataString(key)}";

    /// <summary>Every shared settings document.</summary>
    public async Task<IReadOnlyList<SharedSettingRecord>> ListAsync(CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync(Url("api/settings"), cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
            throw new ServerTooOldException(Feature);
        await NestServerHttp.EnsureSuccessAsync(response, "read the shared settings", cancellationToken);
        var records = await response.Content.ReadFromJsonAsync<List<SharedSettingRecord>>(NestServerHttp.JsonOptions, cancellationToken)
            ?? throw new IOException("Nest server returned no shared settings.");
        if (records.Any(record => record is null || record.Revision < 1 || string.IsNullOrEmpty(record.Json)))
            throw new IOException("Nest server returned invalid shared settings.");
        // A newer server may share kinds this version does not use.
        return records.Where(record => SharedSettingKinds.IsKnown(record.Kind)).ToList();
    }

    /// <summary>
    /// Stores a new document. A different document already stored raises
    /// <see cref="SharedSettingSaveConflictException"/> with it.
    /// </summary>
    public async Task<SharedSettingRecord> CreateAsync(string kind, string key, SharedSettingWrite write,
        CancellationToken cancellationToken = default)
    {
        Validate(kind, key, write);
        using var response = await _httpClient.PostAsync(Url(Path(kind, key)),
            JsonContent.Create(write, options: NestServerHttp.JsonOptions), cancellationToken);
        return await ReadSaved(response, kind, key, $"save the shared {kind} settings", cancellationToken);
    }

    /// <summary>
    /// Replaces the document stored at <paramref name="expectedRevision"/>. A newer one raises
    /// <see cref="SharedSettingSaveConflictException"/>; one that is gone raises <see cref="SharedSettingMissingException"/>.
    /// </summary>
    public async Task<SharedSettingRecord> UpdateAsync(string kind, string key, SharedSettingWrite write, int expectedRevision,
        CancellationToken cancellationToken = default)
    {
        Validate(kind, key, write);
        using var request = new HttpRequestMessage(HttpMethod.Put, Url(Path(kind, key)))
        {
            Content = JsonContent.Create(write, options: NestServerHttp.JsonOptions),
        };
        NestServerHttp.AddIfMatch(request, expectedRevision);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
            throw new SharedSettingMissingException(kind, key);
        return await ReadSaved(response, kind, key, $"save the shared {kind} settings", cancellationToken);
    }

    /// <summary>Deletes the document stored at <paramref name="expectedRevision"/>; one already gone is not an error.</summary>
    public async Task DeleteAsync(string kind, string key, string savedBy, int expectedRevision,
        CancellationToken cancellationToken = default)
    {
        if (SharedSettingRules.NormalizeKey(kind, key) is null)
            throw new ArgumentException($"Invalid {kind} settings key.", nameof(key));
        using var request = new HttpRequestMessage(HttpMethod.Delete,
            Url(Path(kind, key) + "?savedBy=" + Uri.EscapeDataString(savedBy ?? "")));
        NestServerHttp.AddIfMatch(request, expectedRevision);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return;
        if (response.StatusCode == HttpStatusCode.Conflict)
            throw new SharedSettingSaveConflictException(await ReadRecord(response, cancellationToken));
        await NestServerHttp.EnsureSuccessAsync(response, $"delete the shared {kind} settings", cancellationToken);
    }

    private static void Validate(string kind, string key, SharedSettingWrite write)
    {
        if (SharedSettingRules.GetError(kind, key, write) is { } error)
            throw new ArgumentException(error, nameof(write));
    }

    private static async Task<SharedSettingRecord> ReadSaved(HttpResponseMessage response, string kind, string key, string action,
        CancellationToken cancellationToken)
    {
        if (response.StatusCode == HttpStatusCode.Conflict)
            throw new SharedSettingSaveConflictException(await ReadRecord(response, cancellationToken));
        if (response.StatusCode == HttpStatusCode.NotFound)
            throw new ServerTooOldException(Feature);
        await NestServerHttp.EnsureSuccessAsync(response, action, cancellationToken);
        var saved = await ReadRecord(response, cancellationToken);
        if (saved.Kind != kind || saved.Key != SharedSettingRules.NormalizeKey(kind, key) || saved.Revision < 1)
            throw new IOException("The nest server returned different settings than were saved.");
        return saved;
    }

    private static async Task<SharedSettingRecord> ReadRecord(HttpResponseMessage response, CancellationToken cancellationToken) =>
        await response.Content.ReadFromJsonAsync<SharedSettingRecord>(NestServerHttp.JsonOptions, cancellationToken)
        ?? throw new IOException("Nest server returned empty settings.");

    public void Dispose()
    {
        if (_ownsClient)
            _httpClient.Dispose();
    }
}

/// <summary>A shared settings write found a newer or different stored document; nothing was changed.</summary>
public sealed class SharedSettingSaveConflictException : IOException
{
    public SharedSettingSaveConflictException(SharedSettingRecord current)
        : base(string.IsNullOrEmpty(current.SavedBy)
            ? $"These settings were changed on another workstation at {current.SavedAt:g}."
            : $"These settings were changed by {current.SavedBy} at {current.SavedAt:g}.")
    {
        Current = current;
    }

    public SharedSettingRecord Current { get; }
}

/// <summary>A shared settings update found the document deleted; nothing was changed.</summary>
public sealed class SharedSettingMissingException(string kind, string key)
    : IOException($"The shared {kind} settings '{key}' were deleted on another workstation.")
{
}
