using OpenNest.Data;

namespace OpenNest.Tests.Data;

/// <summary>
/// An in-memory drawing set server: revisions, If-Match conflicts, archive, a search over
/// name, customer and notes, and switches that lose or fail the next create. Linked into
/// OpenNest.WinForms.Tests as well.
/// </summary>
internal sealed class InMemoryDrawingSets : IDrawingSetStore
{
    private readonly Dictionary<Guid, List<DrawingSetRecord>> _revisions = new();

    public List<DrawingSetRecord> Creates { get; } = new();
    public List<(Guid Id, int ExpectedRevision, DrawingSetRecord Record)> Updates { get; } = new();
    public List<DrawingSetQuery> Queries { get; } = new();
    public bool LoseNextCreateReply { get; set; }
    public bool FailNextCreate { get; set; }
    public Exception? QueryFailure { get; set; }

    public IEnumerable<DrawingSetRecord> Current => _revisions.Values.Select(r => r[^1]);

    public Task<DrawingSetRecord> CreateAsync(DrawingSetRecord record, CancellationToken cancellationToken = default)
    {
        Creates.Add(record);
        if (FailNextCreate)
        {
            FailNextCreate = false;
            throw new IOException("Synthetic connection refused.");
        }
        if (_revisions.TryGetValue(record.Id, out var existing))
            throw new DrawingSetSaveConflictException(existing[^1]);

        var stored = Store(record, record.Id, 1, false);
        _revisions[record.Id] = new List<DrawingSetRecord> { stored };
        if (LoseNextCreateReply)
        {
            LoseNextCreateReply = false;
            throw new IOException("Synthetic reply lost.");
        }
        return Task.FromResult(stored);
    }

    public Task<DrawingSetRecord> UpdateAsync(Guid id, DrawingSetRecord record, int expectedRevision, CancellationToken cancellationToken = default)
    {
        Updates.Add((id, expectedRevision, record));
        var revisions = _revisions[id];
        var current = revisions[^1];
        if (current.Revision != expectedRevision || current.Archived)
            throw new DrawingSetSaveConflictException(current);
        var stored = Store(record, id, current.Revision + 1, false);
        revisions.Add(stored);
        return Task.FromResult(stored);
    }

    public Task<DrawingSetRecord> ArchiveAsync(Guid id, int expectedRevision, CancellationToken cancellationToken = default) =>
        SetArchived(id, expectedRevision, true);

    public Task<DrawingSetRecord> RestoreAsync(Guid id, int expectedRevision, CancellationToken cancellationToken = default) =>
        SetArchived(id, expectedRevision, false);

    public Task<DrawingSetRecord?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        Task.FromResult(_revisions.TryGetValue(id, out var revisions) ? revisions[^1] : null);

    public Task<DrawingSetPage> QueryAsync(DrawingSetQuery query, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Queries.Add(query);
        if (QueryFailure != null)
            throw QueryFailure;
        var matches = Current
            .Where(r => query.IncludeArchived || !r.Archived)
            .Where(r => (r.Name + " " + r.Customer + " " + r.Notes).Contains(query.NormalizedSearch, StringComparison.OrdinalIgnoreCase))
            .OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return Task.FromResult(new DrawingSetPage
        {
            Items = matches.Skip(query.Offset).Take(query.Limit).Select(Listed).ToList(),
            Total = matches.Count,
            Offset = query.Offset,
            Limit = query.Limit,
        });
    }

    public Task<DrawingSetPage?> GetRevisionsAsync(Guid id, int offset = 0, int limit = 100, CancellationToken cancellationToken = default)
    {
        if (!_revisions.TryGetValue(id, out var revisions))
            return Task.FromResult<DrawingSetPage?>(null);
        var newest = Enumerable.Reverse(revisions).ToList();
        return Task.FromResult<DrawingSetPage?>(new DrawingSetPage
        {
            Items = newest.Skip(offset).Take(limit).Select(Listed).ToList(),
            Total = newest.Count,
            Offset = offset,
            Limit = limit,
        });
    }

    public Task<DrawingSetRecord?> GetRevisionAsync(Guid id, int revision, CancellationToken cancellationToken = default) =>
        Task.FromResult(_revisions.TryGetValue(id, out var revisions) ? revisions.FirstOrDefault(r => r.Revision == revision) : null);

    public Task<DrawingSetPage?> GetSetsContainingAsync(Guid drawingId, int offset = 0, int limit = 100, CancellationToken cancellationToken = default)
    {
        var matches = Current.Where(r => r.Members.Any(m => m.DrawingId == drawingId)).OrderBy(r => r.Name).ToList();
        return Task.FromResult<DrawingSetPage?>(new DrawingSetPage
        {
            Items = matches.Skip(offset).Take(limit).Select(Listed).ToList(),
            Total = matches.Count,
            Offset = offset,
            Limit = limit,
        });
    }

