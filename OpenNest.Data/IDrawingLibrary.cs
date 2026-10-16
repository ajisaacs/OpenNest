namespace OpenNest.Data;

/// <summary>
/// The read side of the server drawing library that browsing and adding to a nest use.
/// <see cref="RemoteDrawingLibrary"/> is the only production implementation.
/// </summary>
public interface IDrawingLibrary
{
    /// <summary>One bounded page of drawings matching the query, filtered and ordered by the server.</summary>
    Task<DrawingLibraryPage> QueryAsync(DrawingLibraryQuery query, CancellationToken cancellationToken = default);

    /// <summary>The exact package of one immutable revision, or null when the drawing or revision is unknown.</summary>
    Task<byte[]?> GetRevisionPackageAsync(Guid id, int revision, CancellationToken cancellationToken = default);
}
