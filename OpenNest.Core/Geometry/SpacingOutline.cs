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

        /// <summary>True when the two outlines share material (holes subtracted).</summary>
        public bool Overlaps(SpacingOutline other)
        {
            ArgumentNullException.ThrowIfNull(other);
            var at = Triangles;
            var bt = other.Triangles;
            // Keeping translations at zero preserves the reference's floating-point operation order.
            return (at != null && bt != null ? at.Overlaps(bt, 0, 0, 0, 0) : null)
                ?? Collision.HasOverlap(Perimeter, other.Perimeter, Holes, other.Holes);
        }

        private static void ToWorld(Polygon polygon, Vector location)
        {
            polygon.Offset(location);
            polygon.UpdateBounds();
        }
    }
}
