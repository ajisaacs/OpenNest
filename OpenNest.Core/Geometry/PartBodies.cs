using System.Collections.Generic;
using System.Linq;

namespace OpenNest.Geometry
{
    /// <summary>A name a source file gives to the geometry inside a region, such as an SVG group's title.</summary>
    public sealed record LabeledRegion(string Name, Box Bounds);

    /// <summary>
    /// The separate parts drawn in one set of entities. Every closed outline that no other outline
    /// holds starts a part; the outlines directly inside it are its holes, and an outline inside one
    /// of those holes starts another part. Entities that are not outlines (open paths, marks, bend
    /// lines, text) belong to the innermost part around them, or to the nearest part when none is.
    /// </summary>
    public sealed class PartBodies
    {
        private readonly List<Body> bodies;
        private readonly Dictionary<Entity, int> outlineOwners = new();

        private PartBodies(List<Body> bodies)
        {
            this.bodies = bodies;
            for (var i = 0; i < bodies.Count; i++)
                foreach (var shape in bodies[i].Contours)
                    foreach (var entity in shape.Entities)
                        outlineOwners[entity] = i;
        }

        /// <summary>The number of parts found; fewer than two means there is nothing to separate.</summary>
        public int Count => bodies.Count;

        /// <summary>The bounds of part <paramref name="index"/>'s outer outline.</summary>
        public Box Bounds(int index) => bodies[index].Outer.BoundingBox;

        /// <summary>
        /// Finds the parts outlined by <paramref name="outlines"/>: the cut entities, without marks or
        /// display-only geometry. Parts are numbered in reading order, top row first, left to right.
        /// </summary>
        public static PartBodies Find(IEnumerable<Entity> outlines)
        {
            var closed = ShapeBuilder
                .GetShapes(outlines.Where(e => e is Line or Arc or Circle))
                .Where(s => s.IsClosed())
                .Select(s => new Contour(s, s.ToPolygon()))
                .Where(c => c.Polygon.Vertices.Count >= 3)
                .ToList();

            foreach (var contour in closed)
                contour.Parent = closed
                    .Where(other => other != contour && Holds(other, contour))
                    .OrderBy(other => other.Shape.BoundingBox.Area())
                    .FirstOrDefault();
            foreach (var contour in closed)
                for (var parent = contour.Parent; parent != null; parent = parent.Parent)
                    contour.Depth++;

            var bodies = closed
                .Where(c => c.Depth % 2 == 0)
                .Select(c => new Body(c))
                .ToList();
            foreach (var hole in closed.Where(c => c.Depth % 2 == 1))
                bodies.First(b => b.OuterContour == hole.Parent).Contours.Add(hole.Shape);

            return new PartBodies(ReadingOrder(bodies));
        }

        /// <summary>The part an entity belongs to, or -1 when no part was found.</summary>
        public int IndexOf(Entity entity)
        {
            if (outlineOwners.TryGetValue(entity, out var owner))
                return owner;
            return IndexOf(RepresentativePoint(entity));
        }

        /// <summary>The innermost part whose outer outline holds <paramref name="point"/>, else the nearest part.</summary>
        public int IndexOf(Vector point)
        {
            var best = -1;
            for (var i = 0; i < bodies.Count; i++)
            {
                var body = bodies[i];
                if (!body.Outer.BoundingBox.Contains(point) || !body.Polygon.ContainsPoint(point))
                    continue;
                if (best < 0 || body.Depth > bodies[best].Depth)
                    best = i;
            }
            if (best >= 0)
                return best;

            var nearest = double.MaxValue;
            for (var i = 0; i < bodies.Count; i++)
            {
                var distance = DistanceTo(bodies[i].Outer.BoundingBox, point);
                if (distance < nearest)
                {
                    nearest = distance;
                    best = i;
                }
            }
            return best;
        }

        /// <summary>Splits <paramref name="entities"/> by part, keeping their order; one list per part.</summary>
        public List<List<Entity>> Group(IEnumerable<Entity> entities)
        {
            var groups = bodies.Select(_ => new List<Entity>()).ToList();
            if (groups.Count == 0)
                return groups;
            foreach (var entity in entities)
                groups[IndexOf(entity)].Add(entity);
            return groups;
        }

