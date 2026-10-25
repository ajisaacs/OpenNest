using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using OpenNest.Collections;
using OpenNest.Geometry;
using OpenNest.Math;
using OpenNest.Shapes;
using OpenNest.Support;

namespace OpenNest
{
    public class Plate
    {
        private int quadrant;

        public event EventHandler<ItemAddedEventArgs<Part>> PartAdded
        {
            add { Parts.ItemAdded += value; }
            remove { Parts.ItemAdded -= value; }
        }

        public event EventHandler<ItemRemovedEventArgs<Part>> PartRemoved
        {
            add { Parts.ItemRemoved += value; }
            remove { Parts.ItemRemoved -= value; }
        }

        public event EventHandler<ItemChangedEventArgs<Part>> PartChanged
        {
            add { Parts.ItemChanged += value; }
            remove { Parts.ItemChanged -= value; }
        }

        /// <summary>
        /// Raised once after the part order changes without adding or removing parts, including
        /// a committed cutting plan that also installed planned programs. Refresh order- and
        /// program-derived views; quantity accounting is unchanged.
        /// </summary>
        public event EventHandler PartsReordered
        {
            add { Parts.ItemsReordered += value; }
            remove { Parts.ItemsReordered -= value; }
        }

        public Plate()
            : this(60, 120) { }

        public Plate(double width, double length)
            : this(new Size(width, length)) { }

        public Plate(Size size)
        {
            EdgeSpacing = new Spacing();
            Size = size;
            Parts = new ObservableList<Part>();
            Parts.ItemAdded += Parts_PartAdded;
            Parts.ItemRemoved += Parts_PartRemoved;
            CutOffs = new ObservableList<CutOff>();
            ClampZones = new ObservableList<ClampZone>();
            ClampZones.ItemAdded += (_, _) => OnClampZonesChanged();
            ClampZones.ItemRemoved += (_, _) => OnClampZonesChanged();
            ClampZones.ItemChanged += (_, _) => OnClampZonesChanged();
            Quadrant = 1;
        }

        private void Parts_PartAdded(object sender, ItemAddedEventArgs<Part> e)
        {
            if (!e.Item.BaseDrawing.IsCutOff)
                e.Item.BaseDrawing.Quantity.Nested += Quantity;
        }

        private void Parts_PartRemoved(object sender, ItemRemovedEventArgs<Part> e)
        {
            if (!e.Item.BaseDrawing.IsCutOff)
                e.Item.BaseDrawing.Quantity.Nested -= Quantity;
        }

        /// <summary>
        /// The spacing between parts.
        /// </summary>
        public double PartSpacing { get; set; }

        /// <summary>
        /// The spacing along the edges of the plate.
        /// </summary>
        public Spacing EdgeSpacing;

        /// <summary>
        /// The size of the plate. A plate following a <see cref="ClampRail"/> re-resolves
        /// its clamp zones for the new size.
        /// </summary>
        public Size Size
        {
            get { return size; }
            set
            {
                // A turned sheet is the same sheet; any other size is no longer the coded item.
                if (stockItemCodes.Count > 0 && !SameSheet(size, value))
                    stockItemCodes = Array.Empty<string>();
                size = value;
                ResolveClampRail();
            }
        }

        private Size size;

        private static bool SameSheet(Size a, Size b) =>
            a.Width == b.Width && a.Length == b.Length || a.Width == b.Length && a.Length == b.Width;

        /// <summary>
        /// The inventory item each physical sheet of this plate is cut from: entry i names sheet
        /// i of <see cref="Quantity"/>, and sheets past the end are uncoded. Empty when no sheet
        /// is coded. A resize (other than turning the sheet) clears it, lowering
        /// <see cref="Quantity"/> truncates it, and raising it leaves the extra sheets uncoded.
        /// Display and hand-off data only: quantity accounting never reads it.
        /// </summary>
        public IReadOnlyList<string> StockItemCodes
        {
            get { return stockItemCodes; }
            set
            {
                ArgumentNullException.ThrowIfNull(value);
                if (value.Any(string.IsNullOrWhiteSpace))
                    throw new ArgumentException("Stock item codes must not be blank.", nameof(value));
                stockItemCodes = value.Take(System.Math.Max(quantity, 0)).ToArray();
            }
        }

        private IReadOnlyList<string> stockItemCodes = Array.Empty<string>();

