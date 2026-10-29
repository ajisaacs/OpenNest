using System;
using System.Collections.Generic;
using System.Linq;
using Clipper2Lib;
using OpenNest.Geometry;

namespace OpenNest
{
    /// <summary>
    /// The material of a sheet that is not a rectangle: a remnant, a drop, or a sheet with a
    /// burned or damaged area. A closed polygon in plate coordinates (the sheet's lower-left
    /// frame, as <see cref="Plate.ClampZones"/>) with optional obstacle polygons for areas inside
    /// it that must stay empty. The polygon lies inside the true material and every obstacle
    /// polygon contains its true area, so chording never adds room. Its bounding box starts at
    /// the origin and is the sheet's <see cref="Size"/>; rectangular code paths keep using that
    /// box. Immutable.
    /// </summary>
    public sealed class SheetOutline : IEquatable<SheetOutline>
    {
        /// <summary>Chord deviation when importing arcs; the outline is also eroded, and each
        /// obstacle inflated, by this much so the stored polygons never reach past the true edges.</summary>
        public const double ChordTolerance = SpacingOutline.ChordTolerance;

        private const int Precision = ClipperBridge.Precision;
        private const double ArcTolerance = 0.001;
        private const double MiterLimit = 2.0;

        private readonly PathD outline;
        private readonly PathsD obstacles;
        private readonly PathsD material;

        /// <summary>
        /// Builds an outline from its vertices and optional obstacles, each a simple polygon of at
        /// least three finite vertices with a positive area. Winding is normalized and the whole
        /// is translated so the outline's bounds start at the origin; an obstacle wholly outside
        /// the outline is dropped.
        /// </summary>
        /// <exception cref="ArgumentException">A polygon is missing, degenerate, nonfinite or
        /// self-intersecting.</exception>
        public SheetOutline(IEnumerable<Vector> points, IEnumerable<IEnumerable<Vector>> obstacles = null)
        {
            ArgumentNullException.ThrowIfNull(points);
            var path = SimplePolygon(points, "outline");
            var box = Clipper.GetBounds(path);
            var shift = new PointD(-box.left, -box.top); // RectD top is the minimum Y.
            outline = Clipper.TranslatePath(path, shift.x, shift.y);
            Size = new Size(box.Height, box.Width);

            this.obstacles = new PathsD();
            if (obstacles != null)
            {
                var index = 0;
                foreach (var obstacle in obstacles)
                {
                    index++;
                    var obstaclePath = Clipper.TranslatePath(SimplePolygon(obstacle, $"obstacle {index}"), shift.x, shift.y);
                    var inside = Clipper.Intersect(new PathsD { outline }, new PathsD { obstaclePath }, FillRule.NonZero, Precision);
                    if (inside.Sum(Clipper.Area) > 0)
                        this.obstacles.Add(obstaclePath);
                }
            }

            material = Clipper.Difference(new PathsD { outline }, this.obstacles, FillRule.NonZero, Precision);
            Area = material.Sum(Clipper.Area);
            if (!(Area > 0))
                throw new ArgumentException("The obstacles leave no material inside the outline.", nameof(obstacles));

            Points = outline.Select(p => new Vector(p.x, p.y)).ToArray();
            Obstacles = this.obstacles
                .Select(o => (IReadOnlyList<Vector>)o.Select(p => new Vector(p.x, p.y)).ToArray())
                .ToArray();
        }

        /// <summary>Vertices of the outline, counterclockwise, in plate coordinates.</summary>
        public IReadOnlyList<Vector> Points { get; }

        /// <summary>Vertices of each obstacle, counterclockwise, in plate coordinates.</summary>
        public IReadOnlyList<IReadOnlyList<Vector>> Obstacles { get; }

        /// <summary>The bounding box of the outline: the plate's size.</summary>
        public Size Size { get; }

        /// <summary>Material area: the outline less its obstacles.</summary>
        public double Area { get; }

        /// <summary>
        /// Where the plate's lower-left corner lies in the placement frame of its parts for
        /// <paramref name="quadrant"/>: -Length on X in quadrants 2 and 3, -Width on Y in 3 and 4,
        /// exactly as <see cref="ClampZoneFrame"/> and the engine stock translate.
        /// </summary>
        public Vector PlacementOrigin(int quadrant) =>
            new(quadrant is 2 or 3 ? -Size.Length : 0, quadrant is 3 or 4 ? -Size.Width : 0);

        /// <summary>
        /// The outline less its obstacles, translated by <paramref name="origin"/>: outer paths
        /// positive, holes negative.
        /// </summary>
        public PathsD Material(Vector origin) => Translate(material, origin);

