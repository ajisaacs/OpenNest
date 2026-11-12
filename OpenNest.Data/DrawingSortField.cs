namespace OpenNest.Data;

/// <summary>Columns a drawing library query may order by (camelCase on the wire).</summary>
public enum DrawingSortField
{
    SavedAt,
    Name,
    Customer,
    MaterialName,
    MaterialGrade,
    Thickness,
    CutType,
    Revision,

    /// <summary>The measured longer side; unmeasured drawings sort as smallest.</summary>
    Length,

    /// <summary>The measured hole count; unmeasured drawings sort as fewest.</summary>
    Holes,
}
