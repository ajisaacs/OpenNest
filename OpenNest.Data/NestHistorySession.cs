using System.Text.Json;

namespace OpenNest.Data;

/// <summary>
/// URL-bound, bounded history browsing. Historical reads never bind an editable document.
/// A selection change cancels and invalidates previous reads even if a backend ignores cancellation.
/// Mutations use the displayed current revision; an uncertain failure requires an explicit refresh.
/// </summary>
public sealed class NestHistorySession : IDisposable
{
    private readonly INestRepository repository;
    private CancellationTokenSource? pending;
    private int generation;
    private bool disposed;
    public string ServerUrl { get; }
    public Guid NestId { get; private set; }
    public bool Deleted { get; private set; }
    public NestRecord? Current { get; private set; }
    public NestRevisionPage? Page { get; private set; }
    public bool IsMutating { get; private set; }
    public const int PageSize = 50;

    public NestHistorySession(INestRepository repository, string serverUrl)
    {
        this.repository = repository ?? throw new ArgumentNullException(nameof(repository));
        ServerUrl = Normalize(serverUrl);
    }

    public async Task<bool> LoadAsync(Guid id, bool deleted = false, int offset = 0,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (IsMutating) throw new InvalidOperationException("Wait for the current operation to finish.");
        if (id == Guid.Empty || offset < 0) throw new ArgumentOutOfRangeException(nameof(id));
        pending?.Cancel();
        var run = ++generation;
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        pending = cts;
        NestId = id;
        Deleted = deleted;
        Current = null;
        Page = null;
        try
        {
            var record = deleted ? await repository.GetDeletedMetadataAsync(id, cts.Token)
                : await repository.GetMetadataAsync(id, cts.Token);
            if (record == null) throw new FileNotFoundException("This nest is no longer in this view. Refresh the list.");
            ValidateRecord(record, id, deleted);
            if (record.Revision < 1) throw new NotSupportedException("Upgrade the nest server to browse or change version history. Current nests can still be opened read-only.");
            var page = deleted ? await repository.GetDeletedRevisionsAsync(id, offset, PageSize, cts.Token)
                : await repository.GetRevisionsAsync(id, offset, PageSize, cts.Token);
            if (page.Offset != offset || page.Items.Count > PageSize || page.Total < offset + page.Items.Count
                || page.Items.Any(r => r.Id != id || r.Revision < 1))
                throw new InvalidDataException("The server returned invalid nest history.");
            if (page.Items.Any(r => r.Revision > record.Revision))
                throw new IOException("The current version changed while loading history. Refresh to review the new current version.");
            if (run != generation || cts.IsCancellationRequested || disposed) return false;
            Current = record;
            Page = page;
            return true;
        }
        catch (Exception) when (run != generation || cts.IsCancellationRequested || disposed) { return false; }
        finally { if (ReferenceEquals(pending, cts)) pending = null; }
    }

    public async Task<byte[]?> DownloadAsync(int revision, string serverUrl, CancellationToken ct = default)
    {
        EnsureReady(serverUrl);
        var id = NestId;
        var run = generation;
        var snapshot = Page!.Items.SingleOrDefault(r => r.Revision == revision)
            ?? throw new InvalidOperationException("Select a revision on the displayed page.");
        var file = Deleted ? await repository.GetDeletedRevisionFileAsync(id, revision, ct)
            : await repository.GetRevisionFileAsync(id, revision, ct);
        ct.ThrowIfCancellationRequested();
        if (disposed || run != generation) return null;
        if (file == null || file.LongLength != snapshot.FileSize)
            throw new InvalidDataException("The revision archive is missing or does not match its metadata. Refresh before trying again.");
        return file;
    }

    public async Task<NestRecord> RestoreAsync(int sourceRevision, string serverUrl, CancellationToken ct = default)
    {
        EnsureReady(serverUrl);
        if (Deleted) throw new InvalidOperationException("Recover the deleted nest before restoring an earlier version.");
        if (!Page!.Items.Any(r => r.Revision == sourceRevision)) throw new InvalidOperationException("Select a displayed revision.");
        return await MutateAsync(() => repository.RestoreAsync(NestId, sourceRevision, Current!.Revision, ct), false, ct);
    }

