using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using OpenNest.Converters;
using OpenNest.Geometry;
using OpenNest.Math;

namespace OpenNest
{
    public static class Helper
    {
        /// <summary>
        /// Rounds a number down to the nearest factor.
        /// </summary>
        /// <param name="num"></param>
        /// <param name="factor"></param>
        /// <returns></returns>
        public static double RoundDownToNearest(double num, double factor)
        {
            return factor.IsEqualTo(0) ? num : System.Math.Floor(num / factor) * factor;
        }

        /// <summary>
        /// Rounds a number up to the nearest factor.
        /// </summary>
        /// <param name="num"></param>
        /// <param name="factor"></param>
        /// <returns></returns>
        public static double RoundUpToNearest(double num, double factor)
        {
            return factor.IsEqualTo(0) ? num : System.Math.Ceiling(num / factor) * factor;
        }

        /// <summary>
        /// Rounds a number to the nearest factor using midpoint rounding convention.
        /// </summary>
        /// <param name="num"></param>
        /// <param name="factor"></param>
        /// <returns></returns>
        public static double RoundToNearest(double num, double factor)
        {
            return factor.IsEqualTo(0) ? num : System.Math.Round(num / factor) * factor;
        }

        public static void Optimize(IList<Arc> arcs)
        {
            for (int i = 0; i < arcs.Count; ++i)
            {
                var arc = arcs[i];

                var coradialArcs = arcs.GetCoradialArs(arc, i);
                int index = 0;

                while (index < coradialArcs.Count)
                {
                    Arc arc2 = coradialArcs[index];
                    Arc joinArc;

                    if (!TryJoinArcs(arc, arc2, out joinArc))
                    {
                        index++;
                        continue;
                    }

                    coradialArcs.Remove(arc2);
                    arcs.Remove(arc2);

                    arc = joinArc;
                    index = 0;
                }

                arcs[i] = arc;
            }
        }

        public static void Optimize(IList<Line> lines)
        {
            for (int i = 0; i < lines.Count; ++i)
            {
                var line = lines[i];

                var collinearLines = lines.GetCollinearLines(line, i);
                var index = 0;

                while (index < collinearLines.Count)
                {
                    Line line2 = collinearLines[index];
                    Line joinLine;

                    if (!TryJoinLines(line, line2, out joinLine))
                    {
                        index++;
                        continue;
                    }

                    collinearLines.Remove(line2);
                    lines.Remove(line2);

                    line = joinLine;
                    index = 0;
                }

                lines[i] = line;
            }
        }

        public static bool TryJoinLines(Line line1, Line line2, out Line lineOut)
        {
            lineOut = null;

            if (line1 == line2)
                return false;

            if (!line1.IsCollinearTo(line2))
                return false;

            bool onPoint = false;

            if (line1.StartPoint == line2.StartPoint)
                onPoint = true;
            else if (line1.StartPoint == line2.EndPoint)
                onPoint = true;
            else if (line1.EndPoint == line2.StartPoint)
                onPoint = true;
            else if (line1.EndPoint == line2.EndPoint)
                onPoint = true;

            var t1 = line1.StartPoint.Y > line1.EndPoint.Y ? line1.StartPoint.Y : line1.EndPoint.Y;
            var t2 = line2.StartPoint.Y > line2.EndPoint.Y ? line2.StartPoint.Y : line2.EndPoint.Y;
            var b1 = line1.StartPoint.Y < line1.EndPoint.Y ? line1.StartPoint.Y : line1.EndPoint.Y;
            var b2 = line2.StartPoint.Y < line2.EndPoint.Y ? line2.StartPoint.Y : line2.EndPoint.Y;
            var l1 = line1.StartPoint.X < line1.EndPoint.X ? line1.StartPoint.X : line1.EndPoint.X;
            var l2 = line2.StartPoint.X < line2.EndPoint.X ? line2.StartPoint.X : line2.EndPoint.X;
            var r1 = line1.StartPoint.X > line1.EndPoint.X ? line1.StartPoint.X : line1.EndPoint.X;
            var r2 = line2.StartPoint.X > line2.EndPoint.X ? line2.StartPoint.X : line2.EndPoint.X;

            if (!onPoint)
            {
                if (t1 < b2 - Tolerance.Epsilon) return false;
                if (b1 > t2 + Tolerance.Epsilon) return false;
                if (l1 > r2 + Tolerance.Epsilon) return false;
                if (r1 < l2 - Tolerance.Epsilon) return false;
            }

            var l = l1 < l2 ? l1 : l2;
            var r = r1 > r2 ? r1 : r2;
            var t = t1 > t2 ? t1 : t2;
            var b = b1 < b2 ? b1 : b2;

            if (!line1.IsVertical() && line1.Slope() < 0)
                lineOut = new Line(new Vector(l, t), new Vector(r, b));
            else
                lineOut = new Line(new Vector(l, b), new Vector(r, t));

            return true;
        }

