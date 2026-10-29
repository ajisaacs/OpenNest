using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using Clipper2Lib;
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
        IReadOnlyList<ClampZone> clampZones = null, SheetOutline outline = null)
    {
        if (cost.HasValue && (!double.IsFinite(cost.Value) || cost.Value <= 0))
            throw new ArgumentOutOfRangeException(nameof(cost), $"Stock {id}: cost must be finite and positive, or null.");
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        if (quantity < 0)
            throw new ArgumentOutOfRangeException(nameof(quantity));
        if (outline != null && (outline.Size.Width != size.Width || outline.Size.Length != size.Length))
            throw new ArgumentException($"Stock {id}: the outline's bounds ({outline.Size}) must be the sheet size ({size}).", nameof(outline));
        Cost = cost;
        Id = id;
        Size = size;
        Outline = outline;
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
            ClampZones = new DetachedZones(zones);
            SheetClampZones = sheetZones;
        }
    }

    /// <summary>
    /// Keep-out rectangles in the placement frame (quadrant applied), translated exactly
    /// like <see cref="WorkArea"/>. Empty when the plate defines none; never null. Every
    /// read returns detached copies: zone bounds are mutable, and the stock must stay
    /// consistent with the zones its materialized plates use.
    /// </summary>
    public IReadOnlyList<ClampZone> ClampZones { get; }

    /// <summary>
    /// The same zones exactly as given, in plate coordinates (the frame of
    /// <see cref="Plate.ClampZones"/>), for plates built from this stock. Keeping the input
    /// avoids a lossy translate-and-back; <see cref="ClampZoneFrame.ToPlacement"/> of such a
    /// plate reproduces <see cref="ClampZones"/> bit for bit.
    /// </summary>
    internal IReadOnlyList<ClampZone> SheetClampZones { get; }

    /// <summary>
    /// The machine clamp rail <see cref="ClampZones"/> were resolved from, when the stock was
    /// built from a rail-following plate; null for explicit zones. Only rail-backed clamps may
    /// be taken off as a last resort (D8): the rail is what lets a committed sheet name the
    /// clamps to pull and carry their removal time, so explicit zones are never removed.
    /// </summary>
    internal ClampRail ClampRail { get; init; }

    /// <summary>
    /// The gap part material keeps from every clamp zone, as <see cref="Plate.ClampClearance"/>:
    /// the rail's own clearance, else the bottom edge spacing (D6). Never part spacing.
    /// </summary>
    [JsonIgnore]
    public double ClampClearance => System.Math.Max(0, ClampRail?.Clearance ?? EdgeSpacing.Bottom);

    /// <summary>Generic per-physical-sheet cost; null selects area scoring.</summary>
    public double? Cost { get; }
    public string Id { get; }

    /// <summary>The sheet's bounding box; the whole sheet when <see cref="Outline"/> is null.</summary>
    public Size Size { get; }

    /// <summary>
    /// The sheet's material when it is not a rectangle, in plate coordinates (the sheet's
    /// lower-left frame, as <see cref="SheetClampZones"/>); null for a rectangular sheet. Its
    /// bounds are <see cref="Size"/>. Only engines that fill an outline nest on such stock;
    /// the others place nothing on it. Parts keep <see cref="OutlineInset"/> from its edges
    /// and obstacles. Omitted from serialized results when null, so rectangular stock
    /// serializes exactly as before.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SheetOutline Outline { get; }

    /// <summary>
    /// The clearance a part keeps from an outlined sheet's edges and obstacles: the largest of
    /// the four edge spacings, so no side of the outline is closer than the rectangle's would be.
    /// </summary>
    [JsonIgnore]
    public double OutlineInset =>
        System.Math.Max(0, System.Math.Max(System.Math.Max(EdgeSpacing.Left, EdgeSpacing.Right),
            System.Math.Max(EdgeSpacing.Top, EdgeSpacing.Bottom)));

    /// <summary>The sheet's lower-left corner in the placement frame: the translation
    /// <see cref="WorkArea"/> and <see cref="ClampZones"/> were given.</summary>
    [JsonIgnore]
    public Vector PlacementOrigin => new(workArea.X - EdgeSpacing.Left, workArea.Y - EdgeSpacing.Bottom);

    /// <summary>
    /// Where a part may lie on an outlined sheet, in the placement frame: the outline less its
    /// obstacles, eroded by <see cref="OutlineInset"/>. Null for a rectangular sheet, whose
    /// <see cref="WorkArea"/> is the answer. A fresh copy on every call.
    /// </summary>
    public PathsD UsableOutline() => Outline?.Usable(OutlineInset, PlacementOrigin);

    /// <summary>
    /// Whether <paramref name="region"/> (outer paths positive, holes negative, in the placement
    /// frame) lies within the outlined sheet's usable area, allowing <paramref name="slack"/>
    /// outside it. True for a rectangular sheet: its box is checked by the callers.
    /// </summary>
    public bool OutlineHolds(PathsD region, double slack) =>
        Outline == null || Outline.Holds(region, OutlineInset, PlacementOrigin, slack);

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

    /// <summary>Full sheet area before edge spacing is removed: the material inside the
    /// <see cref="Outline"/> when there is one.</summary>
    public double Area => Outline?.Area ?? Size.Width * Size.Length;

    /// <summary>True when a width (X) by height (Y) envelope fits the work area within epsilon.</summary>
    public bool Fits(double width, double height, double epsilon = 1e-9) =>
        width <= workArea.Length + epsilon && height <= workArea.Width + epsilon;

    private sealed class DetachedZones : IReadOnlyList<ClampZone>
    {
        private readonly ClampZone[] zones;

        public DetachedZones(ClampZone[] zones) => this.zones = zones;

        public int Count => zones.Length;

        public ClampZone this[int index] => Copy(zones[index]);

        public IEnumerator<ClampZone> GetEnumerator()
        {
            foreach (var zone in zones)
                yield return Copy(zone);
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();

        // The ClampZone constructor copies the rectangle, keeping its exact bits.
        private static ClampZone Copy(ClampZone zone) => new(zone.Id, zone.Bounds);
    }
}
