using System;
using System.Collections.Generic;
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
        int quadrant = 1,
        IReadOnlyList<ClampZone> clampZones = null
    )
        : this(id, size, quantity, partSpacing, edgeSpacing, quadrant, null, clampZones) { }

    public NestPlateStock(string id, Size size, int? quantity, double partSpacing,
        Spacing edgeSpacing, int quadrant, double? cost,
        IReadOnlyList<ClampZone> clampZones = null)
    {
        if (cost.HasValue && (!double.IsFinite(cost.Value) || cost.Value <= 0))
            throw new ArgumentOutOfRangeException(nameof(cost), $"Stock {id}: cost must be finite and positive, or null.");
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        if (quantity < 0)
            throw new ArgumentOutOfRangeException(nameof(quantity));
        Cost = cost;
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
        // Zones arrive in plate coordinates and are carried in the placement frame,
        // translated exactly like WorkArea. Detached copies: stock is immutable input.
        if (clampZones == null || clampZones.Count == 0)
        {
            ClampZones = Array.Empty<ClampZone>();
            SheetClampZones = Array.Empty<ClampZone>();
        }
        else
        {
            var zones = new ClampZone[clampZones.Count];
            var sheetZones = new ClampZone[clampZones.Count];
            for (var i = 0; i < zones.Length; i++)
            {
                zones[i] = new ClampZone(clampZones[i].Id, clampZones[i].Bounds.Translate(left, bottom));
                sheetZones[i] = new ClampZone(clampZones[i].Id, clampZones[i].Bounds);
            }
            ClampZones = zones;
            SheetClampZones = sheetZones;
        }
    }

    /// <summary>
    /// Keep-out rectangles in the placement frame (quadrant applied), translated exactly
    /// like <see cref="WorkArea"/>. Empty when the plate defines none; never null.
    /// </summary>
    public IReadOnlyList<ClampZone> ClampZones { get; }

    /// <summary>
    /// The same zones exactly as given, in plate coordinates (the frame of
    /// <see cref="Plate.ClampZones"/>), for plates built from this stock. Keeping the input
    /// avoids a lossy translate-and-back; <see cref="ClampZoneFrame.ToPlacement"/> of such a
    /// plate reproduces <see cref="ClampZones"/> bit for bit.
    /// </summary>
    internal IReadOnlyList<ClampZone> SheetClampZones { get; }

    /// <summary>Generic per-physical-sheet cost; null selects area scoring.</summary>
    public double? Cost { get; }
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
