using System;
using OpenNest.Geometry;

namespace OpenNest.Engine.Jobs;

/// <summary>Immutable stock settings. Size and spacing are copied value types, not caller-owned settings.</summary>
public sealed class NestPlateStock
{
    private readonly Box workArea;

    public NestPlateStock(
        string id,
        Size size,
        int? quantity = null,
        double partSpacing = 0,
        Spacing edgeSpacing = default,
        int quadrant = 1
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        if (quantity < 0)
            throw new ArgumentOutOfRangeException(nameof(quantity));
        Id = id;
        Size = size;
        Quantity = quantity;
        PartSpacing = partSpacing;
        EdgeSpacing = edgeSpacing;
        Quadrant = quadrant;
        var left = quadrant is 1 or 4 ? 0 : -size.Length;
        var bottom = quadrant is 1 or 2 ? 0 : -size.Width;
        workArea = new Box(
            left + edgeSpacing.Left,
            bottom + edgeSpacing.Bottom,
            size.Length - edgeSpacing.Left - edgeSpacing.Right,
            size.Width - edgeSpacing.Bottom - edgeSpacing.Top
        );
    }

    public string Id { get; }
    public Size Size { get; }

    /// <summary>Available physical sheets: null is unlimited, zero is legal but unavailable.</summary>
    public int? Quantity { get; }
    public double PartSpacing { get; }
    public Spacing EdgeSpacing { get; }
    public int Quadrant { get; }

    /// <summary>
    /// Usable region in the placement frame (quadrant applied, edge spacing removed).
    /// Box.Length is the X extent, Box.Width the Y extent. Returns a detached copy.
    /// </summary>
    public Box WorkArea => new(workArea.X, workArea.Y, workArea.Length, workArea.Width);

    /// <summary>Full sheet area before edge spacing is removed.</summary>
    public double Area => Size.Width * Size.Length;

    /// <summary>True when a width (X) by height (Y) envelope fits the work area within epsilon.</summary>
    public bool Fits(double width, double height, double epsilon = 1e-9) =>
        width <= workArea.Length + epsilon && height <= workArea.Width + epsilon;
}
