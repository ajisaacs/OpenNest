using OpenNest.Data;

namespace OpenNest.Tests.Data;

public class NestNameConflictsTests
{
    [Fact]
    public async Task FreeName_IsKeptWithoutAllocating()
    {
        var repository = new NamedRepository("N26-223X", "Other N26-223");

        Assert.Null(await NestNameConflicts.FindReplacementAsync(repository, "N26-223", 2026));
        Assert.Equal(0, repository.Allocations);
    }

    [Fact]
    public async Task ExactDuplicate_OffersTheNextAllocatedName()
    {
        var repository = new NamedRepository("N26-223") { NextNumber = 31 };

        var replacement = await NestNameConflicts.FindReplacementAsync(repository, "N26-223", 2026);

        Assert.NotNull(replacement);
        Assert.Equal(2026, replacement!.Year);
        Assert.Equal(31, replacement.Number);
        Assert.Equal(NestNames.Format(2026, 31), replacement.Name);
        Assert.Equal(1, repository.Allocations);
        Assert.Equal("N26-223", repository.Queries[0].Search);
    }

    [Fact]
    public async Task MatchIsExact_NotCaseFoldedOrSubstring()
    {
        var repository = new NamedRepository("n26-223", "N26-2234");

        Assert.False(await NestNameConflicts.IsNameTakenAsync(repository, "N26-223"));
    }

    [Fact]
    public async Task DeletedNestsDoNotCount()
    {
        var repository = new NamedRepository();
        repository.Records.Add(new NestRecord { Id = Guid.NewGuid(), Name = "Job", Deleted = true });

        Assert.False(await NestNameConflicts.IsNameTakenAsync(repository, "Job"));
    }

    [Fact]
    public async Task Rename_IgnoresTheNestsOwnRecord()
    {
        var repository = new NamedRepository("Job");
        var own = repository.Records[0].Id;

        Assert.False(await NestNameConflicts.IsNameTakenAsync(repository, "Job", own));
        Assert.True(await NestNameConflicts.IsNameTakenAsync(repository, "Job", Guid.NewGuid()));
    }

    [Fact]
    public async Task TryIsNameTaken_IsUnknownWhenTheServerCannotAnswer()
    {
        var repository = new NamedRepository("Job") { QueryFailure = new HttpRequestException("offline") };

        Assert.Null(await NestNameConflicts.TryIsNameTakenAsync(repository, "Job"));
        repository.QueryFailure = null;
        Assert.True(await NestNameConflicts.TryIsNameTakenAsync(repository, "Job"));
    }

    [Fact]
    public async Task DuplicateBeyondTheFirstPage_IsFound()
    {
        var names = Enumerable.Range(0, NestQuery.MaxLimit + 5).Select(i => $"{i:D4} Job").Append("Job");
        var repository = new NamedRepository(names.ToArray());

        Assert.True(await NestNameConflicts.IsNameTakenAsync(repository, "Job"));
        Assert.Equal(2, repository.Queries.Count);
        Assert.Equal(NestQuery.MaxLimit, repository.Queries[1].Offset);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task FailedCheckOrAllocation_KeepsTheCurrentName(bool queryFails, bool allocationFails)
    {
        var repository = new NamedRepository("Job")
        {
            QueryFailure = queryFails ? new HttpRequestException("offline") : null,
            AllocationFailure = allocationFails ? new NotSupportedException("old server") : null,
        };

        Assert.Null(await NestNameConflicts.FindReplacementAsync(repository, "Job", 2026));
    }

    [Fact]
    public async Task Timeout_KeepsTheCurrentName()
    {
        var repository = new NamedRepository("Job");
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        Assert.Null(await NestNameConflicts.FindReplacementAsync(repository, "Job", 2026, cancelled.Token));
    }

    private sealed class NamedRepository : INestRepository
    {
        public NamedRepository(params string[] names) =>
            Records.AddRange(names.Select(n => new NestRecord { Id = Guid.NewGuid(), Name = n }));

        public List<NestRecord> Records { get; } = [];
        public List<NestQuery> Queries { get; } = [];
        public int NextNumber { get; set; } = 1;
        public int Allocations { get; private set; }
        public Exception? QueryFailure { get; set; }
        public Exception? AllocationFailure { get; set; }

        public Task<NestPage> QueryAsync(NestQuery query, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Queries.Add(query);
            if (QueryFailure != null)
                throw QueryFailure;
            // Mirrors the server: case-insensitive substring search over active nests only.
            var matches = Records
                .Where(r => !r.Deleted && r.Name.Contains(query.NormalizedSearch, StringComparison.OrdinalIgnoreCase))
                .OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            return Task.FromResult(new NestPage
            {
                Items = matches.Skip(query.Offset).Take(query.Limit).ToList(),
                Total = matches.Count,
                Offset = query.Offset,
                Limit = query.Limit,
            });
        }

        public Task<int> AllocateNestNumberAsync(int year, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (AllocationFailure != null)
                throw AllocationFailure;
            Allocations++;
            return Task.FromResult(NextNumber++);
        }

        public Task<IReadOnlyList<NestRecord>> ListAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<NestRecord?> GetMetadataAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<byte[]?> GetFileAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<NestRecord> UploadAsync(byte[] file, NestRecord record, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<NestRecord> UpdateFileAsync(Guid id, byte[] file, NestRecord record, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<NestRecord> UpdateMetadataAsync(Guid id, NestRecord record, CancellationToken ct = default) => throw new NotSupportedException();
        public Task DeleteAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
