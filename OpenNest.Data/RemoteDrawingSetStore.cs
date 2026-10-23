using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenNest.Data;

/// <summary>
/// Talks to the drawing sets and their releases on the nest server (<c>/api/sets</c>,
/// <c>/api/releases</c>). Sets and releases
/// travel as JSON and are checked against <see cref="DrawingSetRules"/> and
/// <see cref="SetReleaseRules"/> before sending. A server without sets raises
/// <see cref="ServerTooOldException"/>; other failures surface as <see cref="IOException"/>.
/// </summary>
public sealed class RemoteDrawingSetStore : IDrawingSetStore, IDisposable
{
    private const string Feature = "drawing sets";

    private const string ReleaseFeature = "releasing several drawing sets at once";

    private const string DrawingReleaseFeature = "releasing library drawings on their own";

    private readonly HttpClient _httpClient;
    private readonly bool _ownsClient;
    private readonly Uri _baseUri;

    public RemoteDrawingSetStore(string baseUrl)
        : this(new HttpClient(), baseUrl)
    {
        _ownsClient = true;
    }

    /// <summary>For tests and hosts that pool HttpClient instances.</summary>
    public RemoteDrawingSetStore(HttpClient httpClient, string baseUrl)
    {
        _httpClient = httpClient;
        _baseUri = ServerUrl.ParseBase(baseUrl, nameof(baseUrl));
    }

    private Uri Url(string relative) => new(_baseUri, relative);

    public async Task<DrawingSetPage> QueryAsync(DrawingSetQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var error = query.GetValidationError();
        if (error is not null)
            throw new ArgumentException(error, nameof(query));

        using var response = await _httpClient.GetAsync(Url("api/sets/query?" + BuildQueryString(query)), cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
            throw new ServerTooOldException(Feature);
        await NestServerHttp.EnsureSuccessAsync(response, "browse drawing sets", cancellationToken);
        var page = await response.Content.ReadFromJsonAsync<DrawingSetPage>(NestServerHttp.JsonOptions, cancellationToken)
            ?? throw new IOException("Nest server returned an empty drawing set page.");
        if (page.Items is null || page.Items.Count > query.Limit || page.Total < 0 || page.Items.Any(item => item is null))
            throw new IOException($"Nest server returned an invalid drawing set page for a page limited to {query.Limit}.");
        return page;
    }

    public async Task<DrawingSetRecord?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        ValidateId(id);
        var record = await GetRecordAsync($"api/sets/{id}", $"read drawing set {id}", cancellationToken);
        if (record is null)
            return null;
        if (record.Id != id || !HasJoinedMembers(record))
            throw new IOException("Nest server returned a different or incomplete drawing set.");
        return record;
    }

    public async Task<DrawingSetPage?> GetRevisionsAsync(Guid id, int offset = 0,
        int limit = DrawingLibraryRules.DefaultRevisionLimit, CancellationToken cancellationToken = default)
    {
        ValidateId(id);
        var page = await GetPageAsync($"api/sets/{id}/revisions", offset, limit, $"read drawing set history {id}", cancellationToken);
        if (page is not null && (page.Items.Any(item => item.Revision < 1 || item.Id != id)
            || page.Items.Zip(page.Items.Skip(1)).Any(pair => pair.First.Revision <= pair.Second.Revision)))
            throw new IOException("Nest server returned an invalid drawing set history page.");
        return page;
    }

    public async Task<DrawingSetRecord?> GetRevisionAsync(Guid id, int revision, CancellationToken cancellationToken = default)
    {
        ValidateId(id);
        ValidateRevision(revision);
        var record = await GetRecordAsync($"api/sets/{id}/revisions/{revision.ToString(CultureInfo.InvariantCulture)}",
            $"read drawing set {id} revision {revision}", cancellationToken);
        if (record is null)
            return null;
        if (record.Id != id || record.Revision != revision || !HasJoinedMembers(record))
            throw new IOException("Nest server returned a different or incomplete drawing set revision.");
        return record;
    }

