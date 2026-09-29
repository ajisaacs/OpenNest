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

    public void Bind(Guid id, string serverUrl)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("A saved nest needs a non-empty id.", nameof(id));
        RemoteId = id;
        ServerUrl = Normalize(serverUrl);
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
        var saved = id == Guid.Empty
            ? await repository.UploadAsync(archive, record, cancellationToken)
            : await repository.UpdateFileAsync(id, archive, record, cancellationToken);
        if (saved.Id == Guid.Empty || (id != Guid.Empty && saved.Id != id))
            throw new InvalidDataException("Server returned an invalid nest id.");

        Bind(saved.Id, normalizedUrl);
        return saved;
    }

    private static string Normalize(string? url) => (url ?? "").Trim().TrimEnd('/');
}
