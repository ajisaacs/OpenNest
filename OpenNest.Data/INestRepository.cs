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

    /// <summary>Reads bounded immutable history; unsupported backends fail closed.</summary>
    Task<NestRevisionPage> GetRevisionsAsync(Guid id, int offset = 0, int limit = 100,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("This repository does not support nest version history.");

    Task<byte[]?> GetRevisionFileAsync(Guid id, int revisionNumber,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("This repository does not support nest version history.");

    Task<NestRecord> UpdateMetadataAsync(Guid id, NestRecord record, int expectedRevision,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("This repository does not support guarded metadata edits.");

    Task<NestRecord?> GetDeletedMetadataAsync(Guid id, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("This repository does not support deleted nests.");

    Task<byte[]?> GetDeletedFileAsync(Guid id, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("This repository does not support deleted nests.");

    Task<NestPage> QueryDeletedAsync(NestQuery query, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("This repository does not support deleted nests.");

    Task<NestRevisionPage> GetDeletedRevisionsAsync(Guid id, int offset = 0, int limit = 100,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("This repository does not support deleted nest history.");

    Task<byte[]?> GetDeletedRevisionFileAsync(Guid id, int revisionNumber,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("This repository does not support deleted nest history.");

    Task<NestRecord> DeleteAsync(Guid id, int expectedRevision, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("This repository does not support guarded deletion.");

    Task<NestRecord> RestoreAsync(Guid id, int sourceRevision, int expectedRevision,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("This repository does not support guarded restoration.");

    Task<NestRecord> RecoverAsync(Guid id, int expectedRevision, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("This repository does not support guarded recovery.");

    /// <summary>Allocates the year's next generated nest number, unique across every client.</summary>
    Task<int> AllocateNestNumberAsync(int year, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("This repository does not allocate nest names.");

    /// <summary>
    /// The ledger item codes on the plates of every open (Draft, Quote or To Be Cut) nest,
    /// except <paramref name="excludeNestId"/> when it is not empty.
    /// </summary>
    Task<IReadOnlyList<NestStockCodeUse>> GetStockCodesInUseAsync(Guid excludeNestId,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("This repository does not list stock item codes.");

    /// <summary>Atomically replaces the current archive only at the expected revision.</summary>
    Task<NestRecord> UpdateFileAsync(Guid id, byte[] nestFile, NestRecord record, int expectedRevision,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("This repository does not support guarded saves. Upgrade the nest server and reload.");

}
