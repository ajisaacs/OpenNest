namespace OpenNest.Data;

/// <summary>
/// A library drawing's editable details. Units are not among them: they give the stored
/// geometry its meaning, so changing them would rescale the part.
/// </summary>
public sealed record DrawingLibraryDetails(
    string Name,
    string Customer,
    string MaterialName,
    string MaterialGrade,
    double Thickness,
    CutType CutType);

/// <summary>
/// Edits one listed library drawing's details (name, customer, material, grade, thickness,
/// cut type), notes and nesting settings in place: the save is a metadata-only revision
/// guarded by the revision it was listed with, so the package (and its hash) stays the same
/// and nests keep the revision they hold. A stale revision raises
/// <see cref="DrawingLibraryConflictException"/> and changes nothing.
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

    /// <summary>The listed record's details, the starting point of an edit.</summary>
    public DrawingLibraryDetails Details => new(Listed.Name, Listed.Customer, Listed.MaterialName,
        Listed.MaterialGrade, Listed.Thickness, Listed.CutType);

    /// <summary>
    /// The record to save: the edited details (text trimmed, thickness rounded as stored),
    /// the listed units, and the new notes and settings.
    /// </summary>
    public DrawingLibraryRecord Build(DrawingLibraryDetails details, string notes, DrawingNestingSettings settings)
    {
        ArgumentNullException.ThrowIfNull(details);
        return new DrawingLibraryRecord
        {
            Id = Listed.Id,
            Name = (details.Name ?? "").Trim(),
            Customer = (details.Customer ?? "").Trim(),
            MaterialName = (details.MaterialName ?? "").Trim(),
            MaterialGrade = (details.MaterialGrade ?? "").Trim(),
            Thickness = double.IsFinite(details.Thickness)
                ? DrawingLibraryRules.NormalizeThickness(details.Thickness)
                : details.Thickness,
            Units = Listed.Units,
            CutType = details.CutType,
            Notes = notes ?? "",
            Settings = settings ?? throw new ArgumentNullException(nameof(settings)),
            SavedBy = SavedBy,
        };
    }

    /// <summary>The first rule the edit breaks, or null when it may be saved.</summary>
    public string? GetError(DrawingLibraryDetails details, string notes, DrawingNestingSettings settings) =>
        DrawingLibraryRules.GetMetadataError(Build(details, notes, settings));

    /// <summary>True when saving would change nothing.</summary>
    public bool IsUnchanged(DrawingLibraryDetails details, string notes, DrawingNestingSettings settings) =>
        SameDetails(Build(details, notes, settings), Listed)
        && (notes ?? "") == Listed.Notes && Equals(settings, Listed.Settings);

    /// <summary>
    /// True when the edit changes the details that identify a library drawing (D4: customer,
    /// name, material, grade, thickness and cut type, normalized as duplicates are matched).
    /// </summary>
    public bool ChangesIdentity(DrawingLibraryDetails details) =>
        !DrawingLibraryDuplicates.SameDuplicateKey(BuildDetails(details), Listed);

    /// <summary>
    /// The other active library drawings the edited details would duplicate (D4), newest
    /// first. Empty when the edit keeps this drawing's identity, so drawings that already
    /// shared it are not reported on every save.
    /// </summary>
    public async Task<IReadOnlyList<DrawingLibraryRecord>> FindCollisionsAsync(DrawingLibraryDetails details,
        CancellationToken cancellationToken = default)
    {
        var record = BuildDetails(details);
        if (!ChangesIdentity(details) || DrawingLibraryRules.GetMetadataError(record) != null)
            return Array.Empty<DrawingLibraryRecord>();
        var matches = await DrawingLibraryDuplicates.FindMatchesAsync(_library, record, cancellationToken);
        return matches.Where(m => m.Id != Listed.Id).ToList();
    }

    // The record with the edited details and the listed notes and settings, for identity checks.
    private DrawingLibraryRecord BuildDetails(DrawingLibraryDetails details) =>
        Build(details, Listed.Notes, Listed.Settings ?? new DrawingNestingSettings());

    /// <summary>Saves the edit as the next revision; returns the stored record.</summary>
    public async Task<DrawingLibraryRecord> SaveAsync(DrawingLibraryDetails details, string notes,
        DrawingNestingSettings settings, CancellationToken cancellationToken = default)
    {
        var record = Build(details, notes, settings);
        var error = DrawingLibraryRules.GetMetadataError(record);
        if (error != null)
            throw new ArgumentException(error, nameof(details));
        var saved = await _library.UpdateMetadataAsync(Listed.Id, record, Listed.Revision, cancellationToken);
        if (!string.Equals(saved.PackageSha256, Listed.PackageSha256, StringComparison.OrdinalIgnoreCase)
            || !SameDetails(saved, record) || saved.Units != record.Units
            || !Equals(saved.Settings, record.Settings) || saved.Notes != record.Notes)
            throw new IOException("The nest server stored a different library drawing than was edited. Reload the library.");
        return saved;
    }

    // Compares a built (trimmed, rounded) record with another, as stored.
    private static bool SameDetails(DrawingLibraryRecord built, DrawingLibraryRecord other) =>
        built.Name == other.Name.Trim() && built.Customer == other.Customer.Trim()
        && built.MaterialName == other.MaterialName.Trim() && built.MaterialGrade == other.MaterialGrade.Trim()
        && built.Thickness == DrawingLibraryRules.NormalizeThickness(other.Thickness)
        && built.CutType == other.CutType;
}
