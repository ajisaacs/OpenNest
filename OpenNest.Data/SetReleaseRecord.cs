namespace OpenNest.Data;

/// <summary>
/// One release of a drawing set revision: the nests made from it, one per material group,
/// with every member quantity multiplied by <see cref="Multiplier"/>. Releases are
/// append-only; a mistaken one is undone by deleting its nests, which then read as deleted.
/// </summary>
public sealed class SetReleaseRecord
{
    /// <summary>Allocated by the client, so an ambiguous post can be retried without a duplicate.</summary>
    public Guid Id { get; set; }

    /// <summary>The set released. Taken from the route on post.</summary>
    public Guid SetId { get; set; }

    /// <summary>The exact set revision released; it need not be the current one.</summary>
    public int SetRevision { get; set; }

    /// <summary>The set's name at <see cref="SetRevision"/>. Server-joined; ignored on post.</summary>
    public string SetName { get; set; } = "";

    public int Multiplier { get; set; } = 1;

    public string Notes { get; set; } = "";

    /// <summary>When the server stored the release. Server-computed.</summary>
    public DateTime ReleasedAt { get; set; }

    /// <summary>Who released the set, as reported by the client.</summary>
    public string ReleasedBy { get; set; } = "";

    /// <summary>The nests created by the release, in the order posted.</summary>
    public IReadOnlyList<SetReleaseNest> Nests { get; set; } = Array.Empty<SetReleaseNest>();
}
