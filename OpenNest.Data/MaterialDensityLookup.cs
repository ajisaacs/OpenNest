namespace OpenNest.Data;

/// <summary>
/// Finds a density (pounds per cubic inch) for a nest material from the machine material
/// catalogs. Nest and drawing materials are usually picked by name and grade only, so they
/// carry no density of their own; the machines' material defaults are where densities live.
/// </summary>
public static class MaterialDensityLookup
{
    /// <summary>
    /// The first positive density, in order: the nest's machine for the exact name and grade,
    /// any machine for the exact name and grade, then the nest's machine and any machine for
    /// the name alone (grades of one material differ by about a percent). Zero when none.
    /// </summary>
    public static double Resolve(
        string? name,
        string? grade,
        MachineConfig? machine,
        IEnumerable<MaterialConfig>? catalog
    )
    {
        if (string.IsNullOrWhiteSpace(name))
            return 0;

        var machineMaterials = machine?.Materials ?? Enumerable.Empty<MaterialConfig>();
        var allMaterials = machineMaterials.Concat(catalog ?? Enumerable.Empty<MaterialConfig>()).ToList();
        var nameKey = SharedListNames.Key(name);
        var sameName = allMaterials
            .Where(m => m != null && SharedListNames.Key(m.Name) == nameKey && double.IsFinite(m.Density) && m.Density > 0)
            .ToList();

        if (!string.IsNullOrWhiteSpace(grade))
        {
            var gradeKey = SharedListNames.Key(grade);
            var exact = sameName.FirstOrDefault(m => SharedListNames.Key(m.Grade) == gradeKey);
            if (exact != null)
                return exact.Density;
        }

        return sameName.FirstOrDefault()?.Density ?? 0;
    }
}