    public async Task<DrawingSetPage?> GetSetsContainingAsync(Guid drawingId, int offset = 0,
        int limit = DrawingLibraryRules.DefaultRevisionLimit, CancellationToken cancellationToken = default)
    {
        if (drawingId == Guid.Empty) throw new ArgumentException("A library drawing id is required.", nameof(drawingId));
        return await GetPageAsync($"api/drawings/{drawingId}/sets", offset, limit,
            $"list the sets using library drawing {drawingId}", cancellationToken);
    }

    /// <summary>
    /// Stores revision 1 under <c>record.Id</c>, which the caller allocates first so an
    /// ambiguous failure can be reconciled by id. An existing id raises
    /// <see cref="DrawingSetSaveConflictException"/> with the stored record.
    /// </summary>
    public async Task<DrawingSetRecord> CreateAsync(DrawingSetRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        ValidateId(record.Id);
        ValidateSet(record);
        using var response = await _httpClient.PostAsync(Url("api/sets"),
            JsonContent.Create(ForSave(record), options: NestServerHttp.JsonOptions), cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
            throw new ServerTooOldException(Feature);
        if (response.StatusCode == HttpStatusCode.Conflict)
            throw new DrawingSetSaveConflictException(await ReadSet(response, cancellationToken));
        await NestServerHttp.EnsureSuccessAsync(response, $"save drawing set '{record.Name}'", cancellationToken);
        var saved = await ReadSet(response, cancellationToken);
        if (saved.Id != record.Id || saved.Revision != 1 || !SameMembers(saved, record))
            throw new IOException("The nest server returned a different drawing set than was saved. Reload the set before saving again.");
        return saved;
    }

    public async Task<DrawingSetRecord> UpdateAsync(Guid id, DrawingSetRecord record, int expectedRevision,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        ValidateId(id);
        ValidateRevision(expectedRevision);
        ValidateSet(record);
        if (record.Id != Guid.Empty && record.Id != id)
            throw new ArgumentException("The set id does not match the set being saved.", nameof(record));
        using var request = Guarded(HttpMethod.Put, $"api/sets/{id}", expectedRevision);
        request.Content = JsonContent.Create(ForSave(record), options: NestServerHttp.JsonOptions);
        var saved = await SendGuarded(request, id, expectedRevision, $"save drawing set {id}", cancellationToken);
        if (!SameMembers(saved, record))
            throw new IOException("The nest server stored different set members. Reload the set before saving again.");
        return saved;
    }

    public Task<DrawingSetRecord> ArchiveAsync(Guid id, int expectedRevision, CancellationToken cancellationToken = default) =>
        SetArchivedAsync(id, expectedRevision, "archive", cancellationToken);

    public Task<DrawingSetRecord> RestoreAsync(Guid id, int expectedRevision, CancellationToken cancellationToken = default) =>
        SetArchivedAsync(id, expectedRevision, "restore", cancellationToken);

    /// <summary>
    /// Records a release under <c>release.Id</c>, allocated by the caller so a post whose
    /// reply was lost can be sent again. A release already holding that id, or one of its
    /// created nests, raises <see cref="SetReleasePostConflictException"/> with that release.
    /// </summary>
    public async Task<SetReleaseRecord> PostReleaseAsync(SetReleaseRecord release, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(release);
        if (release.Id == Guid.Empty) throw new ArgumentException("A release id is required.", nameof(release));
        var error = SetReleaseRules.GetError(release);
        if (error is not null)
            throw new ArgumentException(error, nameof(release));

        using var response = await _httpClient.PostAsync(Url("api/releases"),
            JsonContent.Create(ForPost(release), options: NestServerHttp.JsonOptions), cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
            throw new ServerTooOldException(ReleaseFeature);
        if (response.StatusCode == HttpStatusCode.Conflict)
            throw new SetReleasePostConflictException(await ReadRelease(response, cancellationToken));
        // A server without drawings on their own refuses such a release as malformed.
        if (response.StatusCode == HttpStatusCode.BadRequest && release.Drawings.Count > 0)
            await EnsureDrawingReleasesSupportedAsync(cancellationToken);
        await NestServerHttp.EnsureSuccessAsync(response, "record the release", cancellationToken);
        var stored = await ReadRelease(response, cancellationToken);
        if (stored.Id != release.Id
            || !stored.Sets.Select(s => (s?.SetId, s?.SetRevision, s?.Quantity))
                .SequenceEqual(release.Sets.Select(s => ((Guid?)s.SetId, (int?)s.SetRevision, (int?)s.Quantity)))
            || !stored.Drawings.Select(d => (d?.DrawingId, d?.Quantity))
                .SequenceEqual(release.Drawings.Select(d => ((Guid?)d.DrawingId, (int?)d.Quantity)))
            || !stored.Nests.Select(n => (n?.NestId, n?.Kind, n?.RevisionAfter))
                .SequenceEqual(release.Nests.Select(n => ((Guid?)n.NestId, (SetReleaseNestKind?)n.Kind, (int?)n.RevisionAfter))))
            throw new IOException("The nest server recorded a different release than was posted.");
        return stored;
    }

    public async Task<SetReleaseRecord?> GetReleaseAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (id == Guid.Empty) throw new ArgumentException("A release id is required.", nameof(id));
        using var response = await _httpClient.GetAsync(Url($"api/releases/{id}"), cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            await ThrowIfReleasesUnsupported(cancellationToken);
            return null;
        }
        await NestServerHttp.EnsureSuccessAsync(response, $"read release {id}", cancellationToken);
        var release = await ReadRelease(response, cancellationToken);
        if (release.Id != id)
            throw new IOException("Nest server returned a different release.");
        return release;
    }

    public async Task<SetReleasePage> QueryReleasesAsync(Guid? setId = null, Guid? nestId = null, Guid? drawingId = null,
        int offset = 0, int limit = DrawingLibraryRules.DefaultRevisionLimit, CancellationToken cancellationToken = default)
    {
        if (setId == Guid.Empty) throw new ArgumentException("A drawing set id is required.", nameof(setId));
        if (nestId == Guid.Empty) throw new ArgumentException("A nest id is required.", nameof(nestId));
        if (drawingId == Guid.Empty) throw new ArgumentException("A library drawing id is required.", nameof(drawingId));
        ValidatePaging(offset, limit);
        var query = Paging(offset, limit);
        if (setId is not null) query += $"&setId={setId}";
        if (nestId is not null) query += $"&nestId={nestId}";
        if (drawingId is not null) query += $"&drawingId={drawingId}";
        using var response = await _httpClient.GetAsync(Url($"api/releases/query?{query}"), cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
            throw new ServerTooOldException(drawingId is null ? ReleaseFeature : DrawingReleaseFeature);
        // A server without drawings on their own refuses the unknown drawingId parameter.
        if (response.StatusCode == HttpStatusCode.BadRequest && drawingId is not null)
            throw new ServerTooOldException(DrawingReleaseFeature);
        await NestServerHttp.EnsureSuccessAsync(response, "read releases", cancellationToken);
        var page = await response.Content.ReadFromJsonAsync<SetReleasePage>(NestServerHttp.JsonOptions, cancellationToken)
            ?? throw new IOException("Nest server returned an empty release page.");
        if (page.Items is null || !IsBoundedPage(page.Items.Count, page.Total, page.Offset, page.Limit, offset, limit)
            || page.Items.Any(item => item is null || item.Sets is null || item.Drawings is null || item.Nests is null
                || (setId is not null && item.Sets.All(s => s?.SetId != setId))
                || (nestId is not null && item.Nests.All(n => n?.NestId != nestId))
                || (drawingId is not null && item.Drawings.All(d => d?.DrawingId != drawingId))))
            throw new IOException("Nest server returned an invalid or unbounded release page.");
        return page;
    }

    /// <summary>
    /// Asks for releases of a drawing no release can name: a server from before drawings on
    /// their own answers 400 for the unknown parameter (or 404 without releases at all).
    /// </summary>
    public async Task EnsureDrawingReleasesSupportedAsync(CancellationToken cancellationToken = default)
    {
        using var probe = await _httpClient.GetAsync(Url($"api/releases/query?drawingId={Guid.NewGuid()}&limit=1"), cancellationToken);
        if (probe.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.BadRequest)
            throw new ServerTooOldException(DrawingReleaseFeature);
        await NestServerHttp.EnsureSuccessAsync(probe, "check the release support", cancellationToken);
    }

    /// <summary>
    /// Undoes a release on the server, all or nothing. A refusal raises
    /// <see cref="SetReleaseUndoConflictException"/>: the release was undone before, or nests
    /// not skipped have changed since it (listed, so the caller can skip them and try again).
    /// </summary>
    public async Task<SetReleaseRecord> UndoReleaseAsync(Guid id, SetReleaseUndoRequest request,
        CancellationToken cancellationToken = default)
    {
        if (id == Guid.Empty) throw new ArgumentException("A release id is required.", nameof(id));
        var error = SetReleaseRules.GetUndoError(request);
        if (error is not null)
            throw new ArgumentException(error, nameof(request));

        using var response = await _httpClient.PostAsync(Url($"api/releases/{id}/undo"),
            JsonContent.Create(request, options: NestServerHttp.JsonOptions), cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            await ThrowIfReleasesUnsupported(cancellationToken);
            throw new IOException($"Release {id} no longer exists on the nest server.");
        }
        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            var conflict = await response.Content.ReadFromJsonAsync<SetReleaseUndoConflict>(NestServerHttp.JsonOptions, cancellationToken);
            if (conflict?.Release is null || conflict.ChangedNests is null)
                throw new IOException("Nest server refused the undo without saying why.");
            throw new SetReleaseUndoConflictException(conflict);
        }
        await NestServerHttp.EnsureSuccessAsync(response, $"undo release {id}", cancellationToken);
        var undone = await ReadRelease(response, cancellationToken);
        if (undone.Id != id || !undone.IsUndone)
            throw new IOException("Nest server did not report the release as undone.");
        return undone;
    }

    private async Task<DrawingSetRecord> SetArchivedAsync(Guid id, int expectedRevision, string action,
        CancellationToken cancellationToken)
    {
        ValidateId(id);
        ValidateRevision(expectedRevision);
        using var request = Guarded(HttpMethod.Post, $"api/sets/{id}/{action}", expectedRevision);
        var changed = await SendGuarded(request, id, expectedRevision, $"{action} drawing set {id}", cancellationToken);
        if (changed.Archived != (action == "archive"))
            throw new IOException($"Nest server did not {action} drawing set {id}. Reload the set.");
        return changed;
    }

    private HttpRequestMessage Guarded(HttpMethod method, string relative, int expectedRevision)
    {
        var request = new HttpRequestMessage(method, Url(relative));
        NestServerHttp.AddIfMatch(request, expectedRevision);
        return request;
    }

    private async Task<DrawingSetRecord> SendGuarded(HttpRequestMessage request, Guid id, int expectedRevision,
        string action, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            await ThrowIfServerTooOld(cancellationToken);
            throw new IOException($"Drawing set {id} no longer exists on the nest server.");
        }
        if (response.StatusCode == HttpStatusCode.Conflict)
            throw new DrawingSetSaveConflictException(await ReadSet(response, cancellationToken));
        await NestServerHttp.EnsureSuccessAsync(response, action, cancellationToken);
        var saved = await ReadSet(response, cancellationToken);
        if (saved.Id != id || saved.Revision < expectedRevision)
            throw new IOException("Nest server returned an invalid drawing set revision. Reload the set before saving again.");
        return saved;
    }