        /// <summary>
        /// The material eroded by <paramref name="inset"/> (round joins) and translated by
        /// <paramref name="origin"/>: where a part may lie when it must keep <paramref name="inset"/>
        /// from every edge and obstacle. Empty when nothing is left. A nonpositive inset returns
        /// the material itself.
        /// </summary>
        public PathsD Usable(double inset, Vector origin)
        {
            if (!(inset > 0))
                return Material(origin);
            var eroded = Clipper.InflatePaths(material, -inset, JoinType.Round, EndType.Polygon,
                MiterLimit, Precision, ArcTolerance);
            eroded.RemoveAll(p => p.Count < 3);
            return Translate(eroded, origin);
        }

        /// <summary>
        /// Whether a region lies inside the material eroded by <paramref name="inset"/>, allowing
        /// <paramref name="slack"/> outside it: nothing of <paramref name="region"/> (outer paths
        /// positive, holes negative, in the frame of <paramref name="origin"/>) is left after
        /// subtracting the usable area grown by the slack. The test is exact on the stored polygons;
        /// it never loosens for arcs.
        /// </summary>
        public bool Holds(PathsD region, double inset, Vector origin, double slack)
        {
            var usable = Usable(inset, origin);
            if (slack > 0)
            {
                usable = Clipper.InflatePaths(usable, slack, JoinType.Miter, EndType.Polygon,
                    MiterLimit, Precision, ArcTolerance);
            }
            var outside = Clipper.Difference(region, usable, FillRule.NonZero, Precision);
            return outside.Sum(p => System.Math.Abs(Clipper.Area(p))) <= 0;
        }

        /// <summary>This outline turned a quarter turn about the origin, re-normalized to start
        /// at the origin, as <see cref="Plate.Rotate90"/> turns the sheet.</summary>
        public SheetOutline Rotated90(RotationType direction)
        {
            Vector Turn(Vector p) => direction == RotationType.CW ? new Vector(p.Y, -p.X) : new Vector(-p.Y, p.X);
            return new SheetOutline(Points.Select(Turn), Obstacles.Select(o => o.Select(Turn)));
        }

        /// <summary>This outline turned half a turn, re-normalized to start at the origin.</summary>
        public SheetOutline Rotated180() =>
            new(Points.Select(p => new Vector(-p.X, -p.Y)), Obstacles.Select(o => o.Select(p => new Vector(-p.X, -p.Y))));

