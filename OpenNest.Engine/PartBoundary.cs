using System.Collections.Generic;
using System.Linq;
using OpenNest.Converters;
using OpenNest.Geometry;

namespace OpenNest
{
    /// <summary>
    /// Pre-computed offset boundary polygons for a part's geometry.
    /// Polygons are stored at program-local origin (no location applied)
    /// and can be efficiently translated to any location when extracting lines.
    /// </summary>
    public class PartBoundary
    {
        private const double ChordTolerance = 0.01;

        private readonly List<Polygon> _polygons;

        public PartBoundary(Part part, double spacing)
        {
            var entities = ConvertProgram.ToGeometry(part.Program);
            var shapes = Helper.GetShapes(entities.Where(e => e.Layer != SpecialLayers.Rapid));
            _polygons = new List<Polygon>();

            foreach (var shape in shapes)
            {
                var offsetEntity = shape.OffsetEntity(spacing + ChordTolerance, OffsetSide.Left) as Shape;

                if (offsetEntity == null)
                    continue;

                var polygon = offsetEntity.ToPolygonWithTolerance(ChordTolerance);
                polygon.RemoveSelfIntersections();
                _polygons.Add(polygon);
            }
        }

        /// <summary>
        /// Returns offset boundary lines translated to the given location,
        /// filtered to edges whose outward normal faces the specified direction.
        /// </summary>
        public List<Line> GetLines(Vector location, PushDirection facingDirection)
        {
            var lines = new List<Line>();

            foreach (var polygon in _polygons)
                lines.AddRange(TranslateDirectionalLines(polygon, location, facingDirection));

            return lines;
        }

        /// <summary>
        /// Returns all offset boundary lines translated to the given location.
        /// </summary>
        public List<Line> GetLines(Vector location)
        {
            var lines = new List<Line>();

            foreach (var polygon in _polygons)
            {
                var verts = polygon.Vertices;

                if (verts.Count < 2)
                    continue;

                var last = verts[0].Offset(location);

                for (var i = 1; i < verts.Count; i++)
                {
                    var current = verts[i].Offset(location);
                    lines.Add(new Line(last, current));
                    last = current;
                }
            }

            return lines;
        }

        private static List<Line> TranslateDirectionalLines(
            Polygon polygon, Vector location, PushDirection facingDirection)
        {
            var verts = polygon.Vertices;

            if (verts.Count < 3)
            {
                var fallback = new List<Line>();

                if (verts.Count >= 2)
                {
                    var last = verts[0].Offset(location);

                    for (var i = 1; i < verts.Count; i++)
                    {
                        var current = verts[i].Offset(location);
                        fallback.Add(new Line(last, current));
                        last = current;
                    }
                }

                return fallback;
            }

            var sign = polygon.RotationDirection() == RotationType.CCW ? 1.0 : -1.0;
            var result = new List<Line>();
            var prev = verts[0].Offset(location);

            for (var i = 1; i < verts.Count; i++)
            {
                var current = verts[i].Offset(location);

                // Use un-translated deltas — translation doesn't affect direction.
                var dx = verts[i].X - verts[i - 1].X;
                var dy = verts[i].Y - verts[i - 1].Y;

                bool keep;

                switch (facingDirection)
                {
                    case PushDirection.Left:  keep = -sign * dy > 0; break;
                    case PushDirection.Right: keep =  sign * dy > 0; break;
                    case PushDirection.Up:    keep = -sign * dx > 0; break;
                    case PushDirection.Down:  keep =  sign * dx > 0; break;
                    default: keep = true; break;
                }

                if (keep)
                    result.Add(new Line(prev, current));

                prev = current;
            }

            return result;
        }
    }
}
