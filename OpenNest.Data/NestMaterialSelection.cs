namespace OpenNest.Data;

/// <summary>
/// Chooses the nest material when the Nest Info dialog is accepted. The dialog
/// edits only the material name, so grade and density must come from the
/// material the nest already has.
/// </summary>
public static class NestMaterialSelection
{
    /// <summary>
    /// Returns a new material named <paramref name="selectedName"/>. When that
    /// name matches the current material's name by <see cref="SharedListNames.Key"/>,
    /// the current grade and density are kept; any other name gets a name-only
    /// material. The current material is never modified or returned.
    /// </summary>
    public static Material Apply(Material? current, string? selectedName)
    {
        var name = selectedName ?? "";

        if (current != null && SharedListNames.Key(current.Name) == SharedListNames.Key(name))
            return new Material(name, current.Grade, current.Density);

        return new Material(name);
    }
}