        /// <summary>
        /// An outline from imported geometry: the largest closed contour becomes the outline and
        /// every other closed contour an obstacle. Arcs are chorded at <see cref="ChordTolerance"/>,
        /// then the outline is eroded and each obstacle inflated by that much, so the stored
        /// polygons stay within the true edges. Marks and open contours are ignored.
        /// </summary>
        /// <exception cref="ArgumentException">No closed contour with a positive area.</exception>
        public static SheetOutline FromGeometry(IEnumerable<Entity> entities)
        {
            ArgumentNullException.ThrowIfNull(entities);
            var material = entities.Where(e => SpecialLayers.IsMaterial(e.Layer)).ToList();
            ShapeProfile profile;
            try
            {
                profile = material.Count == 0 ? null : new ShapeProfile(material);
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
            {
                throw new ArgumentException("The geometry has no closed outline.", nameof(entities), exception);
            }
            if (profile?.Perimeter == null)
                throw new ArgumentException("The geometry has no closed outline.", nameof(entities));

            var perimeter = ClipperBridge.ToPath(ClipperBridge.Flatten(profile.Perimeter, ChordTolerance, circumscribe: false), positive: true);
            if (perimeter.Count < 3 || !(Clipper.Area(perimeter) > 0))
                throw new ArgumentException("The geometry has no closed outline.", nameof(entities));
            var outer = Clipper.InflatePaths(new PathsD { perimeter },
                -ChordTolerance, JoinType.Round, EndType.Polygon, MiterLimit, Precision, ArcTolerance);
            var outline = outer.Where(p => p.Count >= 3).OrderByDescending(Clipper.Area).FirstOrDefault()
                ?? throw new ArgumentException("The outline is too thin to hold material.", nameof(entities));

            var obstacles = new List<IEnumerable<Vector>>();
            foreach (var cutout in profile.Cutouts)
            {
                var hole = ClipperBridge.ToPath(ClipperBridge.Flatten(cutout, ChordTolerance, circumscribe: false), positive: true);
                if (hole.Count < 3 || !(Clipper.Area(hole) > 0))
                    continue;
                var grown = Clipper.InflatePaths(new PathsD { hole },
                    ChordTolerance, JoinType.Round, EndType.Polygon, MiterLimit, Precision, ArcTolerance);
                foreach (var path in grown.Where(p => p.Count >= 3 && Clipper.IsPositive(p)))
                    obstacles.Add(path.Select(p => new Vector(p.x, p.y)).ToList());
            }
            return new SheetOutline(outline.Select(p => new Vector(p.x, p.y)), obstacles);
        }

        public bool Equals(SheetOutline other)
        {
            if (other is null)
                return false;
            if (ReferenceEquals(this, other))
                return true;
            if (Points.Count != other.Points.Count || Obstacles.Count != other.Obstacles.Count)
                return false;
            for (var i = 0; i < Points.Count; i++)
                if (!Same(Points[i], other.Points[i]))
                    return false;
            for (var k = 0; k < Obstacles.Count; k++)
            {
                if (Obstacles[k].Count != other.Obstacles[k].Count)
                    return false;
                for (var i = 0; i < Obstacles[k].Count; i++)
                    if (!Same(Obstacles[k][i], other.Obstacles[k][i]))
                        return false;
            }
            return true;
        }

        public override bool Equals(object obj) => Equals(obj as SheetOutline);

        public override int GetHashCode()
        {
            var hash = new System.HashCode();
            hash.Add(Points.Count);
            hash.Add(Obstacles.Count);
            foreach (var p in Points)
            {
                hash.Add(BitConverter.DoubleToInt64Bits(p.X));
                hash.Add(BitConverter.DoubleToInt64Bits(p.Y));
            }
            return hash.ToHashCode();
        }

        public override string ToString() =>
            $"{Size} outline, {Points.Count} vertices" + (Obstacles.Count == 0 ? "" : $", {Obstacles.Count} obstacle(s)");

        /// <summary>Exact bits: two outlines are the same stock only when nothing moved.</summary>
        private static bool Same(Vector a, Vector b) =>
            BitConverter.DoubleToInt64Bits(a.X) == BitConverter.DoubleToInt64Bits(b.X)
            && BitConverter.DoubleToInt64Bits(a.Y) == BitConverter.DoubleToInt64Bits(b.Y);

        private static PathsD Translate(PathsD paths, Vector origin) =>
            origin.X == 0 && origin.Y == 0
                ? new PathsD(paths.Select(p => new PathD(p)))
                : Clipper.TranslatePaths(paths, origin.X, origin.Y);

        /// <summary>A counterclockwise copy without the closing or repeated vertices, after
        /// checking it is one simple polygon of positive area.</summary>
        private static PathD SimplePolygon(IEnumerable<Vector> points, string what)
        {
            ArgumentNullException.ThrowIfNull(points, nameof(points));
            var path = new PathD();
            foreach (var p in points)
            {
                if (!double.IsFinite(p.X) || !double.IsFinite(p.Y))
                    throw new ArgumentException($"The {what} has a nonfinite vertex.", nameof(points));
                if (path.Count > 0 && path[^1].x == p.X && path[^1].y == p.Y)
                    continue;
                path.Add(new PointD(p.X, p.Y));
            }
            if (path.Count > 1 && path[0].x == path[^1].x && path[0].y == path[^1].y)
                path.RemoveAt(path.Count - 1);
            if (path.Count < 3)
                throw new ArgumentException($"The {what} needs at least three distinct vertices.", nameof(points));
            var area = Clipper.Area(path);
            if (!(System.Math.Abs(area) > 0))
                throw new ArgumentException($"The {what} has no area.", nameof(points));
            if (area < 0)
                path.Reverse();
            // Start at the lowest-left vertex so equal polygons list their vertices alike.
            var start = 0;
            for (var i = 1; i < path.Count; i++)
                if (path[i].y < path[start].y || path[i].y == path[start].y && path[i].x < path[start].x)
                    start = i;
            if (start != 0)
                path = new PathD(path.Skip(start).Concat(path.Take(start)));
            // A simple polygon unions to itself: one path of the same area. A figure eight or a
            // bow tie does not, and a sheet edge that crosses itself is not a sheet.
            var union = Clipper.Union(new PathsD { path }, new PathsD(), FillRule.NonZero, Precision);
            var scale = System.Math.Pow(10, -Precision);
            if (union.Count != 1 || System.Math.Abs(Clipper.Area(union[0]) - System.Math.Abs(area)) > scale * (path.Count + 1) * Perimeter(path))
                throw new ArgumentException($"The {what} must be a simple polygon that does not cross itself.", nameof(points));
            return path;
        }

        private static double Perimeter(PathD path)
        {
            var length = 0.0;
            for (var i = 0; i < path.Count; i++)
            {
                var a = path[i];
                var b = path[(i + 1) % path.Count];
                length += System.Math.Sqrt((b.x - a.x) * (b.x - a.x) + (b.y - a.y) * (b.y - a.y));
            }
            return length;
        }
    }
}
