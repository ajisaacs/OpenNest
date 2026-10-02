namespace OpenNest.Data;

/// <summary>One page of saved-nest metadata returned by <c>GET /api/nests/query</c>.</summary>
public sealed class NestPage
{
    /// <summary>At most <see cref="Limit"/> matching records, in the requested order.</summary>
    public IReadOnlyList<NestRecord> Items { get; init; } = Array.Empty<NestRecord>();

    /// <summary>Number of records matching the search across all pages.</summary>
    public int Total { get; init; }

    public int Offset { get; init; }

    public int Limit { get; init; }
}
