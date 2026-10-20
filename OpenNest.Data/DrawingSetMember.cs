namespace OpenNest.Data;

/// <summary>One drawing of a set and how many of it the set needs.</summary>
public sealed class DrawingSetMember
{
    public Guid DrawingId { get; set; }

    public int Quantity { get; set; } = 1;

    /// <summary>
    /// The drawing's current record, archived or not, joined by the server when it reads
    /// a set. Ignored on save.
    /// </summary>
    public DrawingLibraryRecord? Drawing { get; set; }
}