        public static bool TryJoinArcs(Arc arc1, Arc arc2, out Arc arcOut)
        {
            arcOut = null;

            if (arc1 == arc2)
                return false;

            if (arc1.Center != arc2.Center)
                return false;

            if (!arc1.Radius.IsEqualTo(arc2.Radius))
                return false;

            if (arc1.StartAngle > arc1.EndAngle)
                arc1.StartAngle -= Angle.TwoPI;

            if (arc2.StartAngle > arc2.EndAngle)
                arc2.StartAngle -= Angle.TwoPI;

            if (arc1.EndAngle < arc2.StartAngle || arc1.StartAngle > arc2.EndAngle)
                return false;

            var startAngle = arc1.StartAngle < arc2.StartAngle ? arc1.StartAngle : arc2.StartAngle;
            var endAngle = arc1.EndAngle > arc2.EndAngle ? arc1.EndAngle : arc2.EndAngle;

            if (startAngle < 0) startAngle += Angle.TwoPI;
            if (endAngle < 0) endAngle += Angle.TwoPI;

            arcOut = new Arc(arc1.Center, arc1.Radius, startAngle, endAngle);

            return true;
        }

        private static List<Line> GetCollinearLines(this IList<Line> lines, Line line, int startIndex)
        {
            var collinearLines = new List<Line>();

            Parallel.For(startIndex, lines.Count, index =>
            {
                var compareLine = lines[index];

                if (Object.ReferenceEquals(line, compareLine))
                    return;

                if (!line.IsCollinearTo(compareLine))
                    return;

                lock (collinearLines)
                {
                    collinearLines.Add(compareLine);
                }
            });

            return collinearLines;
        }

        private static List<Arc> GetCoradialArs(this IList<Arc> arcs, Arc arc, int startIndex)
        {
            var coradialArcs = new List<Arc>();

            Parallel.For(startIndex, arcs.Count, index =>
            {
                var compareArc = arcs[index];

                if (Object.ReferenceEquals(arc, compareArc))
                    return;

                if (!arc.IsCoradialTo(compareArc))
                    return;

                lock (coradialArcs)
                {
                    coradialArcs.Add(compareArc);
                }
            });

            return coradialArcs;
        }

        public static List<Shape> GetShapes(IEnumerable<Entity> entities)
        {
            var lines = new List<Line>();
            var arcs = new List<Arc>();
            var circles = new List<Circle>();
            var shapes = new List<Shape>();

            var entities2 = new Queue<Entity>(entities);

            while (entities2.Count > 0)
            {
                var entity = entities2.Dequeue();

                switch (entity.Type)
                {
                    case EntityType.Arc:
                        arcs.Add((Arc)entity);
                        break;

                    case EntityType.Circle:
                        circles.Add((Circle)entity);
                        break;

                    case EntityType.Line:
                        lines.Add((Line)entity);
                        break;

                    case EntityType.Shape:
                        var shape = (Shape)entity;
                        shape.Entities.ForEach(e => entities2.Enqueue(e));
                        break;

                    default:
                        Debug.Fail("Unhandled geometry type");
                        break;
                }
            }

            foreach (var circle in circles)
            {
                var shape = new Shape();
                shape.Entities.Add(circle);
                shape.UpdateBounds();
                shapes.Add(shape);
            }

            var entityList = new List<Entity>();

            entityList.AddRange(lines);
            entityList.AddRange(arcs);

            while (entityList.Count > 0)
            {
                var next = entityList[0];
                var shape = new Shape();
                shape.Entities.Add(next);

                entityList.RemoveAt(0);

                Vector startPoint = new Vector();
                Entity connected;

                switch (next.Type)
                {
                    case EntityType.Arc:
                        var arc = (Arc)next;
                        startPoint = arc.EndPoint();
                        break;

                    case EntityType.Line:
                        var line = (Line)next;
                        startPoint = line.EndPoint;
                        break;
                }

                while ((connected = GetConnected(startPoint, entityList)) != null)
                {
                    shape.Entities.Add(connected);
                    entityList.Remove(connected);

                    switch (connected.Type)
                    {
                        case EntityType.Arc:
                            var arc = (Arc)connected;
                            startPoint = arc.EndPoint();
                            break;

                        case EntityType.Line:
                            var line = (Line)connected;
                            startPoint = line.EndPoint;
                            break;
                    }
                }

                shape.UpdateBounds();
                shapes.Add(shape);
            }

            return shapes;
        }

