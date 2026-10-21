using System.Collections.Generic;
using System.Linq;
using System.Threading;
using OpenNest.Converters;
using OpenNest.Geometry;
using OpenNest.Math;

namespace OpenNest.Engine.Fill
{
    /// <summary>
    /// Adds tilted parts beside a layout whose leftover strip is too narrow for the part at 0 or
    /// 90 degrees, such as a long part beside a grid of the same part turned 90 degrees.
    /// The layout also moves flush to one side so its slack forms one strip a tilted part's corner
    /// can tuck into. Added parts clear the layout by the part spacing, tested on the validation
    /// outlines Auto Nest's layout check uses.
    /// </summary>
    public class TiltedRemnantFiller
    {
        private static readonly double SampleStep = Angle.ToRadians(0.25);

        // Steps (in samples) from the most upright fitting angle into its feasible range: the
        // hull's extents are approximate, and a slightly steeper tilt can tile better.
        private static readonly int[] SampleOffsets = { 0, 2, 4, 8, 12 };

        private readonly Box workArea;
        private readonly double partSpacing;
        private readonly IFillComparer comparer;
        private readonly Dictionary<(CNC.Program, double), SpacingOutline> localOutlines = new();

        // Placed copies are tested against every layout variant; reference keys are per copy.
        private readonly Dictionary<Part, SpacingOutline> inflatedOutlines = new(ReferenceEqualityComparer.Instance);

        public TiltedRemnantFiller(Box workArea, double partSpacing, IFillComparer comparer)
        {
            this.workArea = workArea;
            this.partSpacing = partSpacing;
            this.comparer = comparer;
        }

        /// <summary>
        /// Returns <paramref name="layout"/> (possibly moved) plus tilted parts when that beats
        /// the layout, or null when no tilted placement helps.
        /// </summary>
        public List<Part> Fill(Drawing drawing, List<Part> layout, CancellationToken token)
        {
            if (layout == null || layout.Count == 0)
                return null;

            var hull = MaterialHull(drawing);
            if (hull == null)
                return null;

            List<Part> best = null;

            foreach (var alongX in new[] { true, false })
            {
                token.ThrowIfCancellationRequested();
                var candidate = FillSide(drawing, hull, layout, alongX, token);
                if (candidate != null && comparer.IsBetter(candidate, best ?? layout, workArea))
                    best = candidate;
            }

            return best;
        }

        /// <summary>
        /// Fills past the layout's high edge on one axis: the right side when
        /// <paramref name="alongX"/>, otherwise the top.
        /// </summary>
        private List<Part> FillSide(
            Drawing drawing,
            List<Vector> hull,
            List<Part> layout,
            bool alongX,
            CancellationToken token
        )
        {
            // Move the layout flush to the low end so leftover room on both sides becomes one remnant.
            var layoutBox = ((IEnumerable<IBoundable>)layout).GetBoundingBox();
            var toLow = Low(workArea, alongX) - Low(layoutBox, alongX);
            if (!toLow.IsEqualTo(0))
            {
                layout = Offset(layout, Point(alongX, toLow, 0));
                layoutBox = ((IEnumerable<IBoundable>)layout).GetBoundingBox();
            }

            var remnantLow = High(layoutBox, alongX) + partSpacing;
            var remnantSpan = High(workArea, alongX) - remnantLow;
            var crossSpan = Span(workArea, !alongX);

            if (remnantSpan <= Tolerance.Epsilon)
                return null;

            // When the part fits upright, the 0/90-degree strategies already filled this strip.
            foreach (var angle in new[] { 0.0, Angle.HalfPI })
            {
                var (along, cross) = Extents(hull, angle, alongX);
                if (along <= remnantSpan + Tolerance.Epsilon && cross <= crossSpan + Tolerance.Epsilon)
                    return null;
            }

            // A tilted part may reach back over the layout only beside it, in the strip its slack
            // leaves; without slack it must stay in the remnant itself.
            var slack = crossSpan - Span(layoutBox, !alongX);
            var reach = slack > partSpacing ? Span(layoutBox, alongX) : 0;

            var angles = CandidateAngles(hull, alongX, crossSpan, remnantSpan + reach);
            if (angles.Count == 0)
                return null;

            var variants = LayoutVariants(layout, layoutBox, alongX, slack);
            var region = Rect(
                alongX,
                System.Math.Max(Low(workArea, alongX), remnantLow - reach),
                High(workArea, alongX),
                Low(workArea, !alongX),
                High(workArea, !alongX)
            );

            List<Part> best = null;

            foreach (var angle in angles)
            {
                foreach (var direction in new[] { NestDirection.Horizontal, NestDirection.Vertical })
                {
                    token.ThrowIfCancellationRequested();

                    var lattice = new FillLinear(region, partSpacing) { Label = "TiltedRemnant" }
                        .Fill(drawing, angle, direction);
                    if (lattice == null || lattice.Count == 0)
                        continue;

                    foreach (var placed in Alignments(lattice, alongX))
                    {
                        foreach (var variant in variants)
                        {
                            var kept = placed
                                .Where(p => Low(p.BoundingBox, alongX) >= remnantLow - Tolerance.Epsilon
                                    || variant.Clears(p))
                                .ToList();
                            if (kept.Count == 0)
                                continue;

                            var candidate = new List<Part>(variant.Parts.Count + kept.Count);
                            candidate.AddRange(variant.Parts);
                            candidate.AddRange(kept);

                            if (comparer.IsBetter(candidate, best, workArea))
                                best = candidate;
                        }
                    }
                }
            }

            return best;
        }

