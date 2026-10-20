namespace OpenNest.Data;

/// <summary>
/// One revision of a drawing set: a named list of library drawings with per-member
/// quantities (a kit, an assembly, a recurring order). Members reference drawings by id;
/// the set always means each drawing's current revision.
/// </summary>
public sealed class DrawingSetRecord
{
    /// <summary>Allocated by the client before the first save; kept by every revision.</summary>
    public Guid Id { get; set; }

    /// <summary>Immutable revision number, starting at 1. Ignored on save.</summary>
    public int Revision { get; set; }

    public string Name { get; set; } = "";

    public string Customer { get; set; } = "";

    public string Notes { get; set; } = "";

    /// <summary>Hidden from browsing by default; still readable by id.</summary>
    public bool Archived { get; set; }

    /// <summary>When the server stored this revision. Server-computed.</summary>
    public DateTime SavedAt { get; set; }

    /// <summary>Who saved this revision, as reported by the client.</summary>
    public string SavedBy { get; set; } = "";

    /// <summary>Number of members in this revision. Server-computed.</summary>
    public int MemberCount { get; set; }

    /// <summary>
    /// The members in order, one per drawing. Empty on query and history pages, which
    /// carry <see cref="MemberCount"/> only.
    /// </summary>
    public IReadOnlyList<DrawingSetMember> Members { get; set; } = Array.Empty<DrawingSetMember>();
}