    /// <summary>Stored releases, oldest first; tests may add ones with joined nest metadata.</summary>
    public List<SetReleaseRecord> Releases { get; } = new();
    public int PostAttempts { get; private set; }
    public bool FailNextPost { get; set; }

    public Task<SetReleaseRecord> PostReleaseAsync(SetReleaseRecord release, CancellationToken cancellationToken = default)
    {
        PostAttempts++;
        if (FailNextPost)
        {
            FailNextPost = false;
            throw new IOException("Synthetic release post failure.");
        }
        if (Releases.FirstOrDefault(r => r.Id == release.Id) is { } existing)
            throw new SetReleasePostConflictException(existing);

        var stored = new SetReleaseRecord
        {
            Id = release.Id,
            Sets = release.Sets.Select(s => new SetReleaseLine
            {
                SetId = s.SetId,
                SetRevision = s.SetRevision,
                Quantity = s.Quantity,
                SetName = _revisions[s.SetId].First(r => r.Revision == s.SetRevision).Name,
            }).ToList(),
            Notes = release.Notes,
            ReleasedBy = release.ReleasedBy,
            ReleasedAt = new DateTime(2026, 10, 20, 15, 0, 0, DateTimeKind.Utc),
            Nests = release.Nests.ToList(),
        };
        Releases.Add(stored);
        return Task.FromResult(stored);
    }

    public Task<SetReleaseRecord?> GetReleaseAsync(Guid id, CancellationToken cancellationToken = default) =>
        Task.FromResult(Releases.FirstOrDefault(r => r.Id == id));

    public Task<SetReleasePage> QueryReleasesAsync(Guid? setId = null, Guid? nestId = null, int offset = 0, int limit = 100,
        CancellationToken cancellationToken = default)
    {
        var newest = Enumerable.Reverse(Releases
            .Where(r => setId is null || r.Sets.Any(s => s.SetId == setId))
            .Where(r => nestId is null || r.Nests.Any(n => n.NestId == nestId))
            .ToList()).ToList();
        return Task.FromResult(new SetReleasePage
        {
            Items = newest.Skip(offset).Take(limit).ToList(),
            Total = newest.Count,
            Offset = offset,
            Limit = limit,
        });
    }

    /// <summary>Every undo request, in order.</summary>
    public List<(Guid Id, SetReleaseUndoRequest Request)> UndoRequests { get; } = new();

    /// <summary>Answers an undo; this store holds no nests, so a test decides what the server says.</summary>
    public Func<Guid, SetReleaseUndoRequest, SetReleaseRecord>? UndoHandler { get; set; }

    public Task<SetReleaseRecord> UndoReleaseAsync(Guid id, SetReleaseUndoRequest request, CancellationToken cancellationToken = default)
    {
        UndoRequests.Add((id, new SetReleaseUndoRequest { UndoneBy = request.UndoneBy, SkipNests = request.SkipNests.ToList() }));
        return UndoHandler is { } handler
            ? Task.FromResult(handler(id, request))
            : throw new NotSupportedException("The in-memory store does not hold nests, so it cannot undo a release.");
    }

    private Task<DrawingSetRecord> SetArchived(Guid id, int expectedRevision, bool archived)
    {
        var revisions = _revisions[id];
        var current = revisions[^1];
        if (current.Revision != expectedRevision || current.Archived == archived)
            throw new DrawingSetSaveConflictException(current);
        var stored = Store(current, id, current.Revision + 1, archived);
        revisions.Add(stored);
        return Task.FromResult(stored);
    }

    private static DrawingSetRecord Store(DrawingSetRecord record, Guid id, int revision, bool archived) => new()
    {
        Id = id,
        Revision = revision,
        Name = record.Name,
        Customer = record.Customer,
        Notes = record.Notes,
        SavedBy = record.SavedBy,
        Archived = archived,
        SavedAt = new DateTime(2026, 10, 20, 12, 0, 0, DateTimeKind.Utc).AddMinutes(revision),
        MemberCount = record.Members.Count,
        Members = record.Members.Select(m => new DrawingSetMember { DrawingId = m.DrawingId, Quantity = m.Quantity, Drawing = m.Drawing }).ToList(),
    };

    private static DrawingSetRecord Listed(DrawingSetRecord record) => new()
    {
        Id = record.Id,
        Revision = record.Revision,
        Name = record.Name,
        Customer = record.Customer,
        Notes = record.Notes,
        SavedBy = record.SavedBy,
        Archived = record.Archived,
        SavedAt = record.SavedAt,
        MemberCount = record.MemberCount,
    };
}
