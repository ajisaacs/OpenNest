using System.Collections.Generic;
using System.Linq;
using OpenNest.Math;

namespace OpenNest.Geometry
{
    public static class Intersect
    {
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
    }
}
