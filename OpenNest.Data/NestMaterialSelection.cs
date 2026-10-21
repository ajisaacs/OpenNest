namespace OpenNest.Data;

/// <summary>
/// Chooses the nest material from a material name and grade, as edited in the
/// Nest Info dialog or recorded on the nest server. Density is not edited, so it
/// must come from the material the nest already has.
/// </summary>
public static class NestMaterialSelection
{
    /// <summary>
    /// Returns a new material named <paramref name="selectedName"/> with grade
    /// <paramref name="selectedGrade"/>. When both match the current material by
    /// <see cref="SharedListNames.Key"/>, the current density is kept; any other
    /// name or grade gets no density. The current material is never modified or returned.
    /// </summary>
    public static Material Apply(Material? current, string? selectedName, string? selectedGrade)
    {
        var name = selectedName ?? "";
        var grade = selectedGrade ?? "";

        if (
            current != null
            && SharedListNames.Key(current.Name) == SharedListNames.Key(name)
            && SharedListNames.Key(current.Grade) == SharedListNames.Key(grade)
        )
            return new Material(name, grade, current.Density);

        return new Material(name, grade);
    }

    /// <summary>
    /// Applies server metadata to an archived material. A blank recorded grade means
    /// the record predates grades, so the archive's grade stands while the name is
    /// unchanged; every edit rewrites the archive too, so a cleared grade is blank there as well.
    /// </summary>
    public static Material ApplyRecorded(Material? current, string? recordedName, string? recordedGrade)
    {
        var grade = recordedGrade ?? "";
        if (
            grade.Length == 0
            && current != null
            && SharedListNames.Key(current.Name) == SharedListNames.Key(recordedName)
        )
            grade = current.Grade ?? "";

        return Apply(current, recordedName, grade);
    }
}
