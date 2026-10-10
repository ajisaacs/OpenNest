using OpenNest.Geometry;

namespace OpenNest.Api;

/// <summary>One explicit physical-stock type for a whole nesting job.</summary>
public class NestRequestPlate
{
    /// <summary>Positive per-sheet cost in common units; omit on every row for area scoring.</summary>
    public double? Cost { get; init; }

    public string Id { get; init; }
    public Size Size { get; init; }

    /// <summary>Available physical sheets; null means unlimited.</summary>
    public int? Quantity { get; init; }
    public double PartSpacing { get; init; }
    public Spacing EdgeSpacing { get; init; }
    public int Quadrant { get; init; } = 1;
}
