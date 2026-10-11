namespace OpenNest.Data;

/// <summary>
/// Tracks a document's database identity, bound to a particular server address.
/// A failed upload leaves that identity untouched. Switching servers creates a new
/// record rather than accidentally updating an unrelated record with the same id.
/// </summary>
public sealed class NestSaveSession
{
    public Guid RemoteId { get; private set; }
    public string ServerUrl { get; private set; } = "";
    public int Revision { get; private set; }
    public bool RequiresReload { get; private set; }

    public void Bind(Guid id, string serverUrl, int revision)
    {
        if (revision < 0) throw new ArgumentOutOfRangeException(nameof(revision));
        Bind(id, serverUrl);
        Revision = revision;
    }

    public void Bind(Guid id, string serverUrl)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("A saved nest needs a non-empty id.", nameof(id));
        RemoteId = id;
        ServerUrl = Normalize(serverUrl);
        Revision = 0; // Legacy reads remain viewable, but must not become guarded saves.
        RequiresReload = false;
    }

    public async Task<NestRecord> SaveAsync(
        INestRepository repository, string serverUrl, Nest nest, byte[] archive,
        bool saveCopy = false, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(nest);
        ArgumentNullException.ThrowIfNull(archive);

        var normalizedUrl = Normalize(serverUrl);
        if (normalizedUrl.Length == 0)
            throw new ArgumentException("A database server URL is required.", nameof(serverUrl));

        var id = !saveCopy && ServerUrl == normalizedUrl ? RemoteId : Guid.Empty;
        var record = NestRecordFactory.FromNest(nest, id, archive.LongLength);
        if (id != Guid.Empty && (Revision < 1 || RequiresReload))
            throw new IOException("Reload the current nest revision before saving, or Save As a new nest. Upgrade the nest server if version history is unavailable.");
        NestRecord saved;
        try
        {
            saved = id == Guid.Empty
                ? await repository.UploadAsync(archive, record, cancellationToken)
                : await repository.UpdateFileAsync(id, archive, record, Revision, cancellationToken);
        }
        catch (Exception ex) when (id != Guid.Empty && ex is IOException or System.Net.Http.HttpRequestException or OperationCanceledException or System.Text.Json.JsonException)
        {
            // The request may have committed before its reply was lost. Do not replay
            // pending bytes until the operator explicitly reloads/reconciles the record.
            RequiresReload = true;
            throw;
        }
        if (saved.Id == Guid.Empty || (id != Guid.Empty && (saved.Id != id || saved.Revision < Revision)))
        {
            if (id != Guid.Empty) RequiresReload = true;
            throw new InvalidDataException("Server returned an invalid nest id or revision. Reload to reconcile before saving again.");
        }

        Bind(saved.Id, normalizedUrl, saved.Revision);
        return saved;
    }

    private static string Normalize(string? url) => (url ?? "").Trim().TrimEnd('/');
}