        internal static Entity GetConnected(Vector pt, IEnumerable<Entity> geometry)
        {
            var tol = Math.Tolerance.ChainTolerance;

            foreach (var geo in geometry)
            {
                switch (geo.Type)
                {
                    case EntityType.Arc:
                        var arc = (Arc)geo;

                        if (arc.StartPoint().DistanceTo(pt) <= tol)
                            return arc;

                        if (arc.EndPoint().DistanceTo(pt) <= tol)
                        {
                            arc.Reverse();
                            return arc;
                        }

                        break;

                    case EntityType.Line:
                        var line = (Line)geo;

                        if (line.StartPoint.DistanceTo(pt) <= tol)
                            return line;

                        if (line.EndPoint.DistanceTo(pt) <= tol)
                        {
                            line.Reverse();
                            return line;
                        }
                        break;
                }
            }

            return null;
        }

        internal static bool Intersects(Arc arc1, Arc arc2, out List<Vector> pts)
        {
            var c1 = new Circle(arc1.Center, arc1.Radius);
            var c2 = new Circle(arc2.Center, arc2.Radius);

            if (!Intersects(c1, c2, out pts))
            {
                pts = new List<Vector>();
                return false;
            }

            pts = pts.Where(pt =>
                Angle.IsBetweenRad(arc1.Center.AngleTo(pt), arc1.StartAngle, arc1.EndAngle, arc1.IsReversed) &&
                Angle.IsBetweenRad(arc2.Center.AngleTo(pt), arc2.StartAngle, arc2.EndAngle, arc2.IsReversed))
                .ToList();

            return pts.Count > 0;
        }

        internal static bool Intersects(Arc arc, Circle circle, out List<Vector> pts)
        {
            var c1 = new Circle(arc.Center, arc.Radius);

            if (!Intersects(c1, circle, out pts))
            {
                pts = new List<Vector>();
                return false;
            }

            pts = pts.Where(pt => Angle.IsBetweenRad(
                arc.Center.AngleTo(pt),
                arc.StartAngle,
                arc.EndAngle,
                arc.IsReversed)).ToList();

            return pts.Count > 0;
        }

        internal static bool Intersects(Arc arc, Line line, out List<Vector> pts)
        {
            var c1 = new Circle(arc.Center, arc.Radius);

            if (!Intersects(c1, line, out pts))
            {
                pts = new List<Vector>();
                return false;
            }

            pts = pts.Where(pt => Angle.IsBetweenRad(
                arc.Center.AngleTo(pt),
                arc.StartAngle,
                arc.EndAngle,
                arc.IsReversed)).ToList();

            return pts.Count > 0;
        }

        internal static bool Intersects(Arc arc, Shape shape, out List<Vector> pts)
        {
            var pts2 = new List<Vector>();

            foreach (var geo in shape.Entities)
            {
                List<Vector> pts3;
                geo.Intersects(arc, out pts3);
                pts2.AddRange(pts3);
            }

            pts = pts2.Where(pt => Angle.IsBetweenRad(
                arc.Center.AngleTo(pt),
                arc.StartAngle,
                arc.EndAngle,
                arc.IsReversed)).ToList();

            return pts.Count > 0;
        }

        internal static bool Intersects(Arc arc, Polygon polygon, out List<Vector> pts)
        {
            var pts2 = new List<Vector>();
            var lines = polygon.ToLines();

            foreach (var line in lines)
            {
                List<Vector> pts3;
                Intersects(arc, line, out pts3);
                pts2.AddRange(pts3);
            }

            pts = pts2.Where(pt => Angle.IsBetweenRad(
                arc.Center.AngleTo(pt),
                arc.StartAngle,
                arc.EndAngle,
                arc.IsReversed)).ToList();

            return pts.Count > 0;
        }

        internal static bool Intersects(Circle circle1, Circle circle2, out List<Vector> pts)
        {
            var distance = circle1.Center.DistanceTo(circle2.Center);

            // check if circles are too far apart
            if (distance > circle1.Radius + circle2.Radius)
            {
                pts = new List<Vector>();
                return false;
            }

            // check if one circle contains the other
            if (distance < System.Math.Abs(circle1.Radius - circle2.Radius))
            {
                pts = new List<Vector>();
                return false;
            }

            var d = circle2.Center - circle1.Center;
            var a = (circle1.Radius * circle1.Radius - circle2.Radius * circle2.Radius + distance * distance) / (2.0 * distance);
            var h = System.Math.Sqrt(circle1.Radius * circle1.Radius - a * a);

            var pt = new Vector(
                circle1.Center.X + (a * d.X) / distance,
                circle1.Center.Y + (a * d.Y) / distance);

            var i1 = new Vector(
                pt.X + (h * d.Y) / distance,
                pt.Y - (h * d.X) / distance);

            var i2 = new Vector(
                pt.X - (h * d.Y) / distance,
                pt.Y + (h * d.X) / distance);

            pts = i1 != i2 ? new List<Vector> { i1, i2 } : new List<Vector> { i1 };

            return true;
        }