        public CNC.CuttingStrategy.CuttingParameters CuttingParameters { get; set; }

        /// <summary>
        /// Material grain direction in radians. 0 = horizontal.
        /// </summary>
        public double GrainAngle { get; set; }

        /// <summary>
        /// The parts that the plate contains.
        /// </summary>
        public ObservableList<Part> Parts { get; set; }

        /// <summary>
        /// The cut-off lines defined on this plate.
        /// </summary>
        public ObservableList<CutOff> CutOffs { get; set; }

        /// <summary>
        /// Fixed rectangular machine-clamp keep-outs in plate coordinates
        /// (<see cref="ClampZone"/>). Never parts: excluded from quantity, utilization,
        /// cut sequence and CNC output; placement rules and display consume them. Mutate
        /// through this list; <see cref="ClampZonesChanged"/> fires for every change.
        /// </summary>
        public ObservableList<ClampZone> ClampZones { get; }

        /// <summary>
        /// Raised after <see cref="ClampZones"/> content changes so views can refresh
        /// the zone overlay. Carries no geometry arguments.
        /// </summary>
        public event EventHandler ClampZonesChanged;

        private void OnClampZonesChanged() => ClampZonesChanged?.Invoke(this, EventArgs.Empty);

        /// <summary>
        /// The machine clamp rail this plate follows, or null when its
        /// <see cref="ClampZones"/> are explicit (engine sheets, files without a rail). While
        /// set, the zones are derived: re-resolved whenever <see cref="Size"/> or
        /// <see cref="Quadrant"/> changes, because the clamps are fixed to the machine.
        /// </summary>
        public ClampRail ClampRail { get; private set; }

        /// <summary>
        /// The machine support bed this sheet rests on (its nest's, see
        /// <see cref="Nest.SetSupportBed"/>), or null for none. Display, analysis and support
        /// tabs only: never material, spacing or a keep-out.
        /// </summary>
        public SupportBed SupportBed { get; set; }

        private static readonly IReadOnlySet<string> NoRemovedClamps = ImmutableHashSet<string>.Empty;

        /// <summary>
        /// Rail clamp ids taken off this sheet, by Auto Nest as a last resort (D8) so a part
        /// that could not be nested anywhere with every clamp in place still fits, or by hand
        /// (<see cref="SetClampRemoved"/>). Empty unless
        /// <see cref="ClampRail"/> is set; <see cref="ClampZones"/> already excludes them, so
        /// nothing else needs to consult this list to avoid them. Ids stay listed across a
        /// resize/rotate even when the matching clamp is temporarily beyond the sheet, so it
        /// re-appears removed if a later resize brings it back onto the sheet (C13).
        /// </summary>
        public IReadOnlySet<string> RemovedClamps { get; private set; } = NoRemovedClamps;

        /// <summary>
        /// Minutes this sheet's cut-time estimate gains from <see cref="RemovedClamps"/> (D9):
        /// each listed clamp the rail places on this sheet adds
        /// <see cref="OpenNest.ClampRail.RemovalMinutes"/> once; a listed clamp that is beyond
        /// the sheet (or unknown to the rail) is not pulled and costs nothing. Zero when there
        /// is no rail (removal is meaningless without one to remove clamps from).
        /// </summary>
        public double ClampRemovalMinutes =>
            ClampRail == null || RemovedClamps.Count == 0
                ? 0
                : ClampRail.ResolveFor(Size, Quadrant).Count(zone => RemovedClamps.Contains(zone.Id))
                    * ClampRail.RemovalMinutes;

        /// <summary>
        /// Follows <paramref name="rail"/> and replaces the zones with its resolution for
        /// this sheet; null detaches and removes every zone. Unchanged zones raise no events.
        /// </summary>
        public void SetClampRail(ClampRail rail) => SetClampRail(rail, null);

        /// <summary>
        /// Follows <paramref name="rail"/> with the clamps named in <paramref name="removedClamps"/>
        /// taken off this sheet (D8): zones become the rail's resolution for this sheet minus
        /// those ids. Null <paramref name="rail"/> detaches, clears every zone and forgets the
        /// removed set (removal only means something while following a rail).
        /// </summary>
        public void SetClampRail(ClampRail rail, IEnumerable<string> removedClamps)
        {
            ClampRail = rail;
            RemovedClamps = rail == null || removedClamps == null
                ? NoRemovedClamps
                : removedClamps.ToImmutableHashSet(StringComparer.Ordinal);
            ReplaceClampZones(ResolveRailZones());
        }

