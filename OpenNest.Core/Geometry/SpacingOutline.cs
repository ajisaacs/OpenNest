using System;
using System.Collections.Generic;
using OpenNest.Math;

namespace OpenNest.Geometry
{
    /// <summary>
    /// A part's material as world-space polygons for part-spacing checks: the perimeter and its
    /// cutouts, optionally grown by a clearance (perimeter offset outward, cutouts inward, in one
    /// Clipper region offset). Auto Nest's layout check and the pre-post review both judge
    /// spacing with this outline and <see cref="InflationFor"/>, so they cannot disagree.
    /// </summary>
    public sealed class SpacingOutline
    {
        /// <summary>Arc chord tolerance of the outline. Perimeter arcs are circumscribed and
        /// cutout arcs inscribed, so flattening never hides material.</summary>
        public const double ChordTolerance = 0.001;

        /// <summary>How far under the part spacing a gap may fall and still pass. Layouts placed
        /// exactly at the spacing land up to ~1e-4 short once rotated, rounded coordinates
        /// (e.g. PEP's 4-decimal exports) meet the Clipper grid; 0.0005 covers that with margin
        /// and is far below anything a cutting machine can resolve.</summary>
        public const double SpacingSlack = 0.0005;

        private bool prepared;
        private TriangulatedRegion triangles;

        public SpacingOutline(Polygon perimeter, List<Polygon> holes)
        {
            Perimeter = perimeter ?? throw new ArgumentNullException(nameof(perimeter));
            Holes = holes ?? new List<Polygon>();
        }

        public Polygon Perimeter { get; }
        public List<Polygon> Holes { get; }

        /// <summary>Prepared on the first overlap test; null preparation is cached too.</summary>
        public TriangulatedRegion Triangles
        {
            get
            {
                if (!prepared)
                {
                    triangles = TriangulatedRegion.Build(Perimeter, Holes);
                    prepared = true;
                }
                return triangles;
            }
        }

        /// <summary>Inflation that enforces <paramref name="spacing"/> less <see cref="SpacingSlack"/>.
        /// Inflating one part of a pair by it and testing against the other's raw outline covers
        /// both sides: a part inside the other's (shrunk) cutout, or the inflated outline inside
        /// the other's raw cutout.</summary>
        public static double InflationFor(double spacing) =>
            System.Math.Max(0, spacing - SpacingSlack);

        /// <summary>
        /// Outlines <paramref name="profile"/> (already rotated) at <paramref name="location"/>,
        /// grown by <paramref name="inflateBy"/>. A cutout that closes up under the offset is
        /// dropped, which treats it as solid: conservative, since it has no room for another part
        /// at the required spacing anyway. Null when the offset leaves no perimeter.
        /// </summary>
        public static SpacingOutline Build(ShapeProfile profile, Vector location, double inflateBy)
        {
            ArgumentNullException.ThrowIfNull(profile);
            // Flatten by chord tolerance, not a fixed segment count: arc-heavy real parts
            // otherwise produce thousands of vertices.
            var region = ClipperBridge.OffsetForValidation(
                profile,
                inflateBy > Tolerance.Epsilon ? inflateBy : 0,
                ChordTolerance
            );

            var perimeter = region.LargestOuter();

            if (perimeter == null)
                return null;

            ToWorld(perimeter, location);

            foreach (var hole in region.Holes)
                ToWorld(hole, location);

            return new SpacingOutline(perimeter, region.Holes);
        }

        /// <summary>This outline, built at the origin, moved to <paramref name="location"/>: what
        /// <see cref="Build"/> returns there, without repeating the offset for identical parts.</summary>
        public SpacingOutline MovedTo(Vector location)
        {
            var perimeter = (Polygon)Perimeter.Clone();
            ToWorld(perimeter, location);
            var holes = new List<Polygon>(Holes.Count);

            foreach (var hole in Holes)
            {
                var moved = (Polygon)hole.Clone();
                ToWorld(moved, location);
                holes.Add(moved);
            }

            return new SpacingOutline(perimeter, holes);
        }