        internal static bool Intersects(Circle circle, Line line, out List<Vector> pts)
        {
            var d1 = line.EndPoint - line.StartPoint;
            var d2 = line.StartPoint - circle.Center;

            var a = d1.X * d1.X + d1.Y * d1.Y;
            var b = (d1.X * d2.X + d1.Y * d2.Y) * 2;
            var c = (d2.X * d2.X + d2.Y * d2.Y) - circle.Radius * circle.Radius;

            var det = b * b - 4 * a * c;

            if ((a <= Tolerance.Epsilon) || (det < 0))
            {
                pts = new List<Vector>();
                return false;
            }

            double t;
            pts = new List<Vector>();

            if (det.IsEqualTo(0))
            {
                t = -b / (2 * a);
                var pt1 = new Vector(line.StartPoint.X + t * d1.X, line.StartPoint.Y + t * d1.Y);

                if (line.BoundingBox.Contains(pt1))
                    pts.Add(pt1);

                return true;
            }

            t = (-b + System.Math.Sqrt(det)) / (2 * a);
            var pt2 = new Vector(line.StartPoint.X + t * d1.X, line.StartPoint.Y + t * d1.Y);

            if (line.BoundingBox.Contains(pt2))
                pts.Add(pt2);

            t = (-b - System.Math.Sqrt(det)) / (2 * a);
            var pt3 = new Vector(line.StartPoint.X + t * d1.X, line.StartPoint.Y + t * d1.Y);

            if (line.BoundingBox.Contains(pt3))
                pts.Add(pt3);

            return true;
        }

        internal static bool Intersects(Circle circle, Shape shape, out List<Vector> pts)
        {
            pts = new List<Vector>();

            foreach (var geo in shape.Entities)
            {
                List<Vector> pts3;
                geo.Intersects(circle, out pts3);
                pts.AddRange(pts3);
            }

            return pts.Count > 0;
        }

        internal static bool Intersects(Circle circle, Polygon polygon, out List<Vector> pts)
        {
            pts = new List<Vector>();
            var lines = polygon.ToLines();

            foreach (var line in lines)
            {
                List<Vector> pts3;
                Intersects(circle, line, out pts3);
                pts.AddRange(pts3);
            }

            return pts.Count > 0;
        }

        internal static bool Intersects(Line line1, Line line2, out Vector pt)
        {
            var a1 = line1.EndPoint.Y - line1.StartPoint.Y;
            var b1 = line1.StartPoint.X - line1.EndPoint.X;
            var c1 = a1 * line1.StartPoint.X + b1 * line1.StartPoint.Y;

            var a2 = line2.EndPoint.Y - line2.StartPoint.Y;
            var b2 = line2.StartPoint.X - line2.EndPoint.X;
            var c2 = a2 * line2.StartPoint.X + b2 * line2.StartPoint.Y;

            var d = a1 * b2 - a2 * b1;

            if (d.IsEqualTo(0.0))
            {
                pt = Vector.Zero;
                return false;
            }

            var x = (b2 * c1 - b1 * c2) / d;
            var y = (a1 * c2 - a2 * c1) / d;

            pt = new Vector(x, y);
            return line1.BoundingBox.Contains(pt) && line2.BoundingBox.Contains(pt);
        }

        internal static bool Intersects(Line line, Shape shape, out List<Vector> pts)
        {
            pts = new List<Vector>();

            foreach (var geo in shape.Entities)
            {
                List<Vector> pts3;
                geo.Intersects(line, out pts3);
                pts.AddRange(pts3);
            }

            return pts.Count > 0;
        }

        internal static bool Intersects(Line line, Polygon polygon, out List<Vector> pts)
        {
            pts = new List<Vector>();
            var lines = polygon.ToLines();

            foreach (var line2 in lines)
            {
                Vector pt;

                if (Intersects(line, line2, out pt))
                    pts.Add(pt);
            }

            return pts.Count > 0;
        }