        /// <summary>
        /// The rail clamp this sheet places at <paramref name="point"/> (the parts' placement
        /// frame, see <see cref="ClampZoneFrame"/>), whether in place or removed, grown by
        /// <paramref name="tolerance"/> on every side; null without a rail or a clamp there.
        /// Returned bounds are in the placement frame.
        /// </summary>
        public ClampZone RailClampAt(Vector point, double tolerance = 0)
        {
            if (ClampRail == null)
                return null;
            var left = Quadrant is 2 or 3 ? -Size.Length : 0;
            var bottom = Quadrant is 3 or 4 ? -Size.Width : 0;
            foreach (var zone in ClampRail.ResolveFor(Size, Quadrant))
            {
                var box = zone.Bounds.Translate(left, bottom);
                if (box.Offset(tolerance).Contains(point))
                    return new ClampZone(zone.Id, box);
            }
            return null;
        }

        /// <summary>
        /// Takes the rail clamp <paramref name="id"/> off this sheet, or puts it back, as a
        /// manual counterpart to Auto Nest's last-resort removal (D8): the zones and
        /// <see cref="ClampRemovalMinutes"/> follow. False when nothing changed: no rail, a clamp
        /// the rail does not place on this sheet, or one already in the requested state.
        /// </summary>
        public bool SetClampRemoved(string id, bool removed)
        {
            if (ClampRail == null || id == null || RemovedClamps.Contains(id) == removed)
                return false;
            if (removed && !ClampRail.ResolveFor(Size, Quadrant).Any(zone => zone.Id == id))
                return false;
            var ids = removed ? RemovedClamps.Append(id) : RemovedClamps.Where(x => x != id);
            SetClampRail(ClampRail, ids);
            return true;
        }

        private void ResolveClampRail()
        {
            // Called from the constructor's Size assignment before the zone list exists.
            if (ClampRail != null)
                ReplaceClampZones(ResolveRailZones());
        }

        private IReadOnlyList<ClampZone> ResolveRailZones()
        {
            if (ClampRail == null)
                return Array.Empty<ClampZone>();
            var resolved = ClampRail.ResolveFor(Size, Quadrant);
            if (RemovedClamps.Count == 0)
                return resolved;
            var kept = new List<ClampZone>(resolved.Count);
            foreach (var zone in resolved)
                if (!RemovedClamps.Contains(zone.Id))
                    kept.Add(zone);
            return kept;
        }

        private void ReplaceClampZones(IReadOnlyList<ClampZone> zones)
        {
            if (ClampRail.SameZones(ClampZones, zones))
                return;
            ClampZones.Clear();
            foreach (var zone in zones)
                ClampZones.Add(zone);
        }

        /// <summary>
        /// Regenerates all cut-off drawings and materializes each of their trimmed segments as
        /// a part. Existing cut-off parts are removed first, then each regenerated segment goes
        /// back to the place in the cut sequence (<see cref="Parts"/> order) of the saved segment
        /// of its cut-off it overlaps most, so a part drag or cut-off move doesn't send it to the
        /// end of the sequence; a segment with no saved counterpart follows the segment before
        /// it. New cut-offs are added at the end.
        /// </summary>
        public void RegenerateCutOffs(CutOffSettings settings)
        {
            var saved = new Dictionary<CutOff, List<(int Index, double Start, double End)>>();
            for (var i = Parts.Count - 1; i >= 0; i--)
            {
                if (!Parts[i].BaseDrawing.IsCutOff)
                    continue;

                var cutoff = CutOffs.FirstOrDefault(c => ReferenceEquals(c.Drawing, Parts[i].BaseDrawing));
                if (cutoff != null)
                {
                    if (!saved.TryGetValue(cutoff, out var segments))
                        saved[cutoff] = segments = new List<(int, double, double)>();
                    var extent = CutOffSegments.Extent(Parts[i], cutoff.Axis == CutOffAxis.Vertical);
                    segments.Add((i, extent?.Start ?? double.NaN, extent?.End ?? double.NaN));
                }

                Parts.RemoveAt(i);
            }

            RegenerateCutOffs(settings, (cutoff, extents) =>
            {
                if (!saved.TryGetValue(cutoff, out var segments))
                    return null;
                var indices = new int[extents.Count];
                var previous = segments.Min(s => s.Index);
                for (var k = 0; k < extents.Count; k++)
                {
                    var (start, end) = extents[k];
                    var best = -1;
                    var bestOverlap = 0.0;
                    foreach (var (index, savedStart, savedEnd) in segments)
                    {
                        var overlap = System.Math.Min(end, savedEnd) - System.Math.Max(start, savedStart);
                        if (overlap > bestOverlap)
                        {
                            best = index;
                            bestOverlap = overlap;
                        }
                    }
                    indices[k] = previous = best >= 0 ? best : previous;
                }
                return indices;
            });
        }

