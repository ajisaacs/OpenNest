using System;
using System.Collections.Generic;
using System.Linq;
using Clipper2Lib;
using OpenNest.Geometry;

namespace OpenNest.Support
{
    /// <summary>
    /// One straight slice of a cutout's slug, in the part's local coordinates: pierced at
    /// <see cref="Pierce"/> on the slice, led back along it to <see cref="Start"/>, then cut to
    /// <see cref="End"/>. <see cref="Pierce"/> equals <see cref="Start"/> when there is no lead.
    /// </summary>
    public sealed record SlugSlice(Vector Pierce, Vector Start, Vector End);

    /// <summary>
    /// Slices for the slug of a cutout its support bed cannot hold level (see
    /// docs/support-tabs.md#slug-slices). The slug is cut along every line the bed's contacts lie
    /// on (<see cref="SupportBed.ContactLines"/>), so each piece keeps no contact and drops between
    /// the supports once the cutout is cut, instead of tipping. Each slice stops a standoff (the
    /// kerf) short of the cutout, so the pieces stay attached until the cutout itself is cut and
    /// no slice reaches the part.
    /// </summary>
    public static class SlugSlicer
    {
        private const double RoundingAllowance = 2e-4;

        /// <summary>
        /// Whether a cutout's slug, <paramref name="slug"/> (world coordinates), would tip once cut
        /// free: it rests on at least one contact of <paramref name="bed"/> but the bed cannot hold
        /// it level. A slug on no contact falls through whole, so it is not sliced.
        /// </summary>
        public static bool Tips(SupportBed bed, int quadrant, SupportMaterial slug)
        {
            ArgumentNullException.ThrowIfNull(bed);
            ArgumentNullException.ThrowIfNull(slug);
            var support = SupportAnalyzer.Analyze(bed, quadrant, slug);
            return support.IsComplete && !support.IsSupported && support.Contacts.Count > 0;
        }

        /// <summary>
        /// The slices of the slug inside <paramref name="hole"/> (a closed polygon in the part's
        /// local coordinates; world = local + <paramref name="location"/>), along the contact lines
        /// of <paramref name="bed"/> on a sheet in <paramref name="quadrant"/>. Each slice keeps at
        /// least <paramref name="standoff"/> from the hole, is longer than it, and is pierced
        /// <paramref name="reach"/> along itself from its start (at most halfway) where the pierce
        /// keeps <paramref name="pierceClearance"/> from the hole; a slice whose pierce fits at
        /// neither end is left out. Rows (lines along X) come first, then columns, each set from
        /// its end nearer the last slice, and each slice starts at its end nearer where the one
        /// before it ended, so the head runs back and forth.
        /// </summary>
        public static IReadOnlyList<SlugSlice> Slices(SupportBed bed, int quadrant, IReadOnlyList<Vector> hole,
            Vector location, double standoff, double reach, double pierceClearance)
        {
            ArgumentNullException.ThrowIfNull(bed);
            ArgumentNullException.ThrowIfNull(hole);
            if (!(standoff > 0) || !double.IsFinite(standoff))
                throw new ArgumentOutOfRangeException(nameof(standoff), "The standoff must be finite and positive.");
            if (!(reach >= 0) || !double.IsFinite(reach))
                throw new ArgumentOutOfRangeException(nameof(reach));
            if (!(pierceClearance >= 0) || !double.IsFinite(pierceClearance))
                throw new ArgumentOutOfRangeException(nameof(pierceClearance));

            var ring = Ring(hole);
            if (ring.Count < 3)
                return Array.Empty<SlugSlice>();
            // Clipper rounds to 1e-4 (ClipperBridge.Precision): erode a little further so no slice
            // end comes inside the standoff.
            var eroded = Clipper.InflatePaths(new PathsD { ToPath(ring) }, -(standoff + RoundingAllowance),
                JoinType.Round, EndType.Polygon, 2.0, ClipperBridge.Precision);
            if (eroded.Count == 0)
                return Array.Empty<SlugSlice>();

            var bounds = Bounds(ring);
            var world = new Box(bounds.Left + location.X, bounds.Bottom + location.Y, bounds.Length, bounds.Width);
            var lines = bed.ContactLines(world, quadrant);
            // Each line's pieces inside the eroded hole, local; lines that miss it are dropped.
            var groups = new[] { true, false }.Select(alongX => lines
                    .Where(line => line.AlongX == alongX)
                    .OrderBy(line => line.At)
                    .Select(line => Segments(eroded, line, location, standoff))
                    .Where(segments => segments.Count > 0)
                    .ToList())
                .ToArray();

            var slices = new List<SlugSlice>();
            var clearance = System.Math.Max(pierceClearance, standoff);
            Vector? position = null;
            foreach (var group in groups)
            {
                if (group.Count == 0)
                    continue;
                // Rows, then columns, each set from the end nearer the last slice, back and forth:
                // every slice starts at its end nearer where the one before it ended.
                if (position is { } from && Nearest(group[^1], from) < Nearest(group[0], from))
                    group.Reverse();
                foreach (var segments in group)
                {
                    if (position is { } here && Nearest([segments[^1]], here) < Nearest([segments[0]], here))
                        segments.Reverse();
                    foreach (var (a, b) in segments)
                    {
                        var (start, end) = position is { } at && b.DistanceTo(at) < a.DistanceTo(at) ? (b, a) : (a, b);
                        var slice = Pierced(start, end, reach, ring, clearance)
                            ?? Pierced(end, start, reach, ring, clearance);
                        if (slice == null)
                            continue;
                        slices.Add(slice);
                        position = slice.End;
                    }
                }
            }
            return slices;
        }