    public async Task<NestRecord> RecoverAsync(string serverUrl, CancellationToken ct = default)
    {
        EnsureReady(serverUrl);
        if (!Deleted) throw new InvalidOperationException("This nest is not deleted.");
        return await MutateAsync(() => repository.RecoverAsync(NestId, Current!.Revision, ct), false, ct);
    }

    public async Task<NestRecord> DeleteAsync(string serverUrl, CancellationToken ct = default)
    {
        EnsureReady(serverUrl);
        if (Deleted) throw new InvalidOperationException("This nest is already deleted.");
        return await MutateAsync(() => repository.DeleteAsync(NestId, Current!.Revision, ct), true, ct);
    }

    private async Task<NestRecord> MutateAsync(Func<Task<NestRecord>> write, bool deleted, CancellationToken ct)
    {
        IsMutating = true;
        var expected = Current!.Revision;
        try
        {
            var result = await write();
            ValidateRecord(result, NestId, deleted);
            if (result.Revision != expected + 1) throw new InvalidDataException("Invalid mutation revision. Refresh to reconcile.");
            // Verify the exact target after the write; do not declare success from the response alone.
            var verified = deleted ? await repository.GetDeletedMetadataAsync(NestId, ct)
                : await repository.GetMetadataAsync(NestId, ct);
            if (verified == null || verified.Id != NestId || verified.Revision < result.Revision || verified.Deleted != deleted)
                throw new InvalidDataException("Could not verify the new current revision. Refresh to reconcile.");
            ct.ThrowIfCancellationRequested();
            if (disposed) throw new OperationCanceledException("The history browser was closed. Reopen to reconcile.");
            Current = verified;
            Deleted = deleted;
            Page = null;
            return result;
        }
        catch { Current = null; throw; }
        finally { IsMutating = false; }
    }

    private void EnsureReady(string url)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (Normalize(url) != ServerUrl) throw new InvalidOperationException("The nest server changed. Close this browser and reopen it on the selected server.");
        if (Current == null || IsMutating) throw new InvalidOperationException("Refresh the current version before continuing.");
    }

    private static void ValidateRecord(NestRecord record, Guid id, bool deleted)
    {
        if (record.Id != id || record.Deleted != deleted) throw new InvalidDataException("The server returned a different nest identity or visibility.");
    }

    private static string Normalize(string? url) => (url ?? "").Trim().TrimEnd('/');

    /// <summary>Read an immutable current snapshot and ensure metadata did not change during download.</summary>
    public static async Task<NestCurrentSnapshot> ReadCurrentAsync(INestRepository repository, Guid id,
        string serverUrl, CancellationToken ct = default)
    {
        var before = await repository.GetMetadataAsync(id, ct)
            ?? throw new FileNotFoundException("This nest no longer exists on the server.");
        ValidateRecord(before, id, false);
        var beforeMetadata = JsonSerializer.Serialize(before);
        var file = before.Revision > 0 ? await repository.GetRevisionFileAsync(id, before.Revision, ct)
            : await repository.GetFileAsync(id, ct);
        var after = await repository.GetMetadataAsync(id, ct);
        ct.ThrowIfCancellationRequested();
        if (after == null || JsonSerializer.Serialize(after) != beforeMetadata
            || file == null || file.LongLength != before.FileSize)
            throw new InvalidDataException("The current nest changed or its archive does not match its metadata. Refresh and open it again.");
        return new NestCurrentSnapshot(before, file, Normalize(serverUrl));
    }

    public void Clear()
    {
        if (IsMutating) throw new InvalidOperationException("Wait for the current operation to finish.");
        ++generation;
        pending?.Cancel();
        NestId = Guid.Empty;
        Current = null;
        Page = null;
    }

    public void Dispose()
    {
        disposed = true;
        ++generation;
        pending?.Cancel();
        Current = null;
        Page = null;
    }
}

public sealed record NestCurrentSnapshot(NestRecord Metadata, byte[] Archive, string ServerUrl);
