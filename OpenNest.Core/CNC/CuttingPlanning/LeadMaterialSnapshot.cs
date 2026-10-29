using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using OpenNest.Diagnostics;
using OpenNest.Geometry;
using OpenNest.Math;

namespace OpenNest.CNC.CuttingPlanning;

/// <summary>Owned nominal material: one simple perimeter minus disjoint simple holes.</summary>
public sealed class LeadMaterialSnapshot
{
    private LeadMaterialSnapshot(IReadOnlyList<PostVerificationGeometry.Curve[]> rings, IReadOnlyList<bool[]> tabs,
        string reason, bool displayPerimeter = false)
    {
        HasDisplayPerimeter = displayPerimeter;
        Rings = rings;
        Tabs = tabs;
        Reason = reason;
        var extent = PostVerificationGeometry.Extent.None;
        foreach (var ring in rings)
            foreach (var curve in ring)
                extent = extent.Union(curve.Extent);
        Extent = extent;
        BoundaryCount = rings.Sum(ring => ring.Length);
    }

    public bool IsComplete => Reason == null;
    public string Reason { get; }
    internal IReadOnlyList<PostVerificationGeometry.Curve[]> Rings { get; }

    /// <summary>A conservative extent of every boundary's native contact reach; material lies within it.</summary>
    internal PostVerificationGeometry.Extent Extent { get; }

    /// <summary>The boundary curves of all rings: the native queries one full scan charges.</summary>
    internal int BoundaryCount { get; }

    /// <summary>
    /// Per ring curve, whether it is a tabbed cutout's tab: material boundary that the
    /// program never cuts. Aligned with <see cref="Rings"/>.
    /// </summary>
    internal IReadOnlyList<bool[]> Tabs { get; }

    /// <summary>
    /// True when the perimeter (<see cref="Rings"/>[0]) is a display perimeter: a supplied blank's
    /// edge that bounds material but is never cut. Every other ring is a hole the program cuts.
    /// </summary>
    internal bool HasDisplayPerimeter { get; }