        // The pieces of one contact line inside the eroded hole, local, low to high, each longer
        // than the standoff.
        private static List<(Vector A, Vector B)> Segments(PathsD eroded, ContactLine line, Vector location,
            double standoff)
        {
            var at = line.At - (line.AlongX ? location.Y : location.X);
            return Spans(eroded, line.AlongX, at)
                .Where(span => span.High - span.Low > standoff)
                .Select(span => line.AlongX
                    ? (new Vector(span.Low, at), new Vector(span.High, at))
                    : (new Vector(at, span.Low), new Vector(at, span.High)))
                .ToList();
        }

        private static double Nearest(List<(Vector A, Vector B)> segments, Vector point) =>
            segments.Min(segment => System.Math.Min(segment.A.DistanceTo(point), segment.B.DistanceTo(point)));

        // A slice from start to end pierced on itself, or null when the pierce is too close to the hole.
        private static SlugSlice Pierced(Vector start, Vector end, double reach, IReadOnlyList<Vector> ring,
            double clearance)
        {
            var length = start.DistanceTo(end);
            var pierce = start + (end - start) * (System.Math.Min(reach, length / 2) / length);
            return Distance(pierce, ring) >= clearance - Math.Tolerance.Epsilon
                ? new SlugSlice(pierce, start, end)
                : null;
        }

        // Where the line at `at` (Y along X, or X along Y) runs inside the eroded paths, sorted.
        private static List<(double Low, double High)> Spans(PathsD paths, bool alongX, double at)
        {
            var spans = new List<(double, double)>();
            foreach (var path in paths)
            {
                var crossings = new List<double>();
                for (var i = 0; i < path.Count; i++)
                {
                    var p = path[i];
                    var q = path[(i + 1) % path.Count];
                    var (pa, pb, qa, qb) = alongX ? (p.y, p.x, q.y, q.x) : (p.x, p.y, q.x, q.y);
                    if (pa > at == qa > at)
                        continue;
                    crossings.Add(pb + (at - pa) * (qb - pb) / (qa - pa));
                }
                crossings.Sort();
                for (var i = 0; i + 1 < crossings.Count; i += 2)
                    spans.Add((crossings[i], crossings[i + 1]));
            }
            spans.Sort((x, y) => x.Item1.CompareTo(y.Item1));
            return spans;
        }

        private static double Distance(Vector point, IReadOnlyList<Vector> ring)
        {
            var best = double.PositiveInfinity;
            for (var i = 0; i < ring.Count; i++)
            {
                var a = ring[i];
                var b = ring[(i + 1) % ring.Count];
                var edge = b - a;
                var squared = edge.X * edge.X + edge.Y * edge.Y;
                var t = squared > 0
                    ? System.Math.Clamp(((point.X - a.X) * edge.X + (point.Y - a.Y) * edge.Y) / squared, 0, 1)
                    : 0;
                best = System.Math.Min(best, point.DistanceTo(a + edge * t));
            }
            return best;
        }

        // The polygon without a repeated closing vertex.
        private static List<Vector> Ring(IReadOnlyList<Vector> polygon)
        {
            var ring = polygon.ToList();
            if (ring.Count > 1 && ring[0].DistanceTo(ring[^1]) <= Math.Tolerance.Epsilon)
                ring.RemoveAt(ring.Count - 1);
            return ring;
        }

        private static PathD ToPath(IReadOnlyList<Vector> ring)
        {
            var path = new PathD(ring.Count);
            foreach (var vertex in ring)
                path.Add(new PointD(vertex.X, vertex.Y));
            return path;
        }

        private static Box Bounds(IReadOnlyList<Vector> ring)
        {
            var left = ring.Min(v => v.X);
            var bottom = ring.Min(v => v.Y);
            return new Box(left, bottom, ring.Max(v => v.X) - left, ring.Max(v => v.Y) - bottom);
        }
    }
}
