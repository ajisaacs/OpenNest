using System;
using System.Collections.Generic;
using System.Linq;
using OpenNest.CNC;
using OpenNest.Converters;
using OpenNest.Geometry;

namespace OpenNest.Support
{
    /// <summary>
    /// The material of one placed part as it drops onto the support bed, in world coordinates:
    /// its outline minus its holes, chorded at <see cref="ChordTolerance"/>. A tabbed cutout's
    /// slug stays attached while the sheet is cut (see <see cref="NestingMaterial.ForNesting"/>),
    /// so it counts as part. Immutable; an unusable program gives an incomplete material.
    /// </summary>
    public sealed class SupportMaterial
    {
        /// <summary>Chord tolerance for arcs, in model units.</summary>
        public const double ChordTolerance = 0.001;

        private readonly Vector[] outline;
        private readonly Vector[][] holes;
        private readonly Box bounds = new();

        private SupportMaterial(Vector[] outline, Vector[][] holes, string reason)
        {
            this.outline = outline ?? Array.Empty<Vector>();
            this.holes = holes ?? Array.Empty<Vector[]>();
            Reason = reason;
            if (reason != null)
                return;

            var minX = double.PositiveInfinity;
            var minY = double.PositiveInfinity;
            var maxX = double.NegativeInfinity;
            var maxY = double.NegativeInfinity;
            foreach (var point in this.outline)
            {
                minX = System.Math.Min(minX, point.X);
                minY = System.Math.Min(minY, point.Y);
                maxX = System.Math.Max(maxX, point.X);
                maxY = System.Math.Max(maxY, point.Y);
            }
            bounds = new Box(minX, minY, maxX - minX, maxY - minY);
        }

        /// <summary>True when the material was captured; otherwise <see cref="Reason"/> says why not.</summary>
        public bool IsComplete => Reason == null;

        public string Reason { get; }

        /// <summary>The outline's vertices, without a repeated closing vertex.</summary>
        public IReadOnlyList<Vector> Outline => outline;

        /// <summary>Each hole's vertices, without a repeated closing vertex.</summary>
        public IReadOnlyList<IReadOnlyList<Vector>> Holes => holes;

        /// <summary>A copy of the outline's bounding box.</summary>
        public Box Bounds => new(bounds.X, bounds.Y, bounds.Length, bounds.Width);

        /// <summary>
        /// Material from an outline and holes already in world coordinates. Fewer than three
        /// vertices, or a nonfinite one, gives an incomplete material.
        /// </summary>
        public static SupportMaterial FromPolygons(IEnumerable<Vector> outline, IEnumerable<IEnumerable<Vector>> holes = null)
        {
            var ring = Ring(outline);
            if (ring == null)
                return Incomplete("The part outline is not a polygon.");
            var cutouts = new List<Vector[]>();
            foreach (var hole in holes ?? Enumerable.Empty<IEnumerable<Vector>>())
            {
                var cutout = Ring(hole);
                if (cutout == null)
                    return Incomplete("A part hole is not a polygon.");
                cutouts.Add(cutout);
            }
            return new SupportMaterial(ring, cutouts.ToArray(), null);
        }

        /// <summary>The material of a placed part: its program at its location.</summary>
        public static SupportMaterial FromPart(Part part)
        {
            ArgumentNullException.ThrowIfNull(part);
            return FromProgram(part.Program, part.Location);
        }

        /// <summary>
        /// The material of <paramref name="program"/> (part coordinates, rotation applied) placed
        /// at <paramref name="location"/>. Leads, rapids, scribes and break cuts are not material.
        /// A perimeter left open by a tab still bounds its material, closed across the tab.
        /// </summary>
        public static SupportMaterial FromProgram(Program program, Vector location)
        {
            ArgumentNullException.ThrowIfNull(program);
            try
            {
                var entities = ConvertProgram.ToGeometry(program)
                    .Where(e => e.Layer != SpecialLayers.Leadin && e.Layer != SpecialLayers.Leadout)
                    .ForNesting();
                if (entities.Count == 0)
                    return Incomplete("The part has no material contour.");

                var profile = new ShapeProfile(entities);
                var ring = Ring(Chord(profile.Perimeter, location));
                if (ring == null)
                    return Incomplete("The part outline is not a polygon.");
                var cutouts = new List<Vector[]>();
                foreach (var cutout in profile.Cutouts)
                    if (Ring(Chord(cutout, location)) is { } hole)
                        cutouts.Add(hole);
                return new SupportMaterial(ring, cutouts.ToArray(), null);
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or NotSupportedException
                or ArithmeticException or IndexOutOfRangeException)
            {
                return Incomplete(ex.Message);
            }
        }

        private static SupportMaterial Incomplete(string reason) => new(null, null, reason);

        private static IEnumerable<Vector> Chord(Shape shape, Vector location) =>
            shape.ToPolygonWithTolerance(ChordTolerance).Vertices.Select(v => v + location);

