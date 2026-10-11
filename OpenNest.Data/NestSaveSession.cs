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
    public Guid PendingCreateId { get; private set; }
    private string pendingCreateUrl = "";
    private byte[]? pendingCreateArchive;
    private NestRecord? pendingCreateMetadata;

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
        PendingCreateId = Guid.Empty;
        pendingCreateArchive = null;
        pendingCreateMetadata = null;
        pendingCreateUrl = "";
    }

    /// <summary>Explicitly reconcile an upload whose reply was lost before any retry.</summary>
    public async Task<NestRecord?> ReconcilePendingCreateAsync(INestRepository repository,
        string serverUrl, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        if (PendingCreateId == Guid.Empty || Normalize(serverUrl) != pendingCreateUrl)
            throw new InvalidOperationException("No pending upload on this server to reconcile.");
        var record = await repository.GetMetadataAsync(PendingCreateId, cancellationToken);
        if (record is null)
        {
            // A deliberate subsequent Save may retry this SAME ID, never create a second ID.
            RequiresReload = false;
            return null;
        }
        var bytes = await repository.GetFileAsync(PendingCreateId, cancellationToken);
        if (record.Id != PendingCreateId || record.Revision < 1 || bytes is null
            || !bytes.AsSpan().SequenceEqual(pendingCreateArchive)
            || !SameEditableMetadata(record, pendingCreateMetadata!))
            throw new IOException("The pending upload differs from the server record. Reopen the server nest before editing.");
        Bind(record.Id, pendingCreateUrl, record.Revision);
        return record;
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

        if (PendingCreateId != Guid.Empty && (RequiresReload || normalizedUrl != pendingCreateUrl || saveCopy))
            throw new IOException("The previous upload may have succeeded. Reconcile its pending ID on the original server before saving or copying.");
        var id = !saveCopy && ServerUrl == normalizedUrl ? RemoteId : Guid.Empty;
        var creating = id == Guid.Empty;
        var uploadId = creating ? (PendingCreateId == Guid.Empty ? Guid.NewGuid() : PendingCreateId) : id;
        var record = NestRecordFactory.FromNest(nest, uploadId, archive.LongLength);
        if (!creating && (Revision < 1 || RequiresReload))
            throw new IOException("Reload the current nest revision before saving, or Save As a new nest. Upgrade the nest server if version history is unavailable.");
        if (creating)
        {
            PendingCreateId = uploadId;
            pendingCreateUrl = normalizedUrl;
            pendingCreateArchive = archive.ToArray();
            pendingCreateMetadata = record;
        }
        NestRecord saved;
        try
        {
            saved = creating
                ? await repository.UploadAsync(archive, record, cancellationToken)
                : await repository.UpdateFileAsync(id, archive, record, Revision, cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or System.Net.Http.HttpRequestException or OperationCanceledException or System.Text.Json.JsonException)
        {
            // The request may have committed before its reply was lost. Do not replay
            // pending bytes until the operator explicitly reloads/reconciles the record.
            RequiresReload = true;
            throw;
        }
        if (saved.Id != uploadId || (!creating && saved.Revision < Revision))
        {
            RequiresReload = true;
            throw new InvalidDataException("Server returned an invalid nest id or revision. Reload to reconcile before saving again.");
        }

        Bind(saved.Id, normalizedUrl, saved.Revision);
        return saved;
    }

    private static string Normalize(string? url) => (url ?? "").Trim().TrimEnd('/');

    private static bool SameEditableMetadata(NestRecord a, NestRecord b) =>
        a.Name == b.Name && a.Customer == b.Customer && a.DateCreated == b.DateCreated
        && a.DateModified == b.DateModified && a.Material == b.Material && a.Thickness == b.Thickness
        && a.Status == b.Status && a.PlateCount == b.PlateCount && a.PartCount == b.PartCount
        && a.Comments == b.Comments && a.MadeBy == b.MadeBy;
}
