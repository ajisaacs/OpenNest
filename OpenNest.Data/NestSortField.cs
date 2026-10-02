namespace OpenNest.Data;

/// <summary>Columns a saved-nest browse query may order by (camelCase on the wire).</summary>
public enum NestSortField
{
    SavedAt,
    Name,
    Customer,
    Status,
    Material,
    DateCreated,
    DateModified,
    Thickness,
    PlateCount,
    PartCount,
    MadeBy,
    Comments,
    FileSize,
}