    /// <summary>Capture a stable, clean, rotation-baked program, applying location once.
    /// Unsupported or malformed geometry produces an incomplete snapshot; cancellation throws.</summary>
    public static LeadMaterialSnapshot Capture(Program cleanProgram, Vector location,
        CancellationToken token = default)
    {
        try
        {
            var execution = ExecutionMotionReader.ReadSupported(cleanProgram, location, null, token);
            var rings = new List<PostVerificationGeometry.Curve[]>();
            var ringTabs = new List<bool[]>();
            var chain = new List<PostVerificationGeometry.Curve>();
            var chainTabs = new List<bool>();
            var ringDisplay = new List<bool>();
            var chainDisplay = new List<bool>();
            foreach (var motion in execution.Motions)
            {
                token.ThrowIfCancellationRequested();
                // Break cuts run through material that stays attached at tabs; they bound nothing.
                if (motion.Rapid || motion.Layer is LayerType.Scribe or LayerType.Leadin or LayerType.Leadout or LayerType.Break)
                {
                    Finish();
                    continue;
                }
                // A tabbed cutout's tabs are never cut but still close its hole.
                // A display perimeter is never cut but still bounds the blank's material.
                if (motion.Layer is not (LayerType.Cut or LayerType.Display or LayerType.Tab or LayerType.DisplayPerimeter))
                    throw new ArgumentException("Material capture contains an unsupported layer.");
                var curve = motion.Curve;
                if (curve == null || !double.IsFinite(curve.Length) || curve.Length <= PostVerificationGeometry.Epsilon)
                    throw new ArgumentException("Material contains a degenerate motion.");
                if (chain.Count > 0 && chain[^1].End.DistanceTo(curve.Start) > PostVerificationGeometry.Epsilon)
                    throw new ArgumentException("Material contour is discontinuous.");
                chain.Add(curve);
                chainTabs.Add(motion.Layer == LayerType.Tab);
                chainDisplay.Add(motion.Layer == LayerType.DisplayPerimeter);
                if (NominalContourClosure.IsClosed(chain[0].Start, chain[^1].End))
                    Finish();
            }
            Finish();
            if (rings.Count == 0)
                throw new ArgumentException("Material has no closed contour.");
            var budget = 1000000;
            // Certify simple rings and mutually disjoint boundaries before containment.
            foreach (var (r, i, s, j) in ContactCandidates(rings, token))
            {
                token.ThrowIfCancellationRequested();
                if (--budget < 0)
                    throw new NotSupportedException("Material validation exceeds the native query limit.");
                var a = rings[r][i];
                var b = rings[s][j];
                var contacts = a.Contacts(b, out var overlap);
                var adjacent = r == s && (j == i + 1 || (i == 0 && j == rings[r].Length - 1));
                var arcJoint = adjacent && a.IsFiniteArc && b.IsFiniteArc
                    && (a.End.DistanceTo(b.Start) <= Tolerance.Epsilon
                        || a.Start.DistanceTo(b.End) <= Tolerance.Epsilon);
                // Native arc queries pad angles by 1e-5 radians, admitting
                // intersections on an extension (distance grows with radius).
                // At a shared arc joint require both actual finite spans;
                // overlap and genuine second intersections still refuse.
                if (arcJoint)
                    contacts = contacts.Where(p => !a.IsOutsideArcSpan(p) && !b.IsOutsideArcSpan(p)).ToArray();
                // Native contacts at rounded arc/line joints can drift from the
                // authored endpoint. Use the shared geometry tolerance only to
                // recognize that joint; overlaps and other contacts still refuse.
                if (overlap || contacts.Any(p => !adjacent
                    || !(p.DistanceTo(a.End) <= Tolerance.Epsilon
                        && p.DistanceTo(b.Start) <= Tolerance.Epsilon)
                    && !(p.DistanceTo(a.Start) <= Tolerance.Epsilon
                        && p.DistanceTo(b.End) <= Tolerance.Epsilon)))
                    throw new ArgumentException(r != s && (ringDisplay[r] || ringDisplay[s])
                        ? "A display perimeter must enclose every other contour."
                        : $"Material boundaries overlap, touch or self-intersect ({r}:{i}, {s}:{j}, overlap={overlap}).");
            }
            // A ring encloses a point only inside the union of its curves' extents (each at least
            // the curve's own reach), so a part with thousands of holes asks parity only where it can hold.
            var ringExtents = rings.Select(ring => ring.Aggregate(PostVerificationGeometry.Extent.None,
                (extent, curve) => extent.Union(curve.Extent))).ToArray();
            bool Encloses(int ring, Vector point) => ringExtents[ring].Contains(point) && Inside(point, rings[ring], token);
            var outer = -1;
            for (var i = 0; i < rings.Count; i++)
            {
                token.ThrowIfCancellationRequested();
                if (Enumerable.Range(0, rings.Count).All(j => i == j || Encloses(i, rings[j][0].Start)))
                {
                    if (outer >= 0)
                        throw new ArgumentException("Material perimeter is ambiguous.");
                    outer = i;
                }
            }
            if (outer < 0)
                throw new ArgumentException(ringDisplay.Contains(true)
                    ? "A display perimeter must enclose every other contour."
                    : "Material must have exactly one enclosing perimeter.");
            if (ringTabs[outer].Any(tab => tab))
                throw new ArgumentException("A perimeter cannot have tabs.");
            if (ringDisplay.Where((_, i) => i != outer).Any(display => display))
                throw new ArgumentException("A display perimeter must enclose every other contour.");
            if (ringDisplay[outer] && rings.Count == 1)
                throw new ArgumentException("A display perimeter with no cutouts has nothing to cut.");
            var holeRings = Enumerable.Range(0, rings.Count).Where(i => i != outer).ToArray();
            var holes = holeRings.Select(i => rings[i]).ToArray();
            for (var i = 0; i < holeRings.Length; i++)
                for (var j = i + 1; j < holeRings.Length; j++)
                    if (Encloses(holeRings[j], holes[i][0].Start) || Encloses(holeRings[i], holes[j][0].Start))
                        throw new ArgumentException("Nested cutouts are not supported material.");
            return new LeadMaterialSnapshot(new[] { rings[outer] }.Concat(holes).ToArray(),
                new[] { ringTabs[outer] }.Concat(ringTabs.Where((_, i) => i != outer)).ToArray(), null, ringDisplay[outer]);

            void Finish()
            {
                if (chain.Count == 0)
                    return;
                if (!NominalContourClosure.IsClosed(chain[0].Start, chain[^1].End))
                {
                    var gap = chain[0].Start.DistanceTo(chain[^1].End);
                    throw new ArgumentException(FormattableString.Invariant(
                        $"Nominal material contour {rings.Count + 1} is open: endpoint gap {gap:G6} model units exceeds closure tolerance {NominalContourClosure.Tolerance:G6}.")
                        + " Review the source contour in the drawing editor; gaps cannot be filled implicitly.");
                }
                if (chainDisplay.Contains(true) && chainDisplay.Contains(false))
                    throw new ArgumentException("A display perimeter must be one whole closed contour.");
                rings.Add(chain.ToArray());
                ringTabs.Add(chainTabs.ToArray());
                ringDisplay.Add(chainDisplay.Count > 0 && chainDisplay[0]);
                chain.Clear();
                chainTabs.Clear();
                chainDisplay.Clear();
            }
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
        {
            return new LeadMaterialSnapshot(Array.Empty<PostVerificationGeometry.Curve[]>(), Array.Empty<bool[]>(), ex.Message);
        }
    }

    /// <summary>
    /// The boundary pairs certification queries natively, in the order of the full pairwise scan
    /// (ring, other ring, curve, other curve). A pair whose extents are clear by the lead checks'
    /// margin (<see cref="PostVerificationGeometry.Extent.IsClearOfQueriesFrom"/>) can report no
    /// native contact, so it is left out; a sweep over X finds the rest without visiting every pair.
    /// Material that is not well conditioned keeps every pair.
    /// </summary>
    private static List<(int R, int I, int S, int J)> ContactCandidates(
        IReadOnlyList<PostVerificationGeometry.Curve[]> rings, CancellationToken token)
    {
        var curves = new List<(int Ring, int Index, PostVerificationGeometry.Extent Extent)>();
        var whole = PostVerificationGeometry.Extent.None;
        for (var r = 0; r < rings.Count; r++)
            for (var i = 0; i < rings[r].Length; i++)
            {
                curves.Add((r, i, rings[r][i].Extent));
                whole = whole.Union(rings[r][i].Extent);
            }
        var pairs = new List<(int R, int I, int S, int J)>();
        if (!whole.IsWellConditioned)
        {
            for (var a = 0; a < curves.Count; a++)
                for (var b = a + 1; b < curves.Count; b++)
                    pairs.Add((curves[a].Ring, curves[a].Index, curves[b].Ring, curves[b].Index));
        }
        else
        {
            // No pair spans more than the whole material, so this bounds every pair's margin.
            var reach = PostVerificationGeometry.Extent.ClearMargin + PostVerificationGeometry.Extent.SpanRounding
                * System.Math.Max(whole.MaxX - whole.MinX, whole.MaxY - whole.MinY);
            var byMinX = curves.OrderBy(c => c.Extent.MinX).ToArray();
            for (var a = 0; a < byMinX.Length; a++)
            {
                token.ThrowIfCancellationRequested();
                var first = byMinX[a];
                for (var b = a + 1; b < byMinX.Length && byMinX[b].Extent.MinX <= first.Extent.MaxX + reach; b++)
                {
                    var second = byMinX[b];
                    if (first.Extent.IsClearOfQueriesFrom(second.Extent))
                        continue;
                    pairs.Add((first.Ring, first.Index).CompareTo((second.Ring, second.Index)) < 0
                        ? (first.Ring, first.Index, second.Ring, second.Index)
                        : (second.Ring, second.Index, first.Ring, first.Index));
                }
            }
        }
        pairs.Sort((x, y) => (x.R, x.S, x.I, x.J).CompareTo((y.R, y.S, y.I, y.J)));
        return pairs;
    }

    /// <summary>Material with fewer boundaries is scanned whole, exactly as before the index.</summary>
    private const int IndexedBoundaries = 64;

    private BoundaryIndex index;

    /// <summary>
    /// The boundaries a native query from <paramref name="query"/> can report a contact with, in
    /// ring order: every boundary but those whose extents are clear of it by the lead checks'
    /// margin plus <paramref name="reach"/> (<see cref="PostVerificationGeometry.Extent.IsClearOfQueriesFrom"/>).
    /// Small material returns every boundary.
    /// </summary>
    internal IEnumerable<PostVerificationGeometry.Curve> BoundariesNear(PostVerificationGeometry.Extent query,
        double reach = 0) => PositionsNear(query, reach).Select(p => Rings[p.Ring][p.Index]);

    /// <summary>As <see cref="BoundariesNear"/>, each boundary as its ring and its index in that ring.</summary>
    internal IEnumerable<(int Ring, int Index)> PositionsNear(PostVerificationGeometry.Extent query, double reach = 0)
    {
        if (BoundaryCount < IndexedBoundaries)
            return Rings.SelectMany((ring, r) => ring.Select((_, i) => (r, i)));
        return LazyInitializer.EnsureInitialized(ref index, () => new BoundaryIndex(Rings, Extent)).Near(query, reach);
    }

    internal bool ContainsMaterial(Vector point, CancellationToken token)
    {
        if (BoundaryCount < IndexedBoundaries)
            return Inside(point, Rings[0], token) && !Rings.Skip(1).Any(hole => Inside(point, hole, token));
        var holes = LazyInitializer.EnsureInitialized(ref index, () => new BoundaryIndex(Rings, Extent)).HolesOnRow(point.Y);
        return Inside(point, Rings[0], token) && !holes.Any(hole => Inside(point, Rings[hole], token));
    }

    /// <summary>
    /// A uniform grid of boundary extents over the material, for material with many holes. Read
    /// only once built, so concurrent searches may share it.
    /// </summary>
    private sealed class BoundaryIndex
    {
        private readonly (int Ring, int Index)[] positions;
        private readonly PostVerificationGeometry.Extent[] extents;
        private readonly List<int>[] cells;
        private readonly List<int>[] bands;
        private readonly PostVerificationGeometry.Extent[] ringExtents;
        private readonly int ringCount;
        private readonly PostVerificationGeometry.Extent whole;
        private readonly int columns;
        private readonly int rows;
        private readonly double width;
        private readonly double height;

        internal BoundaryIndex(IReadOnlyList<PostVerificationGeometry.Curve[]> rings, PostVerificationGeometry.Extent whole)
        {
            positions = rings.SelectMany((ring, r) => ring.Select((_, i) => (r, i))).ToArray();
            extents = rings.SelectMany(r => r).Select(b => b.Extent).ToArray();
            ringCount = rings.Count;
            ringExtents = new PostVerificationGeometry.Extent[rings.Count];
            this.whole = whole;
            var side = System.Math.Max(1, (int)System.Math.Ceiling(System.Math.Sqrt(positions.Length)));
            columns = side;
            rows = side;
            width = System.Math.Max(whole.MaxX - whole.MinX, double.Epsilon) / columns;
            height = System.Math.Max(whole.MaxY - whole.MinY, double.Epsilon) / rows;
            cells = new List<int>[columns * rows];
            bands = new List<int>[rows];
            if (!whole.IsWellConditioned)
                return;
            for (var ring = 1; ring < rings.Count; ring++)
            {
                var extent = rings[ring].Aggregate(PostVerificationGeometry.Extent.None, (e, curve) => e.Union(curve.Extent));
                ringExtents[ring] = extent;
                for (var band = Row(extent.MinY - PostVerificationGeometry.Extent.ClearMargin);
                    band <= Row(extent.MaxY + PostVerificationGeometry.Extent.ClearMargin); band++)
                    (bands[band] ??= new List<int>()).Add(ring);
            }
            for (var b = 0; b < positions.Length; b++)
            {
                var (x0, y0, x1, y1) = Cells(extents[b], 0);
                for (var y = y0; y <= y1; y++)
                    for (var x = x0; x <= x1; x++)
                        (cells[y * columns + x] ??= new List<int>()).Add(b);
            }
        }

        /// <summary>
        /// The holes, in ring order, whose curves a ray along row <paramref name="y"/> can cross:
        /// every hole but those whose extents are clear of the row (see
        /// <see cref="PostVerificationGeometry.Extent.IsClearOfRow"/>).
        /// </summary>
        internal IEnumerable<int> HolesOnRow(double y)
        {
            if (!whole.IsWellConditioned || !(System.Math.Abs(y) <= PostVerificationGeometry.Extent.WellConditionedLimit))
                return Enumerable.Range(1, ringCount - 1);
            return (bands[Row(y)] ?? []).Where(ring => !ringExtents[ring].IsClearOfRow(y));
        }

        internal IEnumerable<(int Ring, int Index)> Near(PostVerificationGeometry.Extent query, double reach)
        {
            // The margin covers every pair's span, and each skip is then decided for the pair alone.
            // Material or a query that is not well conditioned (whose cells were never built, for
            // the material) keeps every boundary.
            var union = whole.Union(query);
            var margin = PostVerificationGeometry.Extent.ClearMargin + reach
                + PostVerificationGeometry.Extent.SpanRounding * System.Math.Max(union.MaxX - union.MinX, union.MaxY - union.MinY);
            if (!union.IsWellConditioned || !double.IsFinite(margin))
                return positions;
            var (x0, y0, x1, y1) = Cells(query, margin);
            var found = new List<int>();
            for (var y = y0; y <= y1; y++)
                for (var x = x0; x <= x1; x++)
                    if (cells[y * columns + x] is { } cell)
                        found.AddRange(cell);
            found.Sort();
            var near = new List<(int Ring, int Index)>();
            for (var i = 0; i < found.Count; i++)
                if ((i == 0 || found[i] != found[i - 1]) && !query.IsClearOfQueriesFrom(extents[found[i]], reach))
                    near.Add(positions[found[i]]);
            return near;
        }

        // The cells an extent widened by margin covers, clamped to the grid.
        private (int X0, int Y0, int X1, int Y1) Cells(PostVerificationGeometry.Extent extent, double margin) => (
            Column(extent.MinX - margin), Row(extent.MinY - margin), Column(extent.MaxX + margin), Row(extent.MaxY + margin));

        private int Column(double x) => (int)System.Math.Clamp(System.Math.Floor((x - whole.MinX) / width), 0, columns - 1);

        private int Row(double y) => (int)System.Math.Clamp(System.Math.Floor((y - whole.MinY) / height), 0, rows - 1);
    }

    internal static bool Inside(Vector point, IReadOnlyList<PostVerificationGeometry.Curve> ring, CancellationToken token) =>
        PostVerificationGeometry.Inside(point, ring, token);
}
