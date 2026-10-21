namespace OpenNest.Data;

/// <summary>
/// One release of one or more drawing sets: the nests it made or merged into, one per
/// material group, with every member quantity multiplied by its set line's quantity.
/// Releases are append-only; undoing one restores or deletes its nests and marks it
/// undone, it never removes the record.
/// </summary>
public sealed class SetReleaseRecord
{
    /// <summary>Allocated by the client, so an ambiguous post can be retried without a duplicate.</summary>
    public Guid Id { get; set; }

    /// <summary>The sets released and how many times each, in the order posted.</summary>
    public IReadOnlyList<SetReleaseLine> Sets { get; set; } = Array.Empty<SetReleaseLine>();

    public string Notes { get; set; } = "";

    /// <summary>When the server stored the release. Server-computed.</summary>
    public DateTime ReleasedAt { get; set; }

    /// <summary>Who released the sets, as reported by the client.</summary>
    public string ReleasedBy { get; set; } = "";

    /// <summary>When the release was undone; null while it stands. Server-computed.</summary>
    public DateTime? UndoneAt { get; set; }

    /// <summary>Who undid the release, as reported by the client. Server-computed.</summary>
    public string UndoneBy { get; set; } = "";

    /// <summary>The nests the release created or merged into, in the order posted.</summary>
    public IReadOnlyList<SetReleaseNest> Nests { get; set; } = Array.Empty<SetReleaseNest>();

    public bool IsUndone => UndoneAt.HasValue;
}

/// <summary>One set of a release: the exact revision released and how many kits of it.</summary>
public sealed class SetReleaseLine
{
    public Guid SetId { get; set; }

    /// <summary>The exact set revision released; it need not be the current one.</summary>
    public int SetRevision { get; set; }

    /// <summary>How many kits: every member quantity is multiplied by this.</summary>
    public int Quantity { get; set; } = 1;

    /// <summary>The set's name at <see cref="SetRevision"/>. Server-joined; ignored on post.</summary>
    public string SetName { get; set; } = "";
}