        internal static bool Intersects(Shape shape1, Shape shape2, out List<Vector> pts)
        {
            pts = new List<Vector>();

            for (int i = 0; i < shape1.Entities.Count; i++)
            {
                var geo1 = shape1.Entities[i];

                for (int j = 0; j < shape2.Entities.Count; j++)
                {
                    List<Vector> pts2;
                    bool success = false;

                    var geo2 = shape2.Entities[j];

                    switch (geo2.Type)
                    {
                        case EntityType.Arc:
                            success = geo1.Intersects((Arc)geo2, out pts2);
                            break;

                        case EntityType.Circle:
                            success = geo1.Intersects((Circle)geo2, out pts2);
                            break;

                        case EntityType.Line:
                            success = geo1.Intersects((Line)geo2, out pts2);
                            break;

                        case EntityType.Shape:
                            success = geo1.Intersects((Shape)geo2, out pts2);
                            break;

                        case EntityType.Polygon:
                            success = geo1.Intersects((Polygon)geo2, out pts2);
                            break;

                        default:
                            continue;
                    }

                    if (success)
                        pts.AddRange(pts2);
                }
            }

            return pts.Count > 0;
        }

        internal static bool Intersects(Shape shape, Polygon polygon, out List<Vector> pts)
        {
            pts = new List<Vector>();

            var lines = polygon.ToLines();

            for (int i = 0; i < shape.Entities.Count; i++)
            {
                var geo = shape.Entities[i];

                for (int j = 0; j < lines.Count; j++)
                {
                    var line = lines[j];

                    List<Vector> pts2;

                    if (geo.Intersects(line, out pts2))
                        pts.AddRange(pts2);
                }
            }

            return pts.Count > 0;
        }

        internal static bool Intersects(Polygon polygon1, Polygon polygon2, out List<Vector> pts)
        {
            pts = new List<Vector>();

            var lines1 = polygon1.ToLines();
            var lines2 = polygon2.ToLines();

            for (int i = 0; i < lines1.Count; i++)
            {
                var line1 = lines1[i];

                for (int j = 0; j < lines2.Count; j++)
                {
                    var line2 = lines2[j];
                    Vector pt;

                    if (Intersects(line1, line2, out pt))
                        pts.Add(pt);
                }
            }

            return pts.Count > 0;
        }

        public static List<Line> GetPartLines(Part part, double chordTolerance = 0.001)
        {
            var entities = ConvertProgram.ToGeometry(part.Program);
            var shapes = GetShapes(entities.Where(e => e.Layer != SpecialLayers.Rapid));
            var lines = new List<Line>();

            foreach (var shape in shapes)
            {
                var polygon = shape.ToPolygonWithTolerance(chordTolerance);
                polygon.Offset(part.Location);
                lines.AddRange(polygon.ToLines());
            }

            return lines;
        }

        public static List<Line> GetPartLines(Part part, PushDirection facingDirection, double chordTolerance = 0.001)
        {
            var entities = ConvertProgram.ToGeometry(part.Program);
            var shapes = GetShapes(entities.Where(e => e.Layer != SpecialLayers.Rapid));
            var lines = new List<Line>();

            foreach (var shape in shapes)
            {
                var polygon = shape.ToPolygonWithTolerance(chordTolerance);
                polygon.Offset(part.Location);
                lines.AddRange(GetDirectionalLines(polygon, facingDirection));
            }

            return lines;
        }

        public static List<Line> GetOffsetPartLines(Part part, double spacing, double chordTolerance = 0.001)
        {
            var entities = ConvertProgram.ToGeometry(part.Program);
            var shapes = GetShapes(entities.Where(e => e.Layer != SpecialLayers.Rapid));
            var lines = new List<Line>();

            foreach (var shape in shapes)
            {
                // Add chord tolerance to compensate for inscribed polygon chords
                // being inside the actual offset arcs.
                var offsetEntity = shape.OffsetEntity(spacing + chordTolerance, OffsetSide.Left) as Shape;

                if (offsetEntity == null)
                    continue;

                var polygon = offsetEntity.ToPolygonWithTolerance(chordTolerance);
                polygon.RemoveSelfIntersections();
                polygon.Offset(part.Location);
                lines.AddRange(polygon.ToLines());
            }

            return lines;
        }

        public static List<Line> GetOffsetPartLines(Part part, double spacing, PushDirection facingDirection, double chordTolerance = 0.001)
        {
            var entities = ConvertProgram.ToGeometry(part.Program);
            var shapes = GetShapes(entities.Where(e => e.Layer != SpecialLayers.Rapid));
            var lines = new List<Line>();

            foreach (var shape in shapes)
            {
                var offsetEntity = shape.OffsetEntity(spacing + chordTolerance, OffsetSide.Left) as Shape;

                if (offsetEntity == null)
                    continue;

                var polygon = offsetEntity.ToPolygonWithTolerance(chordTolerance);
                polygon.RemoveSelfIntersections();
                polygon.Offset(part.Location);
                lines.AddRange(GetDirectionalLines(polygon, facingDirection));
            }

            return lines;
        }

