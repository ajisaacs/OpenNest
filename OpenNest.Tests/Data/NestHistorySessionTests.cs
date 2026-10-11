using OpenNest.Data;

namespace OpenNest.Tests.Data;

public class NestHistorySessionTests
{
    [Fact]
    public async Task BoundedHistory_DownloadIsByteExactAndReadOnly()
    {
        var repository = new Repository();
        using var session = new NestHistorySession(repository, "http://shop/");
        Assert.True(await session.LoadAsync(repository.Id));
        Assert.Equal(NestHistorySession.PageSize, repository.Limit);
        Assert.Equal(repository.Bytes, await session.DownloadAsync(2, "http://shop"));
        Assert.Equal(0, repository.Writes);
        Assert.Equal(5, session.Current!.Revision);
        Assert.Equal("Earlier notes", session.Page!.Items[0].Comments);
    }

    [Fact]
    public async Task Restore_UsesDisplayedPreconditionSameIdAndVerifiesCurrent()
    {
        var repository = new Repository();
        using var session = new NestHistorySession(repository, "http://shop");
        await session.LoadAsync(repository.Id);
        var restored = await session.RestoreAsync(2, "http://shop");
        Assert.Equal(repository.Id, restored.Id);
        Assert.Equal(6, restored.Revision);
        Assert.Equal(5, repository.Expected);
        Assert.Equal(2, repository.Source);
        Assert.Equal(2, repository.MetadataReads);
        Assert.Null(session.Page);
        Assert.True(await session.LoadAsync(repository.Id));
        Assert.Equal(6, session.Current!.Revision);
    }

