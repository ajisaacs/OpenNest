namespace OpenNest.Data;

/// <summary>
/// Edits one listed library drawing's notes and nesting settings in place: the save is a
/// metadata-only revision guarded by the revision it was listed with, so the package (and
/// its hash) stays the same. A stale revision raises <see cref="DrawingLibraryConflictException"/>
/// and changes nothing.
/// </summary>
public sealed class DrawingLibraryEditSession
{
    private readonly IDrawingLibraryStore _library;

    public DrawingLibraryEditSession(IDrawingLibraryStore library, DrawingLibraryRecord listed, string savedBy)
    {
        _library = library ?? throw new ArgumentNullException(nameof(library));
        Listed = listed ?? throw new ArgumentNullException(nameof(listed));
        if (listed.Id == Guid.Empty || listed.Revision < 1)
            throw new ArgumentException("Choose a listed library drawing to edit.", nameof(listed));
        if (listed.Archived)
            throw new ArgumentException("An archived library drawing cannot be edited. Restore it first.", nameof(listed));
        SavedBy = savedBy ?? "";
    }

    /// <summary>The record as it was listed; its revision guards the save.</summary>
    public DrawingLibraryRecord Listed { get; }

    public string SavedBy { get; }

    /// <summary>The record to save: the listed details with new notes and settings.</summary>
    public DrawingLibraryRecord Build(string notes, DrawingNestingSettings settings) => new()
    {
        Id = Listed.Id,
        Name = Listed.Name,
        Customer = Listed.Customer,
        MaterialName = Listed.MaterialName,
        MaterialGrade = Listed.MaterialGrade,
        Thickness = Listed.Thickness,
        Units = Listed.Units,
        CutType = Listed.CutType,
        Notes = notes ?? "",
        Settings = settings ?? throw new ArgumentNullException(nameof(settings)),
        SavedBy = SavedBy,
    };

    /// <summary>The first rule the edit breaks, or null when it may be saved.</summary>
    public string? GetError(string notes, DrawingNestingSettings settings) =>
        DrawingLibraryRules.GetMetadataError(Build(notes, settings));

    /// <summary>True when saving would change nothing.</summary>
    public bool IsUnchanged(string notes, DrawingNestingSettings settings) =>
        (notes ?? "") == Listed.Notes && Equals(settings, Listed.Settings);

    /// <summary>Saves the edit as the next revision; returns the stored record.</summary>
    public async Task<DrawingLibraryRecord> SaveAsync(string notes, DrawingNestingSettings settings,
        CancellationToken cancellationToken = default)
    {
        var record = Build(notes, settings);
        var error = DrawingLibraryRules.GetMetadataError(record);
        if (error != null)
            throw new ArgumentException(error, nameof(settings));
        var saved = await _library.UpdateMetadataAsync(Listed.Id, record, Listed.Revision, cancellationToken);
        if (!string.Equals(saved.PackageSha256, Listed.PackageSha256, StringComparison.OrdinalIgnoreCase)
            || !Equals(saved.Settings, record.Settings) || saved.Notes != record.Notes)
            throw new IOException("The nest server stored a different library drawing than was edited. Reload the library.");
        return saved;
    }
}