        /// <summary>
        /// The name <paramref name="labels"/> give part <paramref name="index"/>: the smallest labeled
        /// region that holds its whole outline and no other part's, or null when there is none.
        /// </summary>
        public string NameOf(int index, IEnumerable<LabeledRegion> labels)
        {
            if (labels == null)
                return null;
            var bounds = Bounds(index);
            var others = Enumerable.Range(0, Count).Where(i => i != index).Select(Bounds).ToList();
            return labels
                .Where(l => !string.IsNullOrWhiteSpace(l.Name) && l.Bounds != null)
                .Where(l => Holds(l.Bounds, bounds) && !others.Any(o => Holds(l.Bounds, o)))
                .OrderBy(l => l.Bounds.Area())
                .Select(l => l.Name.Trim())
                .FirstOrDefault();
        }

        // Within the contour chaining tolerance, so a label region drawn exactly on a part's
        // outline still holds it.
        private static bool Holds(Box outer, Box inner)
        {
            var tolerance = Math.Tolerance.ChainTolerance;
            return inner.Left >= outer.Left - tolerance
                && inner.Right <= outer.Right + tolerance
                && inner.Bottom >= outer.Bottom - tolerance
                && inner.Top <= outer.Top + tolerance;
        }

        // A contour holds another when its bounds hold the other's and its polygon holds most of
        // the other's vertices: a vertex lying on a shared edge cannot decide it alone.
        private static bool Holds(Contour outer, Contour inner)
        {
            var outerBox = outer.Shape.BoundingBox;
            var innerBox = inner.Shape.BoundingBox;
            if (!Holds(outerBox, innerBox) || outerBox.Area() <= innerBox.Area())
                return false;

            var vertices = inner.Polygon.Vertices;
            var step = System.Math.Max(1, vertices.Count / 16);
            var inside = 0;
            var tested = 0;
            for (var i = 0; i < vertices.Count; i += step)
            {
                tested++;
                if (outer.Polygon.ContainsPoint(vertices[i]))
                    inside++;
            }
            return inside * 2 > tested;
        }

        private static Vector RepresentativePoint(Entity entity) =>
            entity switch
            {
                Line line => line.MidPoint,
                Circle circle => circle.Center,
                _ => entity.BoundingBox.Center,
            };

        private static double DistanceTo(Box box, Vector point)
        {
            var dx = System.Math.Max(0, System.Math.Max(box.Left - point.X, point.X - box.Right));
            var dy = System.Math.Max(0, System.Math.Max(box.Bottom - point.Y, point.Y - box.Top));
            return System.Math.Sqrt(dx * dx + dy * dy);
        }

        // Rows from the top: a part joins the current row while its center lies within the height
        // of the row's first part; each row reads left to right.
        private static List<Body> ReadingOrder(List<Body> bodies)
        {
            var remaining = bodies.OrderByDescending(b => b.Outer.BoundingBox.Top).ToList();
            var ordered = new List<Body>();
            while (remaining.Count > 0)
            {
                var first = remaining[0].Outer.BoundingBox;
                var row = remaining
                    .Where(b => b.Outer.BoundingBox.Center.Y >= first.Bottom && b.Outer.BoundingBox.Center.Y <= first.Top)
                    .ToList();
                if (row.Count == 0)
                    row.Add(remaining[0]);
                ordered.AddRange(row.OrderBy(b => b.Outer.BoundingBox.Left));
                remaining.RemoveAll(row.Contains);
            }
            return ordered;
        }

        private sealed class Contour
        {
            public Contour(Shape shape, Polygon polygon)
            {
                Shape = shape;
                Polygon = polygon;
            }

            public Shape Shape { get; }
            public Polygon Polygon { get; }
            public Contour Parent { get; set; }
            public int Depth { get; set; }
        }

        private sealed class Body
        {
            public Body(Contour outer)
            {
                OuterContour = outer;
                Contours.Add(outer.Shape);
            }

            public Contour OuterContour { get; }
            public Shape Outer => OuterContour.Shape;
            public Polygon Polygon => OuterContour.Polygon;
            public int Depth => OuterContour.Depth;
            public List<Shape> Contours { get; } = new();
        }
    }
}