    private async Task<DrawingSetRecord?> GetRecordAsync(string relative, string action, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(Url(relative), cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            await ThrowIfServerTooOld(cancellationToken);
            return null;
        }
        await NestServerHttp.EnsureSuccessAsync(response, action, cancellationToken);
        return await ReadSet(response, cancellationToken);
    }

    private async Task<DrawingSetPage?> GetPageAsync(string relative, int offset, int limit, string action,
        CancellationToken cancellationToken)
    {
        ValidatePaging(offset, limit);
        using var response = await _httpClient.GetAsync(Url($"{relative}?{Paging(offset, limit)}"), cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            await ThrowIfServerTooOld(cancellationToken);
            return null;
        }
        await NestServerHttp.EnsureSuccessAsync(response, action, cancellationToken);
        var page = await response.Content.ReadFromJsonAsync<DrawingSetPage>(NestServerHttp.JsonOptions, cancellationToken)
            ?? throw new IOException("Nest server returned an empty drawing set page.");
        if (page.Items is null || !IsBoundedPage(page.Items.Count, page.Total, page.Offset, page.Limit, offset, limit)
            || page.Items.Any(item => item is null))
            throw new IOException("Nest server returned an invalid or unbounded drawing set page.");
        return page;
    }

    // A 404 for one set or nest is ambiguous; the query route exists on every set-aware server.
    // A server from before v9 has no /api/releases at all, so a 404 there is not "unknown release".
    private async Task ThrowIfReleasesUnsupported(CancellationToken cancellationToken)
    {
        using var probe = await _httpClient.GetAsync(Url("api/releases/query?limit=1"), cancellationToken);
        if (probe.StatusCode == HttpStatusCode.NotFound)
            throw new ServerTooOldException(ReleaseFeature);
    }

