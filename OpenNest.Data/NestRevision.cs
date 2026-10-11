namespace OpenNest.Data;

/// <summary>A bounded page of immutable metadata snapshots, newest first.</summary>
public sealed class NestRevisionPage
{
    public const int DefaultLimit = 100;
    public const int MaxLimit = 200;
    public IReadOnlyList<NestRecord> Items { get; init; } = Array.Empty<NestRecord>();
    public int Total { get; init; }
    public int Offset { get; init; }
    public int Limit { get; init; }
}
