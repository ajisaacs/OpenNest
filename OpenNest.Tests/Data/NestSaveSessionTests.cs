using OpenNest.Data;

namespace OpenNest.Tests.Data;

public class NestSaveSessionTests
{
    [Fact]
    public async Task Save_CreatesThenUpdatesAndSaveCopyCreatesNewRecord()
    {
        var repository = new FakeRepository();
        var session = new NestSaveSession();
        var nest = new Nest("Shop job");
        var bytes = new byte[] { 1, 2, 3 };

        await session.SaveAsync(repository, "http://shop:8090/", nest, bytes);
        var firstId = session.RemoteId;
        Assert.NotEqual(Guid.Empty, firstId);
        Assert.Equal(3, repository.LastRecord!.FileSize);
        Assert.Equal("Shop job", repository.LastRecord.Name);
        Assert.Equal(1, repository.Uploads);

        await session.SaveAsync(repository, "http://shop:8090", nest, bytes);
        Assert.Equal(firstId, repository.UpdatedId);
        Assert.Equal(1, repository.Updates);

        await session.SaveAsync(repository, "http://shop:8090", nest, bytes, saveCopy: true);
        Assert.NotEqual(firstId, session.RemoteId);
        Assert.Equal(2, repository.Uploads);
    }

    [Fact]
    public async Task FailedSaveAndServerSwitch_NeverOverwritePriorIdentity()
    {
        var repository = new FakeRepository();
        var session = new NestSaveSession();
        var oldId = Guid.NewGuid();
        session.Bind(oldId, "http://old:8090");
        repository.Fail = true;

        await Assert.ThrowsAsync<IOException>(() =>
            session.SaveAsync(repository, "http://new:8090", new Nest("Job"), [1]));
        Assert.Equal(oldId, session.RemoteId);
        Assert.Equal("http://old:8090", session.ServerUrl);
        Assert.Equal(Guid.Empty, repository.LastRecord!.Id);
        Assert.Equal(0, repository.Updates);

        repository.Fail = false;
        await session.SaveAsync(repository, "http://new:8090", new Nest("Job"), [1]);
        Assert.NotEqual(oldId, session.RemoteId);
        Assert.Equal("http://new:8090", session.ServerUrl);
    }

    private sealed class FakeRepository : INestRepository
    {
        public int Uploads { get; private set; }
        public int Updates { get; private set; }
        public Guid UpdatedId { get; private set; }
        public NestRecord? LastRecord { get; private set; }
        public bool Fail { get; set; }

        public Task<NestRecord> UploadAsync(byte[] file, NestRecord record, CancellationToken ct = default)
        {
            Uploads++;
            LastRecord = record;
            if (Fail) throw new IOException("Offline");
            record.Id = Guid.NewGuid();
            return Task.FromResult(record);
        }

        public Task<NestRecord> UpdateFileAsync(Guid id, byte[] file, NestRecord record, CancellationToken ct = default)
        {
            Updates++;
            UpdatedId = id;
            LastRecord = record;
            if (Fail) throw new IOException("Offline");
            return Task.FromResult(record);
        }

        public Task<IReadOnlyList<NestRecord>> ListAsync(CancellationToken ct = default) => throw new NotImplementedException();
        public Task<NestPage> QueryAsync(NestQuery query, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<NestRecord?> GetMetadataAsync(Guid id, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<byte[]?> GetFileAsync(Guid id, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<NestRecord> UpdateMetadataAsync(Guid id, NestRecord record, CancellationToken ct = default) => throw new NotImplementedException();
        public Task DeleteAsync(Guid id, CancellationToken ct = default) => throw new NotImplementedException();
    }
}
