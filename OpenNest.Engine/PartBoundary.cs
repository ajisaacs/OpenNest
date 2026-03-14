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
    /// Directional edge filtering is pre-computed once in the constructor.
    /// </summary>
    public class PartBoundary
    {
        private const double PolygonTolerance = 0.01;

        private readonly List<Polygon> _polygons;
        private readonly (Vector start, Vector end)[] _leftEdges;
        private readonly (Vector start, Vector end)[] _rightEdges;
        private readonly (Vector start, Vector end)[] _upEdges;
        private readonly (Vector start, Vector end)[] _downEdges;

        public PartBoundary(Part part, double spacing)
        {
            var entities = ConvertProgram.ToGeometry(part.Program)
                .Where(e => e.Layer != SpecialLayers.Rapid)
                .ToList();

            var definedShape = new ShapeProfile(entities);
            var perimeter = definedShape.Perimeter;
            _polygons = new List<Polygon>();

            if (perimeter != null)
            {
                var offsetEntity = perimeter.OffsetEntity(spacing, OffsetSide.Left) as Shape;

                if (offsetEntity != null)
                {
                    // Circumscribe arcs so polygon vertices are always outside
                    // the true arc — guarantees the boundary never under-estimates.
                    var polygon = offsetEntity.ToPolygonWithTolerance(PolygonTolerance, circumscribe: true);
                    polygon.RemoveSelfIntersections();
                    _polygons.Add(polygon);
                }
            }

            PrecomputeDirectionalEdges(
                out _leftEdges, out _rightEdges, out _upEdges, out _downEdges);
        }

        private void PrecomputeDirectionalEdges(
            out (Vector start, Vector end)[] leftEdges,
            out (Vector start, Vector end)[] rightEdges,
            out (Vector start, Vector end)[] upEdges,
            out (Vector start, Vector end)[] downEdges)
        {
            var left = new List<(Vector, Vector)>();
            var right = new List<(Vector, Vector)>();
            var up = new List<(Vector, Vector)>();
            var down = new List<(Vector, Vector)>();

            foreach (var polygon in _polygons)
            {
                var verts = polygon.Vertices;

                if (verts.Count < 3)
                {
                    for (var i = 1; i < verts.Count; i++)
                    {
                        var edge = (verts[i - 1], verts[i]);
                        left.Add(edge);
                        right.Add(edge);
                        up.Add(edge);
                        down.Add(edge);
                    }

                    continue;
                }

                var sign = polygon.RotationDirection() == RotationType.CCW ? 1.0 : -1.0;

                for (var i = 1; i < verts.Count; i++)
                {
                    var dx = verts[i].X - verts[i - 1].X;
                    var dy = verts[i].Y - verts[i - 1].Y;
                    var edge = (verts[i - 1], verts[i]);

                    if (-sign * dy > 0) left.Add(edge);
                    if ( sign * dy > 0) right.Add(edge);
                    if (-sign * dx > 0) up.Add(edge);
                    if ( sign * dx > 0) down.Add(edge);
                }
            }

            leftEdges = left.ToArray();
            rightEdges = right.ToArray();
            upEdges = up.ToArray();
            downEdges = down.ToArray();
        }

        /// <summary>
        /// Returns offset boundary lines translated to the given location,
        /// filtered to edges whose outward normal faces the specified direction.
        /// </summary>
        public List<Line> GetLines(Vector location, PushDirection facingDirection)
        {
            var edges = GetDirectionalEdges(facingDirection);
            var lines = new List<Line>(edges.Length);

            foreach (var (start, end) in edges)
                lines.Add(new Line(start.Offset(location), end.Offset(location)));

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

        private (Vector start, Vector end)[] GetDirectionalEdges(PushDirection direction)
        {
            switch (direction)
            {
                case PushDirection.Left:  return _leftEdges;
                case PushDirection.Right: return _rightEdges;
                case PushDirection.Up:    return _upEdges;
                case PushDirection.Down:  return _downEdges;
                default:                  return _leftEdges;
            }
        }
    }
}
