namespace OpenNest.Data;

/// <summary>
/// The drawing library reads and writes that saving a nest drawing to the library uses.
/// <see cref="RemoteDrawingLibrary"/> is the only production implementation.
/// </summary>
public interface IDrawingLibraryStore : IDrawingLibrary
{
    /// <summary>The current record, including an archived one, or null for an unknown id.</summary>
    Task<DrawingLibraryRecord?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>One page of immutable revisions, newest first, or null for an unknown id.</summary>
    Task<DrawingLibraryPage?> GetRevisionsAsync(Guid id, int offset = 0,
        int limit = DrawingLibraryRules.DefaultRevisionLimit, CancellationToken cancellationToken = default);

    /// <summary>Stores revision 1 under the caller-allocated <c>record.Id</c>.</summary>
    Task<DrawingLibraryRecord> UploadAsync(DrawingLibraryRecord record, byte[] package,
        CancellationToken cancellationToken = default);

    /// <summary>Saves the next revision when <paramref name="expectedRevision"/> is current.</summary>
    Task<DrawingLibraryRecord> UpdatePackageAsync(Guid id, DrawingLibraryRecord record, byte[] package,
        int expectedRevision, CancellationToken cancellationToken = default);
}
