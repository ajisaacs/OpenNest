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
}
