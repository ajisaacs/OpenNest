namespace OpenNest.Data;

/// <summary>One bounded page of library drawing metadata.</summary>
public sealed class DrawingLibraryPage : IBrowsePage
{
    /// <summary>At most <see cref="Limit"/> records, in the requested order.</summary>
    public IReadOnlyList<DrawingLibraryRecord> Items { get; init; } = Array.Empty<DrawingLibraryRecord>();

    /// <summary>Number of records matching the request across all pages.</summary>
    public int Total { get; init; }

    public int Offset { get; init; }

    public int Limit { get; init; }

    int IBrowsePage.ItemCount => Items.Count;
}