        /// <summary>
        /// Regenerates all cut-off drawings and materializes each of their trimmed segments as a
        /// part, placing a cut-off's segments consecutively from its index in
        /// <paramref name="sequence"/> (its place in <see cref="Parts"/> order). Cut-offs missing
        /// from it are added at the end. Callers must remove existing cut-off parts first.
        /// </summary>
        public void RegenerateCutOffs(CutOffSettings settings, IReadOnlyDictionary<CutOff, int> sequence) =>
            RegenerateCutOffs(settings, (cutoff, extents) =>
                sequence != null && sequence.TryGetValue(cutoff, out var index)
                    ? Enumerable.Range(index, extents.Count).ToArray() : null);

        /// <summary>
        /// As <see cref="RegenerateCutOffs(CutOffSettings, IReadOnlyDictionary{CutOff, int})"/>,
        /// but each cut-off lists one index per segment in ascending order along its axis. A list
        /// whose length differs from the regenerated segment count places the segments
        /// consecutively from its smallest index.
        /// </summary>
        public void RegenerateCutOffs(CutOffSettings settings, IReadOnlyDictionary<CutOff, IReadOnlyList<int>> sequence) =>
            RegenerateCutOffs(settings, (cutoff, extents) =>
            {
                if (sequence == null || !sequence.TryGetValue(cutoff, out var indices) || indices == null || indices.Count == 0)
                    return null;
                if (indices.Count != extents.Count)
                    return Enumerable.Range(indices.Min(), extents.Count).ToArray();
                // Program order can run toward the origin; the file lists segments ascending.
                var ascending = Enumerable.Range(0, extents.Count).OrderBy(k => extents[k].Start).ToArray();
                var result = new int[extents.Count];
                for (var rank = 0; rank < ascending.Length; rank++)
                    result[ascending[rank]] = indices[rank];
                return result;
            });

        // sequence gives a cut-off's segment indices in program order from its segments' extents
        // along its axis (NaN when unmeasurable), or null to append them.
        private void RegenerateCutOffs(CutOffSettings settings,
            Func<CutOff, IReadOnlyList<(double Start, double End)>, IReadOnlyList<int>> sequence)
        {
            var cache = BuildPerimeterCache(this);
            var placed = new List<(int Index, int Order, Part Part)>();

            // Regenerate and materialize each cut-off's segments
            foreach (var cutoff in CutOffs)
            {
                cutoff.Regenerate(this, settings, cache);

                var programs = cutoff.SegmentPrograms();
                if (programs.Count == 0)
                    continue;

                var vertical = cutoff.Axis == CutOffAxis.Vertical;
                var parts = programs.Select(program => new Part(cutoff.Drawing, program)).ToArray();
                var extents = parts.Select(part => CutOffSegments.Extent(part, vertical) ?? (double.NaN, double.NaN)).ToArray();
                var indices = sequence?.Invoke(cutoff, extents);
                for (var k = 0; k < parts.Length; k++)
                {
                    if (indices != null && k < indices.Count)
                        placed.Add((indices[k], placed.Count, parts[k]));
                    else
                        Parts.Add(parts[k]);
                }
            }

            // Lowest index first: each insert then lands on its saved index, because every part
            // sequenced before it is already in place. Segments sharing an index keep their
            // program order (a later insert at the same index lands before an earlier one).
            foreach (var (index, _, part) in placed.OrderBy(p => p.Index).ThenByDescending(p => p.Order))
                Parts.Insert(System.Math.Clamp(index, 0, Parts.Count), part);
        }

