namespace OpenNest.Data;

/// <summary>An undo request: who is undoing the release, and which changed nests to leave as they are.</summary>
public sealed class SetReleaseUndoRequest
{
    public string UndoneBy { get; set; } = "";

    /// <summary>
    /// Nests of the release to leave alone. Every other nest must still be at the revision
    /// the release wrote, or nothing is undone.
    /// </summary>
    public IReadOnlyList<Guid> SkipNests { get; set; } = Array.Empty<Guid>();
}

public enum SetReleaseUndoRefusal
{
    /// <summary>The release was undone before.</summary>
    AlreadyUndone,

    /// <summary>Nests not skipped have changed since the release; <see cref="SetReleaseUndoConflict.ChangedNests"/> lists them.</summary>
    NestsChanged,
}

/// <summary>Why an undo was refused. Nothing was changed.</summary>
public sealed class SetReleaseUndoConflict
{
    public SetReleaseUndoRefusal Reason { get; set; }

    /// <summary>The release as it is now.</summary>
    public SetReleaseRecord Release { get; set; } = new();

    /// <summary>The current metadata of each nest that moved on since the release, deleted or not.</summary>
    public IReadOnlyList<NestRecord> ChangedNests { get; set; } = Array.Empty<NestRecord>();
}
