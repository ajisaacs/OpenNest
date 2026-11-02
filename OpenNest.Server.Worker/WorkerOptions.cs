namespace OpenNest.Server.Worker;

/// <summary>Worker settings read from the environment.</summary>
public sealed class WorkerOptions
{
    public const string ServerVariable = "OPENNEST_SERVER_URL";
    public const string PollVariable = "OPENNEST_WORKER_POLL_SECONDS";
    public const string AliveVariable = "OPENNEST_WORKER_ALIVE_FILE";

    /// <summary>The nest server the worker takes jobs from.</summary>
    public required string ServerUrl { get; init; }

    /// <summary>How long to wait before asking again when no job was queued.</summary>
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>Touched every round; the container health check fails when it goes stale.</summary>
    public string AliveFile { get; init; } = Path.Combine(Path.GetTempPath(), "opennest-worker.alive");

    public static WorkerOptions FromEnvironment(Func<string, string?> read)
    {
        var server = read(ServerVariable);
        if (string.IsNullOrWhiteSpace(server))
            throw new InvalidOperationException($"{ServerVariable} must name the nest server, for example http://opennest-server:8090/.");
        var poll = TimeSpan.FromSeconds(5);
        if (read(PollVariable) is { Length: > 0 } text)
        {
            if (!int.TryParse(text, out var seconds) || seconds is < 1 or > 3600)
                throw new InvalidOperationException($"{PollVariable} must be whole seconds from 1 to 3600.");
            poll = TimeSpan.FromSeconds(seconds);
        }
        return new WorkerOptions
        {
            ServerUrl = server.Trim(),
            PollInterval = poll,
            AliveFile = read(AliveVariable) is { Length: > 0 } alive ? alive : Path.Combine(Path.GetTempPath(), "opennest-worker.alive"),
        };
    }
}