        /// <summary>
        /// Builds a dictionary mapping each non-cut-off part to its perimeter entity.
        /// Closed shapes use ShapeProfile; open contours fall back to ConvexHull.
        /// </summary>
        public static Dictionary<Part, Geometry.Entity> BuildPerimeterCache(Plate plate)
        {
            var cache = new Dictionary<Part, Geometry.Entity>();

            foreach (var part in plate.Parts)
            {
                if (part.BaseDrawing.IsCutOff)
                    continue;

                cache[part] = BuildPartPerimeter(part);
            }

            return cache;
        }

        /// <summary>
        /// Converts one part's own program into its placed perimeter entity (closed
        /// profile, or a convex hull fallback for a genuinely open contour). This is
        /// the same conversion <see cref="BuildPerimeterCache"/> runs per part; callers
        /// without a pre-built cache (e.g. a direct, uncached <see cref="CutOff.Regenerate"/>
        /// call) use it to get the same conservative, converted-material envelope instead
        /// of trusting the part's cached bounding box alone.
        /// </summary>
        public static Geometry.Entity BuildPartPerimeter(Part part)
        {
            try
            {
                var entities = Converters
                    .ConvertProgram.ToGeometry(part.Program)
                    .Where(e => SpecialLayers.IsMaterial(e.Layer))
                    .ToList();

                if (entities.Count == 0)
                    return null;

                // Leads are cutting paths in scrap, not boundary edges. Chaining them
                // into the outline can make a closed part appear open and replace its
                // recesses with a convex hull. Keep the full geometry for the existing
                // conservative fallback when the material contour really is open.
                var outline = entities.Where(e => e.Layer != SpecialLayers.Leadin &&
                    e.Layer != SpecialLayers.Leadout).ToList();
                var profile = new Geometry.ShapeProfile(outline);

                if (profile.Perimeter.IsClosed())
                {
                    var perimeter = profile.Perimeter;
                    perimeter.Offset(part.Location);
                    return perimeter;
                }

                var points = entities.CollectPoints();
                if (points.Count < 3)
                    return null;

                var hull = Geometry.ConvexHull.Compute(points);
                hull.Offset(part.Location);
                return hull;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// The number of times to cut the plate. Lowering it drops the
        /// <see cref="StockItemCodes"/> of the sheets no longer cut.
        /// </summary>
        public int Quantity
        {
            get { return quantity; }
            set
            {
                quantity = value;
                if (stockItemCodes.Count > System.Math.Max(value, 0))
                    stockItemCodes = stockItemCodes.Take(System.Math.Max(value, 0)).ToArray();
            }
        }

        private int quantity;

        /// <summary>
        /// The quadrant the plate is located in.
        /// 1 = TopRight
        /// 2 = TopLeft
        /// 3 = BottomLeft
        /// 4 = BottomRight
        /// </summary>
        public int Quadrant
        {
            get { return quadrant; }
            set
            {
                quadrant = value <= 4 && value > 0 ? value : 1;
                ResolveClampRail();
            }
        }

        /// <summary>
        /// Rotates the plate clockwise or counter-clockwise along with all parts.
        /// </summary>
        /// <param name="rotationDirection"></param>
        /// <param name="keepSameQuadrant"></param>
        public void Rotate90(RotationType rotationDirection, bool keepSameQuadrant = true)
        {
            const double oneAndHalfPI = System.Math.PI * 1.5;

            Size = new Size(Size.Length, Size.Width);

            // After Size swap above, new Size.Width = old Length (old X extent),
            // new Size.Length = old Width (old Y extent).
            // Convention: Length = X axis, Width = Y axis.
            if (rotationDirection == RotationType.CW)
            {
                Rotate(oneAndHalfPI);

                if (keepSameQuadrant)
                {
                    switch (Quadrant)
                    {
                        case 1:
                            Offset(0, Size.Width);
                            break;

                        case 2:
                            Offset(-Size.Length, 0);
                            break;

                        case 3:
                            Offset(0, -Size.Width);
                            break;

                        case 4:
                            Offset(Size.Length, 0);
                            break;

                        default:
                            return;
                    }
                }
                else
                {
                    Quadrant = Quadrant < 2 ? 4 : Quadrant - 1;
                }
            }
            else
            {
                Rotate(Angle.HalfPI);

                if (keepSameQuadrant)
                {
                    switch (Quadrant)
                    {
                        case 1:
                            Offset(Size.Length, 0);
                            break;

                        case 2:
                            Offset(0, Size.Width);
                            break;

                        case 3:
                            Offset(-Size.Length, 0);
                            break;

                        case 4:
                            Offset(0, -Size.Width);
                            break;

                        default:
                            return;
                    }
                }
                else
                {
                    Quadrant = Quadrant > 3 ? 1 : Quadrant + 1;
                }
            }
        }

        /// <summary>
        /// Rotates the plate 180 degrees along with all parts.
        /// </summary>
        /// <param name="keepSameQuadrant"></param>
        public void Rotate180(bool keepSameQuadrant = true)
        {
            if (keepSameQuadrant)
            {
                Vector centerpt;

                switch (Quadrant)
                {
                    case 1:
                        centerpt = new Vector(Size.Length * 0.5, Size.Width * 0.5);
                        break;

                    case 2:
                        centerpt = new Vector(-Size.Length * 0.5, Size.Width * 0.5);
                        break;

                    case 3:
                        centerpt = new Vector(-Size.Length * 0.5, -Size.Width * 0.5);
                        break;

                    case 4:
                        centerpt = new Vector(Size.Length * 0.5, -Size.Width * 0.5);
                        break;

                    default:
                        return;
                }

                Rotate(System.Math.PI, centerpt);
            }
            else
            {
                Rotate(System.Math.PI);
                Quadrant = (Quadrant + 2) % 4;

                if (Quadrant == 0)
                    Quadrant = 4;
            }
        }

        /// <summary>
        /// Rotates the parts on the plate.
        /// </summary>
        /// <param name="angle"></param>
        public void Rotate(double angle)
        {
            for (var i = Parts.Count - 1; i >= 0; i--)
            {
                if (Parts[i].BaseDrawing.IsCutOff)
                    Parts.RemoveAt(i);
            }

            for (var i = 0; i < Parts.Count; ++i)
            {
                var part = Parts[i];
                part.Rotate(angle);
            }

            foreach (var cutoff in CutOffs)
                cutoff.Position = cutoff.Position.Rotate(angle);
        }

        /// <summary>
        /// Rotates the parts on the plate around the specified origin.
        /// </summary>
        /// <param name="angle"></param>
        /// <param name="origin"></param>
        public void Rotate(double angle, Vector origin)
        {
            for (var i = Parts.Count - 1; i >= 0; i--)
            {
                if (Parts[i].BaseDrawing.IsCutOff)
                    Parts.RemoveAt(i);
            }

            for (var i = 0; i < Parts.Count; ++i)
            {
                var part = Parts[i];
                part.Rotate(angle, origin);
            }

            foreach (var cutoff in CutOffs)
            {
                var pos = cutoff.Position - origin;
                pos = pos.Rotate(angle);
                cutoff.Position = pos + origin;
            }
        }

        /// <summary>
        /// Offsets the parts on the plate.
        /// </summary>
        /// <param name="x"></param>
        /// <param name="y"></param>
        public void Offset(double x, double y)
        {
            // Remove cut-off parts before transforming
            for (var i = Parts.Count - 1; i >= 0; i--)
            {
                if (Parts[i].BaseDrawing.IsCutOff)
                    Parts.RemoveAt(i);
            }

            for (var i = 0; i < Parts.Count; ++i)
            {
                var part = Parts[i];
                part.Offset(x, y);
            }

            // Transform cut-off positions
            foreach (var cutoff in CutOffs)
                cutoff.Position = new Vector(cutoff.Position.X + x, cutoff.Position.Y + y);
        }

        /// <summary>
        /// Offsets the parts on the plate.
        /// </summary>
        /// <param name="voffset"></param>
        public void Offset(Vector voffset)
        {
            for (var i = Parts.Count - 1; i >= 0; i--)
            {
                if (Parts[i].BaseDrawing.IsCutOff)
                    Parts.RemoveAt(i);
            }

            for (var i = 0; i < Parts.Count; ++i)
            {
                var part = Parts[i];
                part.Offset(voffset);
            }

            foreach (var cutoff in CutOffs)
                cutoff.Position = new Vector(
                    cutoff.Position.X + voffset.X,
                    cutoff.Position.Y + voffset.Y
                );
        }

        /// <summary>
        /// The smallest box that contains the plate.
        /// </summary>
        /// <param name="includeParts"></param>
        /// <returns></returns>
        public Box BoundingBox(bool includeParts = true)
        {
            var plateBox = new Box();

            // Width = Y axis (vertical), Length = X axis (horizontal)
            switch (Quadrant)
            {
                case 1:
                    plateBox.X = 0;
                    plateBox.Y = 0;
                    break;

                case 2:
                    plateBox.X = (float)-Size.Length;
                    plateBox.Y = 0;
                    break;

                case 3:
                    plateBox.X = (float)-Size.Length;
                    plateBox.Y = (float)-Size.Width;
                    break;

                case 4:
                    plateBox.X = 0;
                    plateBox.Y = (float)-Size.Width;
                    break;

                default:
                    return new Box();
            }

            plateBox.Width = Size.Width;
            plateBox.Length = Size.Length;

            if (!includeParts)
                return plateBox;

            var boundingBox = new Box();
            var partsBox = Parts.GetBoundingBox();

            boundingBox.X = partsBox.Left < plateBox.Left ? partsBox.Left : plateBox.Left;

            boundingBox.Y = partsBox.Bottom < plateBox.Bottom ? partsBox.Bottom : plateBox.Bottom;

            boundingBox.Length =
                partsBox.Right > plateBox.Right
                    ? partsBox.Right - boundingBox.X
                    : plateBox.Right - boundingBox.X;

            boundingBox.Width =
                partsBox.Top > plateBox.Top
                    ? partsBox.Top - boundingBox.Y
                    : plateBox.Top - boundingBox.Y;

            return boundingBox;
        }

        /// <summary>
        /// The area within the edge spacing.
        /// </summary>
        /// <returns></returns>
        public Box WorkArea()
        {
            var box = BoundingBox(false);

            box.X += EdgeSpacing.Left;
            box.Y += EdgeSpacing.Bottom;
            box.Length -= EdgeSpacing.Left + EdgeSpacing.Right;
            box.Width -= EdgeSpacing.Top + EdgeSpacing.Bottom;

            return box;
        }

        /// <summary>
        /// The offset that centers the parts' combined bounds in the <see cref="WorkArea"/>,
        /// so uneven edge spacing (such as a wider clamp-side margin) shifts them with it.
        /// Zero when the plate has no parts.
        /// </summary>
        public Vector OffsetToCenterPartsInWorkArea() =>
            Parts.Count == 0 ? new Vector() : WorkArea().Center - Parts.GetBoundingBox().Center;

        /// <summary>
        /// Automatically sizes the plate to fit the parts.
        /// </summary>
        /// <param name="roundingFactor">The factor to round the actual size up to.</param>
        /// <example>
        /// AutoSize 9.7 x 10.1
        /// * roundingFactor=1.0   new Size=10 x 11
        /// * roundingFactor=0.5   new Size=10 x 10.5
        /// * roundingFactor=0.25  new Size=9.75 x 10.25
        /// * roundingFactor=0.0   new Size=9.7 x 10.1
        /// </example>
        public void AutoSize(double roundingFactor = 1.0)
        {
            if (Parts.Count == 0)
                return;

            var bounds = Parts.GetBoundingBox();

            // Convention: Length = X axis, Width = Y axis
            double xExtent;
            double yExtent;

            switch (Quadrant)
            {
                case 1:
                    xExtent = System.Math.Abs(bounds.Right) + EdgeSpacing.Right;
                    yExtent = System.Math.Abs(bounds.Top) + EdgeSpacing.Top;
                    break;

                case 2:
                    xExtent = System.Math.Abs(bounds.Left) + EdgeSpacing.Left;
                    yExtent = System.Math.Abs(bounds.Top) + EdgeSpacing.Top;
                    break;

                case 3:
                    xExtent = System.Math.Abs(bounds.Left) + EdgeSpacing.Left;
                    yExtent = System.Math.Abs(bounds.Bottom) + EdgeSpacing.Bottom;
                    break;

                case 4:
                    xExtent = System.Math.Abs(bounds.Right) + EdgeSpacing.Right;
                    yExtent = System.Math.Abs(bounds.Bottom) + EdgeSpacing.Bottom;
                    break;

                default:
                    return;
            }

            Size = new Size(
                Rounding.RoundUpToNearest(yExtent, roundingFactor),
                Rounding.RoundUpToNearest(xExtent, roundingFactor)
            );
        }

        /// <summary>
        /// Sizes the plate using the <see cref="PlateSizes"/> catalog: small
        /// layouts snap to an increment, larger ones round up to the next
        /// standard mill sheet. The plate's long-axis orientation (X vs Y)
        /// is preserved. Does nothing if the plate has no parts.
        /// </summary>
        public PlateSizeResult SnapToStandardSize(PlateSizeOptions options = null)
        {
            if (Parts.Count == 0)
                return default;

            var bounds = Parts.GetBoundingBox();

            // Quadrant-aware extents relative to the plate origin, matching AutoSize.
            double xExtent;
            double yExtent;

            switch (Quadrant)
            {
                case 1:
                    xExtent = System.Math.Abs(bounds.Right) + EdgeSpacing.Right;
                    yExtent = System.Math.Abs(bounds.Top) + EdgeSpacing.Top;
                    break;

                case 2:
                    xExtent = System.Math.Abs(bounds.Left) + EdgeSpacing.Left;
                    yExtent = System.Math.Abs(bounds.Top) + EdgeSpacing.Top;
                    break;

                case 3:
                    xExtent = System.Math.Abs(bounds.Left) + EdgeSpacing.Left;
                    yExtent = System.Math.Abs(bounds.Bottom) + EdgeSpacing.Bottom;
                    break;

                case 4:
                    xExtent = System.Math.Abs(bounds.Right) + EdgeSpacing.Right;
                    yExtent = System.Math.Abs(bounds.Bottom) + EdgeSpacing.Bottom;
                    break;

                default:
                    return default;
            }

            // PlateSizes.Recommend takes (short, long); canonicalize then map
            // the result back so the plate's long axis stays aligned with the
            // parts' long axis.
            var shortDim = System.Math.Min(xExtent, yExtent);
            var longDim = System.Math.Max(xExtent, yExtent);
            var result = PlateSizes.Recommend(shortDim, longDim, options);

            // Plate convention: Length = X axis, Width = Y axis.
            if (xExtent >= yExtent)
                Size = new Size(result.Width, result.Length); // X is the long axis
            else
                Size = new Size(result.Length, result.Width); // Y is the long axis

            return result;
        }

        /// <summary>
        /// Gets the area of the top surface of the plate.
        /// </summary>
        /// <returns></returns>
        public double Area()
        {
            return Size.Width * Size.Length;
        }

        /// <summary>
        /// Gets the volume of the plate.
        /// </summary>
        public double Volume(double thickness)
        {
            return Area() * thickness;
        }

        /// <summary>
        /// Gets the weight of the plate.
        /// </summary>
        public double Weight(double thickness, double density)
        {
            return Volume(thickness) * density;
        }

        /// <summary>
        /// Percentage of the material used.
        /// </summary>
        /// <returns>Returns a number between 0.0 and 1.0</returns>
        public double Utilization()
        {
            return Parts.Where(p => !p.BaseDrawing.IsCutOff).Sum(part => part.BaseDrawing.Area)
                / Area();
        }

        public bool HasOverlappingParts(out List<Vector> pts)
        {
            pts = new List<Vector>();
            var realParts = Parts.Where(p => !p.BaseDrawing.IsCutOff).ToList();

            for (var i = 0; i < realParts.Count; i++)
            {
                var part1 = realParts[i];
                var b1 = part1.BoundingBox;

                for (var j = i + 1; j < realParts.Count; j++)
                {
                    var part2 = realParts[j];
                    var b2 = part2.BoundingBox;

                    // Skip pairs whose bounding boxes don't meaningfully overlap.
                    // Floating-point rounding can produce sub-epsilon overlaps for
                    // parts that are merely edge-touching, so require the overlap
                    // region to exceed Epsilon in both dimensions.
                    var overlapX =
                        System.Math.Min(b1.Right, b2.Right) - System.Math.Max(b1.Left, b2.Left);
                    var overlapY =
                        System.Math.Min(b1.Top, b2.Top) - System.Math.Max(b1.Bottom, b2.Bottom);

                    if (overlapX <= Math.Tolerance.Epsilon || overlapY <= Math.Tolerance.Epsilon)
                        continue;

                    if (part1.Intersects(part2, out var pts2))
                        pts.AddRange(pts2);
                }
            }

            return pts.Count > 0;
        }
    }
}