        // The distinct vertices of a closed ring, or null when it cannot bound any area.
        private static Vector[] Ring(IEnumerable<Vector> vertices)
        {
            if (vertices == null)
                return null;
            var ring = new List<Vector>();
            foreach (var vertex in vertices)
            {
                if (!double.IsFinite(vertex.X) || !double.IsFinite(vertex.Y))
                    return null;
                if (ring.Count == 0 || ring[^1] != vertex)
                    ring.Add(vertex);
            }
            if (ring.Count > 1 && ring[0] == ring[^1])
                ring.RemoveAt(ring.Count - 1);
            return ring.Count < 3 ? null : ring.ToArray();
        }

        /// <summary>
        /// The material's area and center of gravity (its area centroid: plate is of even
        /// thickness), or false when the material is incomplete or encloses no area.
        /// </summary>
        public bool TryGetCenterOfGravity(out Vector centerOfGravity, out double area)
        {
            centerOfGravity = Vector.Invalid;
            area = 0;
            if (!IsComplete || !PolygonAreaMoments.TryCompute(outline, out var whole))
                return false;
            var cutouts = new List<PolygonAreaMoments>();
            foreach (var hole in holes)
                if (PolygonAreaMoments.TryCompute(hole, out var moments))
                    cutouts.Add(moments);
            if (!PolygonAreaMoments.TrySubtract(whole, cutouts, out var net))
                return false;
            centerOfGravity = net.Centroid;
            area = net.Area;
            return true;
        }

        /// <summary>
        /// The spans of the horizontal line at <paramref name="y"/> that lie in material, in
        /// ascending order. A line along an edge or through a vertex counts that crossing once,
        /// on the half-open rule, so touching is not crossing.
        /// </summary>
        public IReadOnlyList<(double Start, double End)> SpansAtY(double y)
        {
            var crossings = new List<double>();
            if (!IsComplete || y < bounds.Bottom || y > bounds.Top)
                return Array.Empty<(double, double)>();
            AddCrossings(outline, y, crossings);
            foreach (var hole in holes)
                AddCrossings(hole, y, crossings);
            crossings.Sort();
            var spans = new List<(double, double)>(crossings.Count / 2);
            for (var i = 0; i + 1 < crossings.Count; i += 2)
                spans.Add((crossings[i], crossings[i + 1]));
            return spans;
        }

        /// <summary>
        /// The spans of the vertical line at <paramref name="x"/> that lie in material, in
        /// ascending order, on the same half-open rule as <see cref="SpansAtY"/>.
        /// </summary>
        public IReadOnlyList<(double Start, double End)> SpansAtX(double x)
        {
            var crossings = new List<double>();
            if (!IsComplete || x < bounds.Left || x > bounds.Right)
                return Array.Empty<(double, double)>();
            AddCrossingsAtX(outline, x, crossings);
            foreach (var hole in holes)
                AddCrossingsAtX(hole, x, crossings);
            crossings.Sort();
            var spans = new List<(double, double)>(crossings.Count / 2);
            for (var i = 0; i + 1 < crossings.Count; i += 2)
                spans.Add((crossings[i], crossings[i + 1]));
            return spans;
        }

        /// <summary>Whether <paramref name="point"/> lies in material (even-odd over outline and holes).</summary>
        public bool Contains(Vector point)
        {
            if (!IsComplete)
                return false;
            var inside = Inside(outline, point);
            foreach (var hole in holes)
                if (Inside(hole, point))
                    inside = !inside;
            return inside;
        }

        private static void AddCrossings(Vector[] ring, double y, List<double> crossings)
        {
            for (int i = 0, j = ring.Length - 1; i < ring.Length; j = i++)
            {
                var a = ring[j];
                var b = ring[i];
                if ((a.Y > y) != (b.Y > y))
                    crossings.Add(a.X + (y - a.Y) * (b.X - a.X) / (b.Y - a.Y));
            }
        }

        private static void AddCrossingsAtX(Vector[] ring, double x, List<double> crossings)
        {
            for (int i = 0, j = ring.Length - 1; i < ring.Length; j = i++)
            {
                var a = ring[j];
                var b = ring[i];
                if ((a.X > x) != (b.X > x))
                    crossings.Add(a.Y + (x - a.X) * (b.Y - a.Y) / (b.X - a.X));
            }
        }

        private static bool Inside(Vector[] ring, Vector point)
        {
            var inside = false;
            for (int i = 0, j = ring.Length - 1; i < ring.Length; j = i++)
            {
                var a = ring[j];
                var b = ring[i];
                if ((a.Y > point.Y) != (b.Y > point.Y)
                    && point.X < a.X + (point.Y - a.Y) * (b.X - a.X) / (b.Y - a.Y))
                    inside = !inside;
            }
            return inside;
        }
    }
}
