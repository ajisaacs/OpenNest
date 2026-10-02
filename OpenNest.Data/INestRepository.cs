namespace OpenNest.Data;

/// <summary>
/// Storage backend for saved nests. <see cref="RemoteNestRepository"/> talks to the
/// central nest server; File mode in the desktop app keeps using SaveFileDialog and
/// does not go through this interface.
/// </summary>
public interface INestRepository
{
    /// <summary>
    /// All stored nests, newest saved first. This is the full-enumeration contract for
    /// backup and verification; browsing uses <see cref="QueryAsync"/>.
    /// </summary>
    Task<IReadOnlyList<NestRecord>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>One bounded page of nests matching the query, filtered and ordered by the server.</summary>
    Task<NestPage> QueryAsync(NestQuery query, CancellationToken cancellationToken = default);

    Task<NestRecord?> GetMetadataAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Downloaded .nest archive bytes, or null when the id does not exist.</summary>
    Task<byte[]?> GetFileAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Stores a new nest; returns the server-assigned record.</summary>
    Task<NestRecord> UploadAsync(
        byte[] nestFile, NestRecord record, CancellationToken cancellationToken = default);

    /// <summary>Replaces the archive and metadata of an existing id, keeping the id.</summary>
    Task<NestRecord> UpdateFileAsync(
        Guid id, byte[] nestFile, NestRecord record, CancellationToken cancellationToken = default);

    /// <summary>Replaces metadata only (status, made-by, comments...), leaving the archive.</summary>
    Task<NestRecord> UpdateMetadataAsync(
        Guid id, NestRecord record, CancellationToken cancellationToken = default);

    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