        /// <summary>True when the two outlines share material (holes subtracted).</summary>
        public bool Overlaps(SpacingOutline other)
        {
            ArgumentNullException.ThrowIfNull(other);
            // A part seated in another's cutout shares nothing with it, and is the triangle
            // test's worst case: every triangle pair overlaps and each piece must be subtracted
            // by the whole cutout before the answer is no.
            if (InsideACutout(this, other) || InsideACutout(other, this))
                return false;
            var at = Triangles;
            var bt = other.Triangles;
            // Keeping translations at zero preserves the reference's floating-point operation order.
            return (at != null && bt != null ? at.Overlaps(bt, 0, 0, 0, 0) : null)
                ?? Collision.HasOverlap(Perimeter, other.Perimeter, Holes, other.Holes);
        }

        /// <summary>True when <paramref name="inner"/>'s perimeter lies strictly inside one
        /// cutout of <paramref name="outer"/>, so the two share no material. A perimeter that
        /// touches or crosses the cutout's edge is not decided here.</summary>
        private static bool InsideACutout(SpacingOutline inner, SpacingOutline outer)
        {
            if (outer.Holes.Count == 0)
                return false;
            var perimeter = inner.Perimeter.Vertices;
            var (left, bottom, right, top) = Bounds(perimeter);

            foreach (var hole in outer.Holes)
            {
                var (holeLeft, holeBottom, holeRight, holeTop) = Bounds(hole.Vertices);

                if (left <= holeLeft || right >= holeRight || bottom <= holeBottom || top >= holeTop)
                    continue;
                if (StrictlyInside(perimeter, hole))
                    return true;
            }

            return false;
        }

        // A cutout is a simple polygon, so a closed outline with every vertex inside it and no
        // edge meeting its edges lies wholly inside it.
        private static bool StrictlyInside(List<Vector> outline, Polygon cutout)
        {
            var edge = cutout.Vertices;
            // A closed polygon repeats its first vertex; its closing edge is the wrap-around.
            var outlineCount = Distinct(outline);
            var edgeCount = Distinct(edge);

            if (outlineCount < 3 || edgeCount < 3)
                return false;

            for (var i = 0; i < outlineCount; i++)
                if (!cutout.ContainsPoint(outline[i]))
                    return false;

            for (var i = 0; i < outlineCount; i++)
            {
                var a = outline[i];
                var b = outline[(i + 1) % outlineCount];

                for (var j = 0; j < edgeCount; j++)
                {
                    var c = edge[j];
                    var d = edge[(j + 1) % edgeCount];

                    // Touching and collinear edges count as meeting: leave those to the full test.
                    if (Side(a, b, c) * Side(a, b, d) <= 0 && Side(c, d, a) * Side(c, d, b) <= 0)
                        return false;
                }
            }

            return true;
        }

        private static int Distinct(List<Vector> vertices) =>
            vertices.Count > 1 && vertices[0] == vertices[^1] ? vertices.Count - 1 : vertices.Count;

        private static double Side(Vector a, Vector b, Vector p) =>
            (b.X - a.X) * (p.Y - a.Y) - (b.Y - a.Y) * (p.X - a.X);

        private static (double Left, double Bottom, double Right, double Top) Bounds(List<Vector> vertices)
        {
            var left = double.PositiveInfinity;
            var bottom = double.PositiveInfinity;
            var right = double.NegativeInfinity;
            var top = double.NegativeInfinity;

            foreach (var vertex in vertices)
            {
                left = System.Math.Min(left, vertex.X);
                bottom = System.Math.Min(bottom, vertex.Y);
                right = System.Math.Max(right, vertex.X);
                top = System.Math.Max(top, vertex.Y);
            }

            return (left, bottom, right, top);
        }

        private static void ToWorld(Polygon polygon, Vector location)
        {
            polygon.Offset(location);
            polygon.UpdateBounds();
        }
    }
}
