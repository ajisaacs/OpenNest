using System.Security.Cryptography;
using OpenNest.Data;

namespace OpenNest.Tests.Data;

/// <summary>
/// An in-memory drawing library server: revisions, If-Match conflicts, archive, the
/// query filters the server applies, a write log and a switch that fails the next write.
/// Linked into OpenNest.WinForms.Tests as well.
/// </summary>
internal sealed class InMemoryDrawingLibrary : IDrawingLibraryStore
{
    private readonly Dictionary<Guid, List<DrawingLibraryRecord>> _revisions = new();
    private readonly Dictionary<string, byte[]> _packages = new();
    private DateTime _clock = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>Fail the write with this index (0 is the next write) without storing it.</summary>
    public int? FailWrite { get; set; }

    /// <summary>Every write requested, in order: ("upload" | "update", id).</summary>
    public List<(string Kind, Guid Id)> Writes { get; } = new();

    public IEnumerable<DrawingLibraryRecord> Current => _revisions.Values.Select(r => r[^1]);

    public DrawingLibraryRecord Get(Guid id) => _revisions[id][^1];

    /// <summary>Stores revision 1 directly, as though another operator had saved it.</summary>
    public DrawingLibraryRecord Seed(string name, string material, double thickness, byte[] package,
        string customer = "", Units units = Units.Inches, CutType cutType = CutType.Laser, bool archived = false)
    {
        var id = Guid.NewGuid();
        var record = Stored(new DrawingLibraryRecord
        {
            Name = name,
            Customer = customer,
            MaterialName = material,
            Thickness = thickness,
            Units = units,
            CutType = cutType,
            Notes = "Seeded notes",
        }, id, 1, package);
        record.Archived = archived;
        _revisions[id] = new List<DrawingLibraryRecord> { record };
        return record;
    }

    public Task<DrawingLibraryPage> QueryAsync(DrawingLibraryQuery query, CancellationToken cancellationToken = default)
    {
        var search = query.NormalizedSearch;
        var matches = Current
            .Where(r => query.IncludeArchived || !r.Archived)
            .Where(r => search.Length == 0 || new[] { r.Name, r.Customer, r.Notes }
                .Any(text => text.Contains(search, StringComparison.OrdinalIgnoreCase)))
            .Where(r => Filter(query.Customer, r.Customer) && Filter(query.MaterialName, r.MaterialName)
                && Filter(query.MaterialGrade, r.MaterialGrade))
            .Where(r => query.Thickness is not { } t || r.Thickness == DrawingLibraryRules.NormalizeThickness(t))
            .Where(r => query.CutType is not { } c || r.CutType == c)
            .OrderByDescending(r => r.SavedAt)
            .ToList();
        return Task.FromResult(new DrawingLibraryPage
        {
            Items = matches.Skip(query.Offset).Take(query.Limit).ToList(),
            Total = matches.Count,
            Offset = query.Offset,
            Limit = query.Limit,
        });
    }

    /// <summary>Every package download requested: (id, revision).</summary>
    public List<(Guid Id, int Revision)> Downloads { get; } = new();

    public Task<byte[]?> GetRevisionPackageAsync(Guid id, int revision, CancellationToken cancellationToken = default)
    {
        Downloads.Add((id, revision));
        var record = _revisions.TryGetValue(id, out var list) ? list.FirstOrDefault(r => r.Revision == revision) : null;
        return Task.FromResult(record != null && _packages.TryGetValue(record.PackageSha256, out var package)
            ? package.ToArray()
            : null);
    }

    public Task<DrawingLibraryRecord?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        Task.FromResult(_revisions.TryGetValue(id, out var list) ? list[^1] : null);

    public Task<DrawingLibraryPage?> GetRevisionsAsync(Guid id, int offset = 0,
        int limit = DrawingLibraryRules.DefaultRevisionLimit, CancellationToken cancellationToken = default)
    {
        if (!_revisions.TryGetValue(id, out var list))
            return Task.FromResult<DrawingLibraryPage?>(null);
        var newestFirst = Enumerable.Reverse(list).ToList();
        return Task.FromResult<DrawingLibraryPage?>(new DrawingLibraryPage
        {
            Items = newestFirst.Skip(offset).Take(limit).ToList(),
            Total = list.Count,
            Offset = offset,
            Limit = limit,
        });
    }

    public Task<DrawingLibraryRecord> UploadAsync(DrawingLibraryRecord record, byte[] package,
        CancellationToken cancellationToken = default)
    {
        Write("upload", record.Id);
        if (_revisions.TryGetValue(record.Id, out var existing))
            throw new DrawingLibraryConflictException(existing[^1]);
        _revisions[record.Id] = new List<DrawingLibraryRecord> { Stored(record, record.Id, 1, package) };
        return Task.FromResult(Get(record.Id));
    }

    public Task<DrawingLibraryRecord> UpdatePackageAsync(Guid id, DrawingLibraryRecord record, byte[] package,
        int expectedRevision, CancellationToken cancellationToken = default)
    {
        Write("update", id);
        var current = Get(id);
        if (current.Revision != expectedRevision || current.Archived)
            throw new DrawingLibraryConflictException(current);
        var next = Stored(record, id, current.Revision + 1, package);
        _revisions[id].Add(next);
        return Task.FromResult(next);
    }

    public Task<DrawingLibraryRecord> UpdateMetadataAsync(Guid id, DrawingLibraryRecord record, int expectedRevision,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public static string Sha(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private void Write(string kind, Guid id)
    {
        var index = Writes.Count;
        Writes.Add((kind, id));
        if (FailWrite == index)
            throw new HttpRequestException("Synthetic connection failure.");
    }

    private DrawingLibraryRecord Stored(DrawingLibraryRecord metadata, Guid id, int revision, byte[] package)
    {
        _clock = _clock.AddMinutes(1);
        _packages[Sha(package)] = package.ToArray();
        return new DrawingLibraryRecord
        {
            Id = id,
            Revision = revision,
            Name = metadata.Name,
            Customer = metadata.Customer,
            MaterialName = metadata.MaterialName,
            MaterialGrade = metadata.MaterialGrade,
            Thickness = DrawingLibraryRules.NormalizeThickness(metadata.Thickness),
            Units = metadata.Units,
            CutType = metadata.CutType,
            Notes = metadata.Notes,
            Settings = metadata.Settings,
            SavedBy = metadata.SavedBy,
            PackageSha256 = Sha(package),
            PackageSize = package.Length,
            SavedAt = _clock,
        };
    }

    private static bool Filter(string filter, string value) =>
        string.IsNullOrWhiteSpace(filter) || SharedListNames.Key(filter) == SharedListNames.Key(value);
}
