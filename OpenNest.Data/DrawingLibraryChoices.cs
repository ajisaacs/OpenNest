namespace OpenNest.Data;

/// <summary>
/// The customers, materials, grades and thicknesses an edit offers in its drop-downs: those
/// already used by library drawings, plus the materials and grades of a material catalog.
/// Names match by <see cref="SharedListNames.Key"/> and keep the first spelling seen, so
/// pass the newest records first. Thicknesses come only from records in the edit's units.
/// The lists are suggestions; any typed value may still be saved.
/// </summary>
public sealed class DrawingLibraryChoices
{
    private readonly List<DrawingLibraryRecord> _records;
    private readonly List<MaterialConfig> _catalog;

    private DrawingLibraryChoices(List<DrawingLibraryRecord> records, List<MaterialConfig> catalog, Units units)
    {
        _records = records;
        Units = units;
        _catalog = catalog;
        Customers = Distinct(records.Select(r => r.Customer));
        Materials = Distinct(records.Select(r => r.MaterialName).Concat(catalog.Select(m => m.Name)));
    }

    public static DrawingLibraryChoices Empty { get; } = new(new(), new(), Units.Inches);

    /// <param name="records">Library drawings, newest first; archived ones are skipped.</param>
    /// <param name="units">The edit's units; thicknesses in other units are not offered.</param>
    /// <param name="catalog">Materials and grades on file, for example the machine configurations'.</param>
    public static DrawingLibraryChoices From(IEnumerable<DrawingLibraryRecord> records, Units units,
        IEnumerable<MaterialConfig>? catalog = null)
    {
        ArgumentNullException.ThrowIfNull(records);
        return new DrawingLibraryChoices(records.Where(r => r != null && !r.Archived).ToList(),
            catalog?.Where(m => m != null).ToList() ?? new(), units);
    }

    public Units Units { get; }

    /// <summary>Customers, sorted.</summary>
    public IReadOnlyList<string> Customers { get; }

    /// <summary>Material names, sorted.</summary>
    public IReadOnlyList<string> Materials { get; }

    /// <summary>The grades used or on file for <paramref name="material"/>, sorted.</summary>
    public IReadOnlyList<string> GradesOf(string? material)
    {
        var key = SharedListNames.Key(material);
        if (key.Length == 0)
            return Array.Empty<string>();
        return Distinct(_records.Where(r => SharedListNames.Key(r.MaterialName) == key).Select(r => r.MaterialGrade)
            .Concat(_catalog.Where(m => SharedListNames.Key(m.Name) == key).Select(m => m.Grade)));
    }

    /// <summary>
    /// The thicknesses library drawings of <paramref name="material"/> (and of
    /// <paramref name="grade"/>, when one is given) use, thinnest first.
    /// </summary>
    public IReadOnlyList<double> ThicknessesOf(string? material, string? grade)
    {
        var materialKey = SharedListNames.Key(material);
        if (materialKey.Length == 0)
            return Array.Empty<double>();
        var gradeKey = SharedListNames.Key(grade);
        return _records
            .Where(r => r.Units == Units && SharedListNames.Key(r.MaterialName) == materialKey
                && (gradeKey.Length == 0 || SharedListNames.Key(r.MaterialGrade) == gradeKey)
                && double.IsFinite(r.Thickness) && r.Thickness > 0)
            .Select(r => DrawingLibraryRules.NormalizeThickness(r.Thickness))
            .Distinct()
            .OrderBy(t => t)
            .ToList();
    }

    private static IReadOnlyList<string> Distinct(IEnumerable<string?> names) =>
        names
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => MaterialListRules.Spelling(name))
            .GroupBy(SharedListNames.Key)
            .Select(group => group.First())
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();
}