        /// <summary>
        /// The lattice pushed against the far wall, at both cross-axis ends: the far wall leaves
        /// the most room beside the layout, and either cross end may put a corner in its strip.
        /// </summary>
        private IEnumerable<List<Part>> Alignments(List<Part> lattice, bool alongX)
        {
            var box = ((IEnumerable<IBoundable>)lattice).GetBoundingBox();
            var along = High(workArea, alongX) - High(box, alongX);
            var toLow = Low(workArea, !alongX) - Low(box, !alongX);
            var toHigh = High(workArea, !alongX) - High(box, !alongX);

            yield return Offset(lattice, Point(alongX, along, toLow));

            if (!toHigh.IsEqualTo(toLow))
                yield return Offset(lattice, Point(alongX, along, toHigh));
        }

        private static List<Part> Offset(List<Part> parts, Vector offset) =>
            parts.Select(p => p.CloneAtOffset(offset)).ToList();

        /// <summary>
        /// The layout as placed and moved flush to either cross-axis end, so its slack collects
        /// in one strip on the other side.
        /// </summary>
        private List<LayoutVariant> LayoutVariants(
            List<Part> layout,
            Box layoutBox,
            bool alongX,
            double slack
        )
        {
            var variants = new List<LayoutVariant> { new(layout, this) };
            if (slack <= partSpacing)
                return variants;

            var shifts = new[]
            {
                Low(workArea, !alongX) - Low(layoutBox, !alongX),
                High(workArea, !alongX) - High(layoutBox, !alongX),
            };

            foreach (var shift in shifts.Where(s => !s.IsEqualTo(0)).Distinct())
                variants.Add(new LayoutVariant(Offset(layout, Point(alongX, 0, shift)), this));

            return variants;
        }

        /// <summary>
        /// For each range of angles at which the part spans no more than
        /// <paramref name="crossLimit"/> across and <paramref name="alongLimit"/> along, the most
        /// upright angle (the narrowest along) and a few steeper ones.
        /// </summary>
        private static List<double> CandidateAngles(
            List<Vector> hull,
            bool alongX,
            double crossLimit,
            double alongLimit
        )
        {
            var count = (int)System.Math.Round(System.Math.PI / SampleStep);
            var along = new double[count];
            var fits = new bool[count];

            for (var i = 0; i < count; i++)
            {
                var (a, c) = Extents(hull, i * SampleStep, alongX);
                along[i] = a;
                fits[i] = a <= alongLimit + Tolerance.Epsilon && c <= crossLimit + Tolerance.Epsilon;
            }

            var angles = new List<double>();
            if (fits.All(f => f))
                return angles;

            // Extents repeat every 180 degrees, so a range may wrap; start scanning after a gap.
            var start = System.Array.IndexOf(fits, false);
            for (var k = 1; k <= count; k++)
            {
                var first = (start + k) % count;
                if (!fits[first] || fits[(first - 1 + count) % count])
                    continue;

                var range = new List<int>();
                for (var i = first; fits[i]; i = (i + 1) % count)
                    range.Add(i);

                var upright = range.IndexOf(range.OrderBy(i => along[i]).First());
                foreach (var offset in SampleOffsets)
                {
                    foreach (var at in new[] { upright - offset, upright + offset })
                    {
                        if (at < 0 || at >= range.Count)
                            continue;
                        var angle = range[at] * SampleStep;
                        if (!angles.Any(a => a.IsEqualTo(angle)))
                            angles.Add(angle);
                    }
                }
            }

            return angles;
        }

