using OpenNest;

namespace OpenNest.Data;

/// <summary>How a release reached one of its nests.</summary>
public enum SetReleaseNestKind
{
    /// <summary>The release uploaded the nest as new.</summary>
    Created,

    /// <summary>The release added its parts to an existing draft nest and renested it.</summary>
    Merged,
}

/// <summary>What undoing the release did to one of its nests.</summary>
public enum SetReleaseNestUndo
{
    /// <summary>A created nest was deleted (it can still be recovered).</summary>
    Deleted,

    /// <summary>A merged nest was restored to <see cref="SetReleaseNest.RevisionBefore"/>.</summary>
    Restored,

    /// <summary>The operator left the nest as it was, because it had changed since the release.</summary>
    Skipped,
}

/// <summary>One nest of a release, the material group it was made for, and what the release put in it.</summary>
public sealed class SetReleaseNest
{
    public Guid NestId { get; set; }

    public SetReleaseNestKind Kind { get; set; } = SetReleaseNestKind.Created;

    /// <summary>The draft's revision the release merged into; null for a created nest.</summary>
    public int? RevisionBefore { get; set; }

    /// <summary>The nest revision the release wrote. Undo acts only while the nest is still at it.</summary>
    public int RevisionAfter { get; set; }

    public string MaterialName { get; set; } = "";

    public string MaterialGrade { get; set; } = "";

    /// <summary>Thickness in <see cref="Units"/>, stored rounded to four decimals.</summary>
    public double Thickness { get; set; }

    public Units Units { get; set; } = Units.Inches;

    public CutType CutType { get; set; } = CutType.Laser;

    /// <summary>
    /// The drawing quantities this release added to the nest, per set and for the drawings
    /// released on their own. Empty for releases
    /// recorded before items were kept (schema v8).
    /// </summary>
    public IReadOnlyList<SetReleaseNestItem> Items { get; set; } = Array.Empty<SetReleaseNestItem>();

    /// <summary>What undoing the release did to the nest; null while the release stands. Server-computed.</summary>
    public SetReleaseNestUndo? Undo { get; set; }

    /// <summary>The nest revision the undo wrote; null when it wrote none. Server-computed.</summary>
    public int? UndoRevision { get; set; }

    /// <summary>
    /// The nest's current metadata, deleted or not, joined by the server when it reads a
    /// release, so status and deletion are always up to date. Ignored on post.
    /// </summary>
    public NestRecord? Nest { get; set; }
}

/// <summary>A drawing quantity one set of a release, or the drawing released on its own, added to one nest.</summary>
public sealed class SetReleaseNestItem
{
    /// <summary>The set that asked for the quantity; null for a drawing released on its own.</summary>
    public Guid? SetId { get; set; }

    public Guid DrawingId { get; set; }

    /// <summary>The library revision of the drawing that went into the nest.</summary>
    public int DrawingRevision { get; set; }

    public int Quantity { get; set; }
}
