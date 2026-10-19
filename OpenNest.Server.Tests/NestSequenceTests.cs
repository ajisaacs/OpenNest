using OpenNest.Data;

namespace OpenNest.Server.Tests;

public sealed class NestSequenceTests
{
    [Fact]
    public void Allocation_CountsPerYearAndSurvivesReopen()
    {
        using var factory = new ServerFactory();
        using (var database = new NestDatabase(factory.DatabasePath, pooling: false))
        {
            Assert.Equal(1, database.AllocateNestNumber(2026));
            Assert.Equal(2, database.AllocateNestNumber(2026));
            Assert.Equal(1, database.AllocateNestNumber(2027));
        }

        using var reopened = new NestDatabase(factory.DatabasePath, pooling: false);
        Assert.Equal(3, reopened.AllocateNestNumber(2026));
    }

    [Fact]
    public void Allocation_SkipsPastStoredGeneratedNamesIncludingDeleted()
    {
        using var factory = new ServerFactory();
        using var database = new NestDatabase(factory.DatabasePath, pooling: false);
        database.Insert(Guid.NewGuid(), new NestRecord { Name = NestNames.Format(2026, 40) }, [1]);
        var deleted = database.Insert(Guid.NewGuid(), new NestRecord { Name = NestNames.Format(2026, 75) }, [2]);
        database.SetDeleted(deleted.Id, true, deleted.Revision);
        // Another year's name, a renamed nest and non-canonical padding never move the counter.
        database.Insert(Guid.NewGuid(), new NestRecord { Name = NestNames.Format(2025, 900) }, [3]);
        database.Insert(Guid.NewGuid(), new NestRecord { Name = "N26-ZZZ customer copy" }, [4]);
        database.Insert(Guid.NewGuid(), new NestRecord { Name = "N26-2ZZZ" }, [5]);

        Assert.Equal(76, database.AllocateNestNumber(2026));
        Assert.Equal(77, database.AllocateNestNumber(2026));

        // A name uploaded later (an offline fallback) still pushes the counter past it.
        database.Insert(Guid.NewGuid(), new NestRecord { Name = NestNames.Format(2026, 200) }, [6]);
        Assert.Equal(201, database.AllocateNestNumber(2026));
    }

    [Fact]
    public async Task Allocation_IsUniqueUnderConcurrentClients()
    {
        using var factory = new ServerFactory();
        using var client = factory.CreateClient();
        var repository = new RemoteNestRepository(client, client.BaseAddress!.ToString());

        var numbers = await Task.WhenAll(
            Enumerable.Range(0, 40).Select(_ => repository.AllocateNestNumberAsync(2026)));

        Assert.Equal(Enumerable.Range(1, 40), numbers.Order());
    }

    [Fact]
    public async Task DuplicateFirstSaveName_GetsAFreshNameOnlyForActiveExactMatches()
    {
        using var factory = new ServerFactory();
        using (var database = new NestDatabase(factory.DatabasePath, pooling: false))
        {
            database.Insert(Guid.NewGuid(), new NestRecord { Name = NestNames.Format(2026, 40) }, [1]);
            var deleted = database.Insert(Guid.NewGuid(), new NestRecord { Name = "Old job" }, [2]);
            database.SetDeleted(deleted.Id, true, deleted.Revision);
        }
        using var client = factory.CreateClient();
        var repository = new RemoteNestRepository(client, client.BaseAddress!.ToString());

        var replacement = await NestNameConflicts.FindReplacementAsync(repository, NestNames.Format(2026, 40), 2026);
        Assert.Equal(NestNames.Format(2026, 41), replacement?.Name);

        Assert.Null(await NestNameConflicts.FindReplacementAsync(repository, "Old job", 2026));
        Assert.Null(await NestNameConflicts.FindReplacementAsync(repository, NestNames.Format(2026, 40).ToLowerInvariant(), 2026));
    }

    [Fact]
    public async Task Endpoint_RejectsYearsOutsideTheNameRange()
    {
        using var factory = new ServerFactory();
        using var client = factory.CreateClient();

        using var response = await client.PostAsync("/api/nest-names/next?year=1999", null);

        Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            new RemoteNestRepository(client, client.BaseAddress!.ToString()).AllocateNestNumberAsync(2100));
    }
}