    private async Task ThrowIfServerTooOld(CancellationToken cancellationToken)
    {
        using var probe = await _httpClient.GetAsync(Url("api/sets/query?limit=1"), cancellationToken);
        if (probe.StatusCode == HttpStatusCode.NotFound)
            throw new ServerTooOldException(Feature);
    }

    private static bool IsBoundedPage(int count, int total, int pageOffset, int pageLimit, int offset, int limit) =>
        count <= limit && pageOffset == offset && pageLimit == limit && total >= 0
        && count <= System.Math.Max(0L, (long)total - offset);

    private static bool HasJoinedMembers(DrawingSetRecord record) =>
        record.Members is not null
        && record.Members.All(m => m is not null && m.Drawing is not null && m.Drawing.Id == m.DrawingId);

    private static bool SameMembers(DrawingSetRecord saved, DrawingSetRecord sent) =>
        saved.Members is not null
        && saved.Members.Select(m => (m?.DrawingId, m?.Quantity))
            .SequenceEqual(sent.Members.Select(m => ((Guid?)m.DrawingId, (int?)m.Quantity)));

    // The joined drawings are ignored by the server; leaving them out keeps the body small.
    private static DrawingSetRecord ForSave(DrawingSetRecord record) => new()
    {
        Id = record.Id,
        Name = record.Name,
        Customer = record.Customer,
        Notes = record.Notes,
        SavedBy = record.SavedBy,
        Members = record.Members.Select(m => new DrawingSetMember { DrawingId = m.DrawingId, Quantity = m.Quantity }).ToList(),
    };

