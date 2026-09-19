using OpenNest.Geometry;

namespace OpenNest.Api;

/// <summary>One explicit physical-stock type for a whole nesting job.</summary>
public class NestRequestPlate
{
    public string Id { get; init; }
    public Size Size { get; init; }
    /// <summary>Available physical sheets; null means unlimited.</summary>
    public int? Quantity { get; init; }
    public double PartSpacing { get; init; }
    public Spacing EdgeSpacing { get; init; }
    public int Quadrant { get; init; } = 1;
}
