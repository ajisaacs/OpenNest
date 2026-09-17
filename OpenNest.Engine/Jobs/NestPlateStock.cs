using System;
using OpenNest.Geometry;

namespace OpenNest;

/// <summary>Immutable stock settings. Size and spacing are copied value types, not caller-owned settings.</summary>
public sealed class NestPlateStock
{
    public NestPlateStock(string id, Size size, int? quantity = null, double partSpacing = 0,
        Spacing edgeSpacing = default, int quadrant = 1)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        if (quantity < 0) throw new ArgumentOutOfRangeException(nameof(quantity));
        Id = id;
        Size = size;
        Quantity = quantity;
        PartSpacing = partSpacing;
        EdgeSpacing = edgeSpacing;
        Quadrant = quadrant;
    }

    public string Id { get; }
    public Size Size { get; }
    /// <summary>Available physical sheets: null is unlimited, zero is legal but unavailable.</summary>
    public int? Quantity { get; }
    public double PartSpacing { get; }
    public Spacing EdgeSpacing { get; }
    public int Quadrant { get; }
}