        private static (double Along, double Cross) Extents(List<Vector> hull, double angle, bool alongX)
        {
            var cos = System.Math.Cos(angle);
            var sin = System.Math.Sin(angle);
            double minX = double.MaxValue, maxX = double.MinValue;
            double minY = double.MaxValue, maxY = double.MinValue;

            foreach (var v in hull)
            {
                var x = v.X * cos - v.Y * sin;
                var y = v.X * sin + v.Y * cos;
                minX = System.Math.Min(minX, x);
                maxX = System.Math.Max(maxX, x);
                minY = System.Math.Min(minY, y);
                maxY = System.Math.Max(maxY, y);
            }

            return alongX ? (maxX - minX, maxY - minY) : (maxY - minY, maxX - minX);
        }

        private static List<Vector> MaterialHull(Drawing drawing)
        {
            var shapes = ShapeBuilder.GetShapes(ConvertProgram.ToGeometry(drawing.Program).ForNesting());
            var points = shapes
                .SelectMany(s => s.ToPolygonWithTolerance(0.01)?.Vertices ?? new List<Vector>())
                .ToList();

            return points.Count < 3 ? null : ConvexHull.Compute(points).Vertices;
        }

        /// <summary>
        /// The part's perimeter outline grown by <paramref name="inflateBy"/>. Cutouts are left
        /// out: tilted parts go beside the layout, never into it, so treating parts as solid only
        /// rejects placements this filler never looks for. Built once per program and moved.
        /// </summary>
        private SpacingOutline Outline(Part part, double inflateBy)
        {
            var key = (part.Program, inflateBy);
            if (!localOutlines.TryGetValue(key, out var local))
            {
                var profile = new ShapeProfile(ConvertProgram.ToGeometry(part.Program).ForNesting());
                local = profile.Perimeter == null
                    ? null
                    : SpacingOutline.Build(new ShapeProfile(profile.Perimeter), new Vector(0, 0), inflateBy);
                localOutlines[key] = local;
            }

            if (local == null)
                return null;

            var perimeter = (Polygon)local.Perimeter.Clone();
            perimeter.Offset(part.Location);
            perimeter.UpdateBounds();
            return new SpacingOutline(perimeter, new List<Polygon>());
        }

        private SpacingOutline InflatedOutline(Part part)
        {
            if (!inflatedOutlines.TryGetValue(part, out var outline))
            {
                outline = Outline(part, SpacingOutline.InflationFor(partSpacing));
                inflatedOutlines[part] = outline;
            }

            return outline;
        }

        private static double Low(Box box, bool alongX) => alongX ? box.Left : box.Bottom;

        private static double High(Box box, bool alongX) => alongX ? box.Right : box.Top;

        private static double Span(Box box, bool alongX) => alongX ? box.Length : box.Width;

        private static Vector Point(bool alongX, double along, double cross) =>
            alongX ? new Vector(along, cross) : new Vector(cross, along);

        private static Box Rect(bool alongX, double alongLow, double alongHigh, double crossLow, double crossHigh) =>
            alongX
                ? new Box(alongLow, crossLow, alongHigh - alongLow, crossHigh - crossLow)
                : new Box(crossLow, alongLow, crossHigh - crossLow, alongHigh - alongLow);

        /// <summary>One position of the layout, with its raw outlines built on first need.</summary>
        private sealed class LayoutVariant
        {
            private readonly TiltedRemnantFiller owner;
            private readonly SpacingOutline[] outlines;

            public LayoutVariant(List<Part> parts, TiltedRemnantFiller owner)
            {
                Parts = parts;
                this.owner = owner;
                outlines = new SpacingOutline[parts.Count];
            }

            public List<Part> Parts { get; }

            /// <summary>True when <paramref name="part"/> is at least the part spacing from every layout part.</summary>
            public bool Clears(Part part)
            {
                var reach = part.BoundingBox.Offset(SpacingOutline.InflationFor(owner.partSpacing));
                SpacingOutline inflated = null;

                for (var i = 0; i < Parts.Count; i++)
                {
                    if (!reach.Intersects(Parts[i].BoundingBox))
                        continue;

                    inflated ??= owner.InflatedOutline(part);
                    outlines[i] ??= owner.Outline(Parts[i], 0);
                    if (inflated == null || outlines[i] == null || inflated.Overlaps(outlines[i]))
                        return false;
                }

                return true;
            }
        }
    }
}
