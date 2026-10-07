using OpenNest.Data;

namespace OpenNest.Tests.Data;

public class NestDetailsSessionTests
{
    [Fact]
    public async Task Load_AppliesTheNestsDetails()
    {
        var id = Guid.NewGuid();
        var expected = new NestDetails();
        var requested = new List<Guid>();
        using var session = new NestDetailsSession((nestId, _) =>
        {
            requested.Add(nestId);
            return Task.FromResult(expected);
        });

        Assert.True(await session.LoadAsync(id));

        Assert.Equal(new[] { id }, requested);
        Assert.Equal(id, session.NestId);
        Assert.Same(expected, session.Details);
        Assert.False(session.IsLoading);
    }

    [Fact]
    public async Task OlderResponse_ArrivingLast_NeverReplacesTheNewerNestsDetails()
    {
        var loads = new ControlledLoads();
        using var session = new NestDetailsSession(loads.Load);
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();

        var firstLoad = session.LoadAsync(first);
        var secondLoad = session.LoadAsync(second);
        Assert.True(loads.Tokens[0].IsCancellationRequested);

        var secondDetails = new NestDetails();
        loads.Results[1].SetResult(secondDetails);
        Assert.True(await secondLoad);
        loads.Results[0].SetResult(new NestDetails());
        Assert.False(await firstLoad);

        Assert.Equal(second, session.NestId);
        Assert.Same(secondDetails, session.Details);
    }

    [Fact]
    public async Task SupersededFailure_IsIgnored_LatestFailure_ClearsTheDetailsAndPropagates()
    {
        var loads = new ControlledLoads();
        using var session = new NestDetailsSession(loads.Load);

        var applied = session.LoadAsync(Guid.NewGuid());
        loads.Results[0].SetResult(new NestDetails());
        await applied;

        var superseded = session.LoadAsync(Guid.NewGuid());
        var latest = session.LoadAsync(Guid.NewGuid());
        Assert.Null(session.Details);

        loads.Results[1].SetException(new InvalidOperationException("old"));
        Assert.False(await superseded);
        Assert.True(session.IsLoading);

        loads.Results[2].SetException(new InvalidOperationException("This nest no longer exists on the server."));
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => latest);
        Assert.Contains("no longer exists", error.Message);
        Assert.Null(session.Details);
        Assert.False(session.IsLoading);
    }

    [Fact]
    public async Task SupersededLoad_CompletingSynchronouslyOnCancellation_IsNotReportedAsTheLatestFailure()
    {
        var calls = 0;
        var latestDetails = new NestDetails();
        using var session = new NestDetailsSession((_, token) =>
        {
            if (++calls > 1)
                return Task.FromResult(latestDetails);

            var cancelled = new TaskCompletionSource<NestDetails>();
            token.Register(() => cancelled.SetCanceled(token));
            return cancelled.Task;
        });

        var superseded = session.LoadAsync(Guid.NewGuid());
        var latest = session.LoadAsync(Guid.NewGuid());

        Assert.False(await superseded);
        Assert.True(await latest);
        Assert.Same(latestDetails, session.Details);
        Assert.False(session.IsLoading);
    }

    [Fact]
    public async Task IsLoading_FollowsOnlyTheLatestLoad()
    {
        var loads = new ControlledLoads();
        using var session = new NestDetailsSession(loads.Load);

        var first = session.LoadAsync(Guid.NewGuid());
        var second = session.LoadAsync(Guid.NewGuid());
        loads.Results[1].SetResult(new NestDetails());
        await second;
        Assert.False(session.IsLoading);

        // The superseded load is still outstanding but no longer counts.
        Assert.False(first.IsCompleted);
        loads.Results[0].SetResult(new NestDetails());
        Assert.False(await first);
        Assert.False(session.IsLoading);
    }

    [Fact]
    public async Task Clear_DiscardsTheLoadInFlightAndForgetsTheNest()
    {
        var loads = new ControlledLoads();
        using var session = new NestDetailsSession(loads.Load);

        var load = session.LoadAsync(Guid.NewGuid());
        session.Clear();

        Assert.True(loads.Tokens[0].IsCancellationRequested);
        Assert.False(session.IsLoading);
        Assert.Null(session.NestId);
        loads.Results[0].SetResult(new NestDetails());
        Assert.False(await load);
        Assert.Null(session.Details);
    }

    [Fact]
    public async Task Dispose_CancelsTheLoadAndDiscardsItsLateResponse()
    {
        var loads = new ControlledLoads();
        var session = new NestDetailsSession(loads.Load);

        var load = session.LoadAsync(Guid.NewGuid());
        session.Dispose();

        Assert.True(loads.Tokens[0].IsCancellationRequested);
        loads.Results[0].SetResult(new NestDetails());
        Assert.False(await load);
        Assert.Null(session.Details);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => session.LoadAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task Prefetch_ThenLoad_ResolvesFromTheCacheWithoutASecondRequest()
    {
        var requested = new List<Guid>();
        var details = new NestDetails();
        using var session = new NestDetailsSession((id, _) =>
        {
            requested.Add(id);
            return Task.FromResult(details);
        });
        var nestId = Guid.NewGuid();

        session.Prefetch(nestId);
        Assert.Equal(new[] { nestId }, requested);

        Assert.True(await session.LoadAsync(nestId));
        Assert.Same(details, session.Details);
        Assert.Equal(new[] { nestId }, requested);
    }

    [Fact]
    public async Task Prefetch_SkipsWhenAlreadyTheCurrentNest_OrAlreadyCached()
    {
        var requested = new List<Guid>();
        using var session = new NestDetailsSession((id, _) =>
        {
            requested.Add(id);
            return Task.FromResult(new NestDetails());
        });
        var nestId = Guid.NewGuid();

        await session.LoadAsync(nestId);
        Assert.Equal(new[] { nestId }, requested);

        // Already the current nest.
        session.Prefetch(nestId);
        Assert.Equal(new[] { nestId }, requested);

        // No longer current, but its earlier load is still cached.
        session.Clear();
        session.Prefetch(nestId);
        Assert.Equal(new[] { nestId }, requested);
    }

    [Fact]
    public async Task Clear_CancelsAnInFlightPrefetch_SoItsResultIsNeverCached()
    {
        var loads = new ControlledLoads();
        using var session = new NestDetailsSession(loads.Load);
        var nestId = Guid.NewGuid();

        session.Prefetch(nestId);
        Assert.False(loads.Tokens[0].IsCancellationRequested);

        session.Clear();
        Assert.True(loads.Tokens[0].IsCancellationRequested);
        loads.Results[0].SetResult(new NestDetails());

        // Whether or not the cancelled prefetch's continuation has run yet, it must not have
        // populated the cache, so loading the same nest issues a fresh request.
        var fresh = new NestDetails();
        var load = session.LoadAsync(nestId);
        Assert.Equal(2, loads.Results.Count);
        loads.Results[1].SetResult(fresh);
        Assert.True(await load);
        Assert.Same(fresh, session.Details);
    }

    /// <summary>Each load waits for its own result, which the test completes explicitly.</summary>
    private sealed class ControlledLoads
    {
        public List<TaskCompletionSource<NestDetails>> Results { get; } = new();

        public List<CancellationToken> Tokens { get; } = new();

        public Task<NestDetails> Load(Guid id, CancellationToken token)
        {
            var result = new TaskCompletionSource<NestDetails>(TaskCreationOptions.RunContinuationsAsynchronously);
            Results.Add(result);
            Tokens.Add(token);
            return result.Task;
        }
    }
}
