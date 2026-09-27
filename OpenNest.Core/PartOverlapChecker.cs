using System.Collections.Generic;
using OpenNest.Geometry;

namespace OpenNest
{
    /// <summary>
    /// Overlap-only form of <see cref="Part.Intersects"/> for one pass over a fixed set of parts.
    /// Verdicts match <c>Intersects(other, out _)</c>. Each distinct <see cref="CNC.Program"/>
    /// (by reference) is prepared once, each part's world polygon is built once, and crossing
    /// points are not computed. Tiled copies from <see cref="Part.CloneAtOffset"/> share one
    /// Program, so a fill grid prepares its pattern's programs only once.
    /// Parts must not move, rotate or change Program while an instance is in use. Instances are
    /// not thread-safe: create one per check.
    /// </summary>
    public sealed class PartOverlapChecker
    {
        private readonly Dictionary<CNC.Program, PreparedProgram> programs = new(
            ReferenceEqualityComparer.Instance
        );

        private readonly Dictionary<Part, Polygon> worldPolygons = new(
            ReferenceEqualityComparer.Instance
        );

        /// <summary>
        /// Same verdict as <c>part1.Intersects(part2, out _)</c>. Preparation stages run in the
        /// same order, with the same early exits, the first time each Program is needed.
        /// </summary>
        public bool Overlaps(Part part1, Part part2)
        {
            PerfCounters.CountPartIntersects();

            var prepared1 = Prepare(part1.Program);
            var prepared2 = Prepare(part2.Program);

            if (prepared1.Material.Count == 0 || prepared2.Material.Count == 0)
                return false;

            var perimeter1 = prepared1.GetPerimeter();
            var perimeter2 = prepared2.GetPerimeter();

            if (perimeter1 == null || perimeter2 == null)
                return false;

            var polygon1 = WorldPolygon(part1, prepared1);
            var polygon2 = WorldPolygon(part2, prepared2);

            if (polygon1 == null || polygon2 == null)
                return false;

            return Collision.HasOverlap(polygon1, polygon2);
        }

        private PreparedProgram Prepare(CNC.Program program)
        {
            if (!programs.TryGetValue(program, out var prepared))
            {
                prepared = new PreparedProgram(Part.MaterialEntities(program));
                programs.Add(program, prepared);
            }

            return prepared;
        }

        private Polygon WorldPolygon(Part part, PreparedProgram prepared)
        {
            if (worldPolygons.TryGetValue(part, out var polygon))
                return polygon;

            var local = prepared.GetLocalPolygon();

            if (local != null)
            {
                // Clone copies the vertices but not the bounds. Recomputing the bounds from the
                // same vertices and then offsetting reproduces Part.Intersects' polygon bit for bit.
                polygon = (Polygon)local.Clone();
                polygon.UpdateBounds();
                polygon.Offset(part.Location);
            }

            worldPolygons.Add(part, polygon);
            return polygon;
        }

        private sealed class PreparedProgram
        {
            private bool perimeterReady;
            private Shape perimeter;
            private bool polygonReady;
            private Polygon localPolygon;

            public PreparedProgram(List<Entity> material) => Material = material;

            public List<Entity> Material { get; }

            public Shape GetPerimeter()
            {
                if (!perimeterReady)
                {
                    perimeter = new ShapeProfile(Material).Perimeter;
                    perimeterReady = true;
                }

                return perimeter;
            }

            /// <summary>Only called after <see cref="GetPerimeter"/> returned non-null.</summary>
            public Polygon GetLocalPolygon()
            {
                if (!polygonReady)
                {
                    localPolygon = Part.BuildOverlapPolygon(perimeter);
                    polygonReady = true;
                }

                return localPolygon;
            }
        }
    }
}
