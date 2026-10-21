namespace OpenNest.Data;

/// <summary>One bounded page of set releases, newest first, each with its sets and nests.</summary>
public sealed class SetReleasePage
{
    public IReadOnlyList<SetReleaseRecord> Items { get; init; } = Array.Empty<SetReleaseRecord>();

    /// <summary>Number of matching releases across all pages.</summary>
    public int Total { get; init; }

    public int Offset { get; init; }

    public int Limit { get; init; }
}