    [Fact]
    public async Task Recovery_IsExplicitAndUsesDeletedEndpoints()
    {
        var repository = new Repository { Deleted = true };
        using var session = new NestHistorySession(repository, "http://shop");
        await session.LoadAsync(repository.Id, true);
        Assert.Equal(repository.Bytes, await session.DownloadAsync(2, "http://shop"));
        Assert.Equal(1, repository.DeletedDownloads);
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.RestoreAsync(2, "http://shop"));
        var recovered = await session.RecoverAsync("http://shop");
        Assert.False(recovered.Deleted);
        Assert.Equal(6, recovered.Revision);
        Assert.Equal(5, repository.Expected);
    }

    [Fact]
    public async Task FailedRestore_PreservesHistoryButDisablesBlindRetry()
    {
        var repository = new Repository { Fail = true };
        using var session = new NestHistorySession(repository, "http://shop");
        await session.LoadAsync(repository.Id);
        var page = session.Page;
        await Assert.ThrowsAsync<IOException>(() => session.RestoreAsync(2, "http://shop"));
        Assert.Same(page, session.Page);
        Assert.Null(session.Current);
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.RestoreAsync(2, "http://shop"));
        Assert.Equal(1, repository.Writes);
        Assert.Equal(5, repository.Revision);
        repository.Fail = false;
        await session.LoadAsync(repository.Id);
        Assert.NotNull(session.Current);
    }

    [Fact]
    public async Task ServerSwitch_RefusesDownloadRestoreAndRecoveryWithoutRequests()
    {
        var repository = new Repository();
        using var session = new NestHistorySession(repository, "http://shop");
        await session.LoadAsync(repository.Id);
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.DownloadAsync(2, "http://other"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.RestoreAsync(2, "http://other"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.RecoverAsync("http://other"));
        Assert.Equal(0, repository.Downloads);
        Assert.Equal(0, repository.Writes);
    }

    [Fact]
    public async Task StaleSelection_IgnoresBackendThatIgnoresCancellation()
    {
        var repository = new Repository();
        var held = new TaskCompletionSource<NestRevisionPage>(TaskCreationOptions.RunContinuationsAsynchronously);
        repository.History = held.Task;
        using var session = new NestHistorySession(repository, "http://shop");
        var first = session.LoadAsync(repository.Id);
        repository.History = null;
        var secondId = Guid.NewGuid();
        await session.LoadAsync(secondId);
        held.SetResult(new NestRevisionPage { Items = [repository.Record(repository.Id, 2)], Total = 1, Limit = 50 });
        Assert.False(await first);
        Assert.Equal(secondId, session.Current!.Id);
    }

    [Fact]
    public async Task CancelAndClear_NeverPublishLateHistory()
    {
        var repository = new Repository();
        var held = new TaskCompletionSource<NestRevisionPage>(TaskCreationOptions.RunContinuationsAsynchronously);
        repository.History = held.Task;
        using var session = new NestHistorySession(repository, "http://shop");
        using var cancellation = new CancellationTokenSource();
        var task = session.LoadAsync(repository.Id, cancellationToken: cancellation.Token);
        cancellation.Cancel();
        session.Clear();
        held.SetResult(new NestRevisionPage { Items = [repository.Record(repository.Id, 2)], Total = 1, Limit = 50 });
        Assert.False(await task);
        Assert.Null(session.Current);
        Assert.Null(session.Page);
    }

    [Fact]
    public async Task LegacyServer_HistoryRefusesButCurrentReadRetainsZeroRevision()
    {
        var repository = new Repository { Revision = 0 };
        using var session = new NestHistorySession(repository, "http://shop");
        await Assert.ThrowsAsync<NotSupportedException>(() => session.LoadAsync(repository.Id));
        var snapshot = await NestHistorySession.ReadCurrentAsync(repository, repository.Id, "http://shop/");
        Assert.Equal(0, snapshot.Metadata.Revision);
        Assert.Equal("http://shop", snapshot.ServerUrl);
        Assert.Equal(repository.Bytes, snapshot.Archive);
    }

    [Fact]
    public async Task CurrentOpen_UsesImmutableRevisionNotUnversionedFile()
    {
        var repository = new Repository();
        var snapshot = await NestHistorySession.ReadCurrentAsync(repository, repository.Id, "http://shop");
        Assert.Equal(5, repository.DownloadRevision);
        Assert.Equal(0, repository.LegacyDownloads);
        var save = new NestSaveSession();
        save.Bind(snapshot.Metadata.Id, snapshot.ServerUrl, snapshot.Metadata.Revision);
        Assert.Equal(5, save.Revision);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CurrentOpen_RejectsChangedMetadataOrMismatchingArchive(bool concurrent)
    {
        var repository = new Repository { ChangeDuringDownload = concurrent, WrongSize = !concurrent };
        await Assert.ThrowsAsync<InvalidDataException>(() => NestHistorySession.ReadCurrentAsync(repository, repository.Id, "http://shop"));
    }

    [Fact]
    public async Task Mutation_VerificationFailureIsNotSuccess()
    {
        var repository = new Repository { WrongVerification = true };
        using var session = new NestHistorySession(repository, "http://shop");
        await session.LoadAsync(repository.Id);
        await Assert.ThrowsAsync<InvalidDataException>(() => session.RestoreAsync(2, "http://shop"));
        Assert.Null(session.Current);
        Assert.Equal(1, repository.Writes);
    }

    [Fact]
    public async Task Download_SelectionChangedDuringReadDropsLateBytes()
    {
        var repository = new Repository();
        using var session = new NestHistorySession(repository, "http://shop");
        await session.LoadAsync(repository.Id);
        var held = new TaskCompletionSource<byte[]?>(TaskCreationOptions.RunContinuationsAsynchronously);
        repository.File = held.Task;
        var read = session.DownloadAsync(2, "http://shop");
        session.Clear();
        held.SetResult(repository.Bytes);
        Assert.Null(await read);
    }

    private sealed class Repository : INestRepository
    {
        public Guid Id { get; } = Guid.NewGuid();
        public byte[] Bytes { get; } = [1, 2, 3];
        public int Revision = 5;
        public bool Deleted, Fail, ChangeDuringDownload, WrongSize, WrongVerification;
        public int Limit, Writes, Expected, Source, MetadataReads, Downloads, DeletedDownloads, LegacyDownloads, DownloadRevision;
        public Task<NestRevisionPage>? History;
        public Task<byte[]?>? File;
        public NestRecord Record(Guid id, int revision) => new()
        {
            Id = id,
            Revision = revision,
            Deleted = Deleted,
            FileSize = WrongSize ? 100 : Bytes.Length,
            Comments = "Earlier notes",
        };
        public Task<NestRecord?> GetMetadataAsync(Guid id, CancellationToken ct = default)
        {
            ++MetadataReads;
            return Task.FromResult<NestRecord?>(Record(id, WrongVerification && Writes > 0 ? 1 : Revision));
        }
        public Task<NestRecord?> GetDeletedMetadataAsync(Guid id, CancellationToken ct = default) => GetMetadataAsync(id, ct);
        public Task<NestRevisionPage> GetRevisionsAsync(Guid id, int offset = 0, int limit = 100, CancellationToken cancellationToken = default)
        {
            Limit = limit;
            return History ?? Task.FromResult(new NestRevisionPage { Items = [Record(id, 2)], Total = offset + 1, Offset = offset, Limit = limit });
        }
        public Task<NestRevisionPage> GetDeletedRevisionsAsync(Guid id, int offset = 0, int limit = 100, CancellationToken cancellationToken = default) => GetRevisionsAsync(id, offset, limit, cancellationToken);
        public Task<byte[]?> GetRevisionFileAsync(Guid id, int revisionNumber, CancellationToken cancellationToken = default)
        {
            Downloads++;
            DownloadRevision = revisionNumber;
            if (ChangeDuringDownload) Revision++;
            return File ?? Task.FromResult<byte[]?>(Bytes);
        }
        public Task<byte[]?> GetDeletedRevisionFileAsync(Guid id, int revisionNumber, CancellationToken cancellationToken = default)
        {
            DeletedDownloads++;
            return GetRevisionFileAsync(id, revisionNumber, cancellationToken);
        }
        public Task<byte[]?> GetFileAsync(Guid id, CancellationToken ct = default) { LegacyDownloads++; return Task.FromResult<byte[]?>(Bytes); }
        public Task<NestRecord> RestoreAsync(Guid id, int sourceRevision, int expectedRevision, CancellationToken ct = default)
        {
            Writes++; Expected = expectedRevision; Source = sourceRevision;
            if (Fail) throw new IOException("Conflict: current version 6. Reload or Save As; no automatic retry.");
            Deleted = false;
            Revision++;
            return Task.FromResult(Record(id, Revision));
        }
        public Task<NestRecord> RecoverAsync(Guid id, int expectedRevision, CancellationToken ct = default) => RestoreAsync(id, 0, expectedRevision, ct);
        public Task<IReadOnlyList<NestRecord>> ListAsync(CancellationToken ct = default) => throw new NotImplementedException();
        public Task<NestPage> QueryAsync(NestQuery query, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<NestRecord> UploadAsync(byte[] file, NestRecord record, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<NestRecord> UpdateFileAsync(Guid id, byte[] file, NestRecord record, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<NestRecord> UpdateMetadataAsync(Guid id, NestRecord record, CancellationToken ct = default) => throw new NotImplementedException();
        public Task DeleteAsync(Guid id, CancellationToken ct = default) => throw new NotImplementedException();
    }
}
