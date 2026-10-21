namespace OpenNest.Data;

/// <summary>
/// Drawing sets and their releases on the nest server (<c>/api/sets</c>). Kept apart from
/// <see cref="IDrawingLibrary"/> so browsing and inserting drawings do not depend on sets.
/// <see cref="RemoteDrawingSetStore"/> is the only production implementation.
/// </summary>
public interface IDrawingSetStore
{
    /// <summary>One bounded page of sets matching the query, without their members.</summary>
    Task<DrawingSetPage> QueryAsync(DrawingSetQuery query, CancellationToken cancellationToken = default);

    /// <summary>
    /// The current record, including an archived one, with every member joined to its
    /// drawing's current library record; null for an unknown id.
    /// </summary>
    Task<DrawingSetRecord?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>One page of immutable revisions without members, newest first, or null for an unknown id.</summary>
    Task<DrawingSetPage?> GetRevisionsAsync(Guid id, int offset = 0,
        int limit = DrawingLibraryRules.DefaultRevisionLimit, CancellationToken cancellationToken = default);

    /// <summary>One revision with its members, or null when the set or revision is unknown.</summary>
    Task<DrawingSetRecord?> GetRevisionAsync(Guid id, int revision, CancellationToken cancellationToken = default);

    /// <summary>Sets whose current revision contains the drawing, by name, or null for an unknown drawing.</summary>
    Task<DrawingSetPage?> GetSetsContainingAsync(Guid drawingId, int offset = 0,
        int limit = DrawingLibraryRules.DefaultRevisionLimit, CancellationToken cancellationToken = default);

    /// <summary>Stores revision 1 under the caller-allocated <c>record.Id</c>.</summary>
    Task<DrawingSetRecord> CreateAsync(DrawingSetRecord record, CancellationToken cancellationToken = default);

    /// <summary>Saves the next revision when <paramref name="expectedRevision"/> is current.</summary>
    Task<DrawingSetRecord> UpdateAsync(Guid id, DrawingSetRecord record, int expectedRevision,
        CancellationToken cancellationToken = default);

    Task<DrawingSetRecord> ArchiveAsync(Guid id, int expectedRevision, CancellationToken cancellationToken = default);

    Task<DrawingSetRecord> RestoreAsync(Guid id, int expectedRevision, CancellationToken cancellationToken = default);

    /// <summary>Records a release whose nests already exist, under the caller-allocated <c>release.Id</c>.</summary>
    Task<SetReleaseRecord> PostReleaseAsync(SetReleaseRecord release, CancellationToken cancellationToken = default);

    /// <summary>One release with its sets and nests, or null for an unknown id.</summary>
    Task<SetReleaseRecord?> GetReleaseAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>One page of releases with their sets and nests, newest first, optionally only those including a set or a nest.</summary>
    Task<SetReleasePage> QueryReleasesAsync(Guid? setId = null, Guid? nestId = null, int offset = 0,
        int limit = DrawingLibraryRules.DefaultRevisionLimit, CancellationToken cancellationToken = default);

    /// <summary>Undoes a release, all or nothing; a refusal raises <see cref="SetReleaseUndoConflictException"/>.</summary>
    Task<SetReleaseRecord> UndoReleaseAsync(Guid id, SetReleaseUndoRequest request, CancellationToken cancellationToken = default);
}