        /// <summary>
        /// Returns only polygon edges whose outward normal faces the specified direction.
        /// </summary>
        private static List<Line> GetDirectionalLines(Polygon polygon, PushDirection facingDirection)
        {
            if (polygon.Vertices.Count < 3)
                return polygon.ToLines();

            var sign = polygon.RotationDirection() == RotationType.CCW ? 1.0 : -1.0;
            var lines = new List<Line>();
            var last = polygon.Vertices[0];

            for (int i = 1; i < polygon.Vertices.Count; i++)
            {
                var current = polygon.Vertices[i];
                var dx = current.X - last.X;
                var dy = current.Y - last.Y;

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
                    lines.Add(new Line(last, current));

                last = current;
            }

            return lines;
        }

        /// <summary>
        /// Finds the distance from a vertex to a line segment along a push axis.
        /// Returns double.MaxValue if the ray does not hit the segment.
        /// </summary>
        private static double RayEdgeDistance(Vector vertex, Line edge, PushDirection direction)
        {
            return RayEdgeDistance(
                vertex.X, vertex.Y,
                edge.pt1.X, edge.pt1.Y, edge.pt2.X, edge.pt2.Y,
                direction);
        }

        [System.Runtime.CompilerServices.MethodImpl(
            System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        private static double RayEdgeDistance(
            double vx, double vy,
            double p1x, double p1y, double p2x, double p2y,
            PushDirection direction)
        {
            switch (direction)
            {
                case PushDirection.Left:
                case PushDirection.Right:
                {
                    var dy = p2y - p1y;
                    if (dy > -Tolerance.Epsilon && dy < Tolerance.Epsilon)
                        return double.MaxValue;

                    var t = (vy - p1y) / dy;
                    if (t < -Tolerance.Epsilon || t > 1.0 + Tolerance.Epsilon)
                        return double.MaxValue;

                    var ix = p1x + t * (p2x - p1x);
                    var dist = direction == PushDirection.Left ? vx - ix : ix - vx;
                    if (dist > Tolerance.Epsilon) return dist;
                    if (dist >= -Tolerance.Epsilon) return 0;
                    return double.MaxValue;
                }

                case PushDirection.Down:
                case PushDirection.Up:
                {
                    var dx = p2x - p1x;
                    if (dx > -Tolerance.Epsilon && dx < Tolerance.Epsilon)
                        return double.MaxValue;

                    var t = (vx - p1x) / dx;
                    if (t < -Tolerance.Epsilon || t > 1.0 + Tolerance.Epsilon)
                        return double.MaxValue;

                    var iy = p1y + t * (p2y - p1y);
                    var dist = direction == PushDirection.Down ? vy - iy : iy - vy;
                    if (dist > Tolerance.Epsilon) return dist;
                    if (dist >= -Tolerance.Epsilon) return 0;
                    return double.MaxValue;
                }

                default:
                    return double.MaxValue;
            }
        }

        /// <summary>
        /// Computes the minimum translation distance along a push direction before
        /// any edge of movingLines contacts any edge of stationaryLines.
        /// Returns double.MaxValue if no collision path exists.
        /// </summary>
        public static double DirectionalDistance(List<Line> movingLines, List<Line> stationaryLines, PushDirection direction)
        {
            var minDist = double.MaxValue;

            // Case 1: Each moving vertex → each stationary edge
            for (int i = 0; i < movingLines.Count; i++)
            {
                var movingStart = movingLines[i].pt1;
                var movingEnd = movingLines[i].pt2;

                for (int j = 0; j < stationaryLines.Count; j++)
                {
                    var d = RayEdgeDistance(movingStart, stationaryLines[j], direction);
                    if (d < minDist) minDist = d;

                    d = RayEdgeDistance(movingEnd, stationaryLines[j], direction);
                    if (d < minDist) minDist = d;
                }
            }

            // Case 2: Each stationary vertex → each moving edge (opposite direction)
            var opposite = OppositeDirection(direction);

            for (int i = 0; i < stationaryLines.Count; i++)
            {
                var stationaryStart = stationaryLines[i].pt1;
                var stationaryEnd = stationaryLines[i].pt2;

                for (int j = 0; j < movingLines.Count; j++)
                {
                    var d = RayEdgeDistance(stationaryStart, movingLines[j], opposite);
                    if (d < minDist) minDist = d;

                    d = RayEdgeDistance(stationaryEnd, movingLines[j], opposite);
                    if (d < minDist) minDist = d;
                }
            }

            return minDist;
        }

        /// <summary>
        /// Computes the minimum directional distance with the moving lines translated
        /// by (movingDx, movingDy) without creating new Line objects.
        /// </summary>
        public static double DirectionalDistance(
            List<Line> movingLines, double movingDx, double movingDy,
            List<Line> stationaryLines, PushDirection direction)
        {
            var minDist = double.MaxValue;

            // Case 1: Each moving vertex → each stationary edge
            for (int i = 0; i < movingLines.Count; i++)
            {
                var ml = movingLines[i];
                var mx1 = ml.pt1.X + movingDx;
                var my1 = ml.pt1.Y + movingDy;
                var mx2 = ml.pt2.X + movingDx;
                var my2 = ml.pt2.Y + movingDy;

                for (int j = 0; j < stationaryLines.Count; j++)
                {
                    var se = stationaryLines[j];
                    var d = RayEdgeDistance(mx1, my1, se.pt1.X, se.pt1.Y, se.pt2.X, se.pt2.Y, direction);
                    if (d < minDist) minDist = d;

                    d = RayEdgeDistance(mx2, my2, se.pt1.X, se.pt1.Y, se.pt2.X, se.pt2.Y, direction);
                    if (d < minDist) minDist = d;
                }
            }

            // Case 2: Each stationary vertex → each moving edge (opposite direction)
            var opposite = OppositeDirection(direction);

            for (int i = 0; i < stationaryLines.Count; i++)
            {
                var sl = stationaryLines[i];

                for (int j = 0; j < movingLines.Count; j++)
                {
                    var me = movingLines[j];
                    var d = RayEdgeDistance(
                        sl.pt1.X, sl.pt1.Y,
                        me.pt1.X + movingDx, me.pt1.Y + movingDy,
                        me.pt2.X + movingDx, me.pt2.Y + movingDy,
                        opposite);
                    if (d < minDist) minDist = d;

                    d = RayEdgeDistance(
                        sl.pt2.X, sl.pt2.Y,
                        me.pt1.X + movingDx, me.pt1.Y + movingDy,
                        me.pt2.X + movingDx, me.pt2.Y + movingDy,
                        opposite);
                    if (d < minDist) minDist = d;
                }
            }

            return minDist;
        }

        /// <summary>
        /// Packs line segments into a flat double array [x1,y1,x2,y2, ...] for GPU transfer.
        /// </summary>
        public static double[] FlattenLines(List<Line> lines)
        {
            var result = new double[lines.Count * 4];
            for (int i = 0; i < lines.Count; i++)
            {
                var line = lines[i];
                result[i * 4] = line.pt1.X;
                result[i * 4 + 1] = line.pt1.Y;
                result[i * 4 + 2] = line.pt2.X;
                result[i * 4 + 3] = line.pt2.Y;
            }
            return result;
        }

        public static PushDirection OppositeDirection(PushDirection direction)
        {
            switch (direction)
            {
                case PushDirection.Left: return PushDirection.Right;
                case PushDirection.Right: return PushDirection.Left;
                case PushDirection.Up: return PushDirection.Down;
                case PushDirection.Down: return PushDirection.Up;
                default: return direction;
            }
        }

        public static double ClosestDistanceLeft(Box box, List<Box> boxes)
        {
            var closestDistance = double.MaxValue;

            for (int i = 0; i < boxes.Count; i++)
            {
                var compareBox = boxes[i];

                RelativePosition pos;

                if (!box.IsHorizontalTo(compareBox, out pos))
                    continue;

                if (pos != RelativePosition.Right)
                    continue;

                var distance = box.Left - compareBox.Right;

                if (distance < closestDistance)
                    closestDistance = distance;
            }

            return closestDistance == double.MaxValue ? double.NaN : closestDistance;
        }

        public static double ClosestDistanceRight(Box box, List<Box> boxes)
        {
            var closestDistance = double.MaxValue;

            for (int i = 0; i < boxes.Count; i++)
            {
                var compareBox = boxes[i];

                RelativePosition pos;

                if (!box.IsHorizontalTo(compareBox, out pos))
                    continue;

                if (pos != RelativePosition.Left)
                    continue;

                var distance = compareBox.Left - box.Right;

                if (distance < closestDistance)
                    closestDistance = distance;
            }

            return closestDistance == double.MaxValue ? double.NaN : closestDistance;
        }

        public static double ClosestDistanceUp(Box box, List<Box> boxes)
        {
            var closestDistance = double.MaxValue;

            for (int i = 0; i < boxes.Count; i++)
            {
                var compareBox = boxes[i];

                RelativePosition pos;

                if (!box.IsVerticalTo(compareBox, out pos))
                    continue;

                if (pos != RelativePosition.Bottom)
                    continue;

                var distance = compareBox.Bottom - box.Top;

                if (distance < closestDistance)
                    closestDistance = distance;
            }

            return closestDistance == double.MaxValue ? double.NaN : closestDistance;
        }

        public static double ClosestDistanceDown(Box box, List<Box> boxes)
        {
            var closestDistance = double.MaxValue;

            for (int i = 0; i < boxes.Count; i++)
            {
                var compareBox = boxes[i];

                RelativePosition pos;

                if (!box.IsVerticalTo(compareBox, out pos))
                    continue;

                if (pos != RelativePosition.Top)
                    continue;

                var distance = box.Bottom - compareBox.Top;

                if (distance < closestDistance)
                    closestDistance = distance;
            }

            return closestDistance == double.MaxValue ? double.NaN : closestDistance;
        }

        public static Box GetLargestBoxVertically(Vector pt, Box bounds, IEnumerable<Box> boxes)
        {
            var verticalBoxes = boxes.Where(b => !(b.Left > pt.X || b.Right < pt.X)).ToList();

            #region Find Top/Bottom Limits

            var top = double.MaxValue;
            var btm = double.MinValue;

            foreach (var box in verticalBoxes)
            {
                var boxBtm = box.Bottom;
                var boxTop = box.Top;

                if (boxBtm > pt.Y && boxBtm < top)
                    top = boxBtm;

                else if (box.Top < pt.Y && boxTop > btm)
                    btm = boxTop;
            }

            if (top == double.MaxValue)
            {
                if (bounds.Top > pt.Y)
                    top = bounds.Top;
                else return Box.Empty;
            }

            if (btm == double.MinValue)
            {
                if (bounds.Bottom < pt.Y)
                    btm = bounds.Bottom;
                else return Box.Empty;
            }

            #endregion

            var horizontalBoxes = boxes.Where(b => !(b.Bottom >= top || b.Top <= btm)).ToList();

            #region Find Left/Right Limits

            var lft = double.MinValue;
            var rgt = double.MaxValue;

            foreach (var box in horizontalBoxes)
            {
                var boxLft = box.Left;
                var boxRgt = box.Right;

                if (boxLft > pt.X && boxLft < rgt)
                    rgt = boxLft;

                else if (boxRgt < pt.X && boxRgt > lft)
                    lft = boxRgt;
            }

            if (rgt == double.MaxValue)
            {
                if (bounds.Right > pt.X)
                    rgt = bounds.Right;
                else return Box.Empty;
            }

            if (lft == double.MinValue)
            {
                if (bounds.Left < pt.X)
                    lft = bounds.Left;
                else return Box.Empty;
            }

            #endregion

            return new Box(lft, btm, rgt - lft, top - btm);
        }

        public static Box GetLargestBoxHorizontally(Vector pt, Box bounds, IEnumerable<Box> boxes)
        {
            var horizontalBoxes = boxes.Where(b => !(b.Bottom > pt.Y || b.Top < pt.Y)).ToList();

            #region Find Left/Right Limits

            var lft = double.MinValue;
            var rgt = double.MaxValue;

            foreach (var box in horizontalBoxes)
            {
                var boxLft = box.Left;
                var boxRgt = box.Right;

                if (boxLft > pt.X && boxLft < rgt)
                    rgt = boxLft;

                else if (boxRgt < pt.X && boxRgt > lft)
                    lft = boxRgt;
            }

            if (rgt == double.MaxValue)
            {
                if (bounds.Right > pt.X)
                    rgt = bounds.Right;
                else return Box.Empty;
            }

            if (lft == double.MinValue)
            {
                if (bounds.Left < pt.X)
                    lft = bounds.Left;
                else return Box.Empty;
            }

            #endregion

            var verticalBoxes = boxes.Where(b => !(b.Left >= rgt || b.Right <= lft)).ToList();

            #region Find Top/Bottom Limits

            var top = double.MaxValue;
            var btm = double.MinValue;

            foreach (var box in verticalBoxes)
            {
                var boxBtm = box.Bottom;
                var boxTop = box.Top;

                if (boxBtm > pt.Y && boxBtm < top)
                    top = boxBtm;

                else if (box.Top < pt.Y && boxTop > btm)
                    btm = boxTop;
            }

            if (top == double.MaxValue)
            {
                if (bounds.Top > pt.Y)
                    top = bounds.Top;
                else return Box.Empty;
            }

            if (btm == double.MinValue)
            {
                if (bounds.Bottom < pt.Y)
                    btm = bounds.Bottom;
                else return Box.Empty;
            }

            #endregion

            return new Box(lft, btm, rgt - lft, top - btm);
        }
    }
}
