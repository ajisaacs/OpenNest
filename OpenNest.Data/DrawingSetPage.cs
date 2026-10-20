namespace OpenNest.Data;

/// <summary>One bounded page of drawing set records, without their members.</summary>
public sealed class DrawingSetPage
{
    /// <summary>At most <see cref="Limit"/> records, in the requested order.</summary>
    public IReadOnlyList<DrawingSetRecord> Items { get; init; } = Array.Empty<DrawingSetRecord>();

    /// <summary>Number of records matching the request across all pages.</summary>
    public int Total { get; init; }

    public int Offset { get; init; }

    public int Limit { get; init; }
}