    // Joined and server-computed values are ignored by the server; leaving them out keeps the body small.
    private static SetReleaseRecord ForPost(SetReleaseRecord release) => new()
    {
        Id = release.Id,
        Notes = release.Notes,
        ReleasedBy = release.ReleasedBy,
        Sets = release.Sets.Select(s => new SetReleaseLine
        {
            SetId = s.SetId,
            SetRevision = s.SetRevision,
            Quantity = s.Quantity,
        }).ToList(),
        Drawings = release.Drawings.Select(d => new SetReleaseDrawing
        {
            DrawingId = d.DrawingId,
            Quantity = d.Quantity,
        }).ToList(),
        Nests = release.Nests.Select(n => new SetReleaseNest
        {
            NestId = n.NestId,
            Kind = n.Kind,
            RevisionBefore = n.RevisionBefore,
            RevisionAfter = n.RevisionAfter,
            MaterialName = n.MaterialName,
            MaterialGrade = n.MaterialGrade,
            Thickness = n.Thickness,
            Units = n.Units,
            CutType = n.CutType,
            Items = n.Items,
        }).ToList(),
    };

    private static void ValidateSet(DrawingSetRecord record)
    {
        var error = DrawingSetRules.GetError(record);
        if (error is not null)
            throw new ArgumentException(error, nameof(record));
    }

    private static void ValidateId(Guid id)
    {
        if (id == Guid.Empty) throw new ArgumentException("A drawing set id is required.", nameof(id));
    }

    private static void ValidateRevision(int revision)
    {
        if (revision < 1) throw new ArgumentOutOfRangeException(nameof(revision));
    }

    private static void ValidatePaging(int offset, int limit)
    {
        if (offset < 0) throw new ArgumentOutOfRangeException(nameof(offset));
        if (limit < 1 || limit > DrawingLibraryRules.MaxRevisionLimit) throw new ArgumentOutOfRangeException(nameof(limit));
    }

    private static string Paging(int offset, int limit) =>
        $"offset={offset.ToString(CultureInfo.InvariantCulture)}&limit={limit.ToString(CultureInfo.InvariantCulture)}";

    private static string BuildQueryString(DrawingSetQuery query)
    {
        var text = new StringBuilder()
            .Append("search=").Append(Uri.EscapeDataString(query.NormalizedSearch))
            .Append("&sort=").Append(JsonNamingPolicy.CamelCase.ConvertName(query.Sort.ToString()))
            .Append("&order=").Append(query.Descending ? "desc" : "asc")
            .Append("&offset=").Append(query.Offset.ToString(CultureInfo.InvariantCulture))
            .Append("&limit=").Append(query.Limit.ToString(CultureInfo.InvariantCulture));
        if (!string.IsNullOrWhiteSpace(query.Customer))
            text.Append("&customer=").Append(Uri.EscapeDataString(query.Customer));
        if (query.IncludeArchived)
            text.Append("&includeArchived=true");
        return text.ToString();
    }

    private static async Task<DrawingSetRecord> ReadSet(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var record = await response.Content.ReadFromJsonAsync<DrawingSetRecord>(NestServerHttp.JsonOptions, cancellationToken);
        return record ?? throw new IOException("Nest server returned an empty drawing set record.");
    }

    private static async Task<SetReleaseRecord> ReadRelease(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var release = await response.Content.ReadFromJsonAsync<SetReleaseRecord>(NestServerHttp.JsonOptions, cancellationToken);
        if (release is null || release.Sets is null || release.Drawings is null || release.Nests is null)
            throw new IOException("Nest server returned an empty set release record.");
        return release;
    }


    public void Dispose()
    {
        if (_ownsClient)
            _httpClient.Dispose();
    }
}

/// <summary>A guarded set write found a newer or different current state; nothing was changed.</summary>
public sealed class DrawingSetSaveConflictException : IOException
{
    public DrawingSetSaveConflictException(DrawingSetRecord current)
        : base(current.Archived
            ? $"Drawing set '{current.Name}' is archived or was changed (current revision {current.Revision}). Reload before saving."
            : $"Drawing set '{current.Name}' was changed by someone else (current revision {current.Revision}). Reload before saving.")
    {
        Current = current;
    }

    /// <summary>The set's current server record.</summary>
    public DrawingSetRecord Current { get; }
}

/// <summary>
/// A release already holds the posted release id or one of its created nests; nothing was
/// recorded. When <see cref="Current"/> has the posted id, an earlier post whose reply was
/// lost was stored.
/// </summary>
public sealed class SetReleasePostConflictException : IOException
{
    public SetReleasePostConflictException(SetReleaseRecord current)
        : base($"Release {current.Id} already records this release or one of its nests.")
    {
        Current = current;
    }

    /// <summary>The release that holds the id or nest.</summary>
    public SetReleaseRecord Current { get; }
}

/// <summary>The server refused to undo a release; nothing was changed.</summary>
public sealed class SetReleaseUndoConflictException : IOException
{
    public SetReleaseUndoConflictException(SetReleaseUndoConflict conflict)
        : base(conflict.Reason == SetReleaseUndoRefusal.AlreadyUndone
            ? "The release was already undone."
            : $"{conflict.ChangedNests.Count} of the release's nests changed since it was made.")
    {
        Conflict = conflict;
    }

    public SetReleaseUndoConflict Conflict { get; }
}
