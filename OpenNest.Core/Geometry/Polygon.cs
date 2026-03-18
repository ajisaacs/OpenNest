using OpenNest.Math;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenNest.Geometry
{
    public class Polygon : Entity
    {
        public List<Vector> Vertices;

        public Polygon()
        {
            Vertices = new List<Vector>();
        }

        /// <summary>
        /// Closes the polygon if it's not already.
        /// </summary>
        public void Close()
        {
            if (Vertices.Count < 3)
                return;

            var first = Vertices.First();
            var last = Vertices.Last();

            if (first != last)
                Vertices.Add(first);
        }

        /// <summary>
        /// Returns true if the polygon is closed.
        /// </summary>
        /// <returns></returns>
        public bool IsClosed()
        {
            if (Vertices.Count < 3)
                return false;

            return (Vertices.First() == Vertices.Last());
        }

        /// <summary>
        /// Returns true if the polygon is self intersecting.
        /// </summary>
        /// <returns></returns>
        public bool IsComplex()
        {
            var lines = ToLines();

            for (int i = 0; i < lines.Count; ++i)
            {
                var line1 = lines[i];

                for (int j = i; j < lines.Count; ++j)
                {
                    var line2 = lines[j];

                    if (line1.Intersects(line2))
                        return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Area of the polygon.
        /// </summary>
        /// <returns>Returns the area or 0 if the polygon is NOT closed.</returns>
        public double Area()
        {
            if (Vertices.Count < 3)
                return 0.0;

            return System.Math.Abs(CalculateArea());
        }

        /// <summary>
        /// Distance around the polygon.
        /// </summary>
        /// <returns></returns>
        public double Perimeter()
        {
            if (Vertices.Count < 3)
                return 0.0;

            double sum = 0.0;

            var last = Vertices[0];

            for (int i = 1; i < Vertices.Count; ++i)
            {
                var current = Vertices[i];
                sum += last.DistanceTo(current);
                last = current;
            }

            return sum;
        }

        /// <summary>
        /// Gets the rotation direction of the polygon.
        /// </summary>
        /// <returns></returns>
        public RotationType RotationDirection()
        {
            if (Vertices.Count < 3)
                throw new Exception("Not enough points to determine direction. Must have at least 3 points.");

            return CalculateArea() > 0 ? RotationType.CCW : RotationType.CW;
        }

        /// <summary>
        /// Converts the polygon to a group of lines.
        /// </summary>
        /// <returns></returns>
        public List<Line> ToLines()
        {
            var list = new List<Line>();

            if (Vertices.Count < 2)
                return list;

            var last = Vertices[0];

            for (int i = 1; i < Vertices.Count; ++i)
            {
                var current = Vertices[i];
                list.Add(new Line(last, current));
                last = current;
            }

            return list;
        }

        /// <summary>
        /// Gets the area of the polygon.
        /// </summary>
        /// <returns>
        /// Returns the area of the polygon.
        /// * Positive number = counter-clockwise rotation
        /// * Negative number = clockwise rotation
        /// </returns>
        private double CalculateArea()
        {
            double xsum = 0;
            double ysum = 0;

            for (int i = 0; i < Vertices.Count - 1; ++i)
            {
                var current = Vertices[i];
                var next = Vertices[i + 1];

                xsum += current.X * next.Y;
                ysum += current.Y * next.X;
            }

            return (xsum - ysum) * 0.5;
        }

        /// <summary>
        /// Distance around the polygon.
        /// </summary>
        public override double Length
        {
            get { return Perimeter(); }
        }

        /// <summary>
        /// Reverses the rotation direction of the polygon.
        /// </summary>
        public override void Reverse()
        {
            Vertices.Reverse();
        }

        /// <summary>
        /// Moves the start point to the given coordinates.
        /// </summary>
        /// <param name="x"></param>
        /// <param name="y"></param>
        public override void MoveTo(double x, double y)
        {
            if (Vertices.Count == 0)
                return;

            var first = Vertices[0];
            var offset = new Vector(x - first.X, y - first.Y);

            Vertices.ForEach(vertex => vertex += offset);
            boundingBox.Offset(offset);
        }

        /// <summary>
        /// Moves the start point to the given point.
        /// </summary>
        /// <param name="pt"></param>
        public override void MoveTo(Vector pt)
        {
            if (Vertices.Count == 0)
                return;

            var first = Vertices[0];
            var offset = pt - first;

            Vertices.ForEach(vertex => vertex += offset);
            boundingBox.Offset(offset);
        }

        /// <summary>
        /// Offsets the location by the given distances.
        /// </summary>
        /// <param name="x"></param>
        /// <param name="y"></param>
        public override void Offset(double x, double y)
        {
            for (int i = 0; i < Vertices.Count; i++)
                Vertices[i] = Vertices[i].Offset(x, y);

            boundingBox.Offset(x, y);
        }

        /// <summary>
        /// Offsets the location by the given distances.
        /// </summary>
        /// <param name="voffset"></param>
        public override void Offset(Vector voffset)
        {
            for (int i = 0; i < Vertices.Count; i++)
                Vertices[i] = Vertices[i].Offset(voffset);

            boundingBox.Offset(voffset);
        }

        /// <summary>
        /// Scales the polygon from the zero point.
        /// </summary>
        /// <param name="factor"></param>
        public override void Scale(double factor)
        {
            for (int i = 0; i < Vertices.Count; i++)
                Vertices[i] *= factor;

            UpdateBounds();
        }

        /// <summary>
        /// Scales the polygon from the zero point.
        /// </summary>
        /// <param name="factor"></param>
        /// <param name="origin"></param>
        public override void Scale(double factor, Vector origin)
        {
            for (int i = 0; i < Vertices.Count; i++)
                Vertices[i] = (Vertices[i] - origin) * factor + origin;

            UpdateBounds();
        }

        /// <summary>
        /// Rotates the polygon from the zero point.
        /// </summary>
        /// <param name="angle"></param>
        public override void Rotate(double angle)
        {
            for (int i = 0; i < Vertices.Count; i++)
                Vertices[i] = Vertices[i].Rotate(angle);

            UpdateBounds();
        }

        /// <summary>
        /// Rotates the polygon from the origin.
        /// </summary>
        /// <param name="angle"></param>
        /// <param name="origin"></param>
        public override void Rotate(double angle, Vector origin)
        {
            for (int i = 0; i < Vertices.Count; i++)
                Vertices[i] = Vertices[i].Rotate(angle, origin);

            UpdateBounds();
        }

        /// <summary>
        /// Updates the bounding box.
        /// </summary>
        public override void UpdateBounds()
        {
            if (Vertices.Count == 0)
                return;

            var first = Vertices[0];
            var minX = first.X;
            var maxX = first.X;
            var minY = first.Y;
            var maxY = first.Y;

            for (int i = 1; i < Vertices.Count; ++i)
            {
                var vertex = Vertices[i];

                if (vertex.X < minX) minX = vertex.X;
                else if (vertex.X > maxX) maxX = vertex.X;

                if (vertex.Y < minY) minY = vertex.Y;
                else if (vertex.Y > maxY) maxY = vertex.Y;
            }

            boundingBox.X = minX;
            boundingBox.Y = minY;
            boundingBox.Width = maxX - minX;
            boundingBox.Length = maxY - minY;
        }

        public override Entity OffsetEntity(double distance, OffsetSide side)
        {
            throw new NotImplementedException();
        }

        public override Entity OffsetEntity(double distance, Vector pt)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Gets the closest point on the polygon to the given point.
        /// </summary>
        /// <param name="pt"></param>
        /// <returns></returns>
        public override Vector ClosestPointTo(Vector pt)
        {
            var lines = ToLines();

            if (lines.Count == 0)
                return Vector.Invalid;

            Vector closestPt = lines[0].ClosestPointTo(pt);
            double distance = closestPt.DistanceTo(pt);

            for (int i = 1; i < lines.Count; i++)
            {
                var line = lines[i];
                var closestPt2 = line.ClosestPointTo(pt);
                var distance2 = closestPt2.DistanceTo(pt);

                if (distance2 < distance)
                {
                    closestPt = closestPt2;
                    distance = distance2;
                }
            }

            return closestPt;
        }

        /// <summary>
        /// Returns true if the given arc is intersecting this.
        /// </summary>
        /// <param name="arc"></param>
        /// <returns></returns>
        public override bool Intersects(Arc arc)
        {
            List<Vector> pts;
            return Intersect.Intersects(arc, this, out pts);
        }

        /// <summary>
        /// Returns true if the given arc is intersecting this.
        /// </summary>
        /// <param name="arc"></param>
        /// <param name="pts"></param>
        /// <returns></returns>
        public override bool Intersects(Arc arc, out List<Vector> pts)
        {
            return Intersect.Intersects(arc, this, out pts);
        }

        /// <summary>
        /// Returns true if the given circle is intersecting this.
        /// </summary>
        /// <param name="circle"></param>
        /// <returns></returns>
        public override bool Intersects(Circle circle)
        {
            List<Vector> pts;
            return Intersect.Intersects(circle, this, out pts);
        }

        /// <summary>
        /// Returns true if the given circle is intersecting this.
        /// </summary>
        /// <param name="circle"></param>
        /// <param name="pts"></param>
        /// <returns></returns>
        public override bool Intersects(Circle circle, out List<Vector> pts)
        {
            return Intersect.Intersects(circle, this, out pts);
        }

        /// <summary>
        /// Returns true if the given line is intersecting this.
        /// </summary>
        /// <param name="line"></param>
        /// <returns></returns>
        public override bool Intersects(Line line)
        {
            List<Vector> pts;
            return Intersect.Intersects(line, this, out pts);
        }

        /// <summary>
        /// Returns true if the given line is intersecting this.
        /// </summary>
        /// <param name="line"></param>
        /// <param name="pts"></param>
        /// <returns></returns>
        public override bool Intersects(Line line, out List<Vector> pts)
        {
            return Intersect.Intersects(line, this, out pts);
        }

        /// <summary>
        /// Returns true if the given polygon is intersecting this.
        /// </summary>
        /// <param name="polygon"></param>
        /// <returns></returns>
        public override bool Intersects(Polygon polygon)
        {
            List<Vector> pts;
            return Intersect.Intersects(this, polygon, out pts);
        }

        /// <summary>
        /// Returns true if the given polygon is intersecting this.
        /// </summary>
        /// <param name="polygon"></param>
        /// <param name="pts"></param>
        /// <returns></returns>
        public override bool Intersects(Polygon polygon, out List<Vector> pts)
        {
            return Intersect.Intersects(this, polygon, out pts);
        }

        /// <summary>
        /// Returns true if the given shape is intersecting this.
        /// </summary>
        /// <param name="shape"></param>
        /// <returns></returns>
        public override bool Intersects(Shape shape)
        {
            List<Vector> pts;
            return Intersect.Intersects(shape, this, out pts);
        }

        /// <summary>
        /// Returns true if the given shape is intersecting this.
        /// </summary>
        /// <param name="shape"></param>
        /// <param name="pts"></param>
        /// <returns></returns>
        public override bool Intersects(Shape shape, out List<Vector> pts)
        {
            return Intersect.Intersects(shape, this, out pts);
        }

        /// <summary>
        /// Type of entity.
        /// </summary>
        public override EntityType Type
        {
            get { return EntityType.Polygon; }
        }

        /// <summary>
        /// Removes self-intersecting loops from the polygon by finding non-adjacent
        /// edge crossings and keeping the larger contour at each crossing.
        /// </summary>
        public void RemoveSelfIntersections()
        {
            if (!IsClosed() || Vertices.Count < 5)
                return;

            while (FindCrossing(out var edgeI, out var edgeJ, out var pt))
            {
                Vertices = SplitAtCrossing(edgeI, edgeJ, pt);
            }
        }

        private bool FindCrossing(out int edgeI, out int edgeJ, out Vector pt)
        {
            var n = Vertices.Count - 1;

            // Pre-calculate edge bounding boxes to speed up intersection checks.
            var edgeBounds = new (double minX, double maxX, double minY, double maxY)[n];
            for (var i = 0; i < n; i++)
            {
                var v1 = Vertices[i];
                var v2 = Vertices[i + 1];
                edgeBounds[i] = (
                    System.Math.Min(v1.X, v2.X) - Tolerance.Epsilon,
                    System.Math.Max(v1.X, v2.X) + Tolerance.Epsilon,
                    System.Math.Min(v1.Y, v2.Y) - Tolerance.Epsilon,
                    System.Math.Max(v1.Y, v2.Y) + Tolerance.Epsilon
                );
            }

            for (var i = 0; i < n; i++)
            {
                var bi = edgeBounds[i];
                for (var j = i + 2; j < n; j++)
                {
                    if (i == 0 && j == n - 1)
                        continue;

                    var bj = edgeBounds[j];

                    // Prune with bounding box check.
                    if (bi.maxX < bj.minX || bj.maxX < bi.minX ||
                        bi.maxY < bj.minY || bj.maxY < bi.minY)
                    {
                        continue;
                    }

                    if (SegmentsIntersect(Vertices[i], Vertices[i + 1], Vertices[j], Vertices[j + 1], out pt))
                    {
                        edgeI = i;
                        edgeJ = j;
                        return true;
                    }
                }
            }

            edgeI = edgeJ = -1;
            pt = Vector.Zero;
            return false;
        }

        private List<Vector> SplitAtCrossing(int edgeI, int edgeJ, Vector pt)
        {
            var n = Vertices.Count - 1;

            var loopA = Vertices.GetRange(0, edgeI + 1);
            loopA.Add(pt);
            loopA.AddRange(Vertices.GetRange(edgeJ + 1, n - edgeJ - 1));
            loopA.Add(loopA[0]);

            var loopB = new List<Vector> { pt };
            loopB.AddRange(Vertices.GetRange(edgeI + 1, edgeJ - edgeI));
            loopB.Add(pt);

            var areaA = System.Math.Abs(CalculateArea(loopA));
            var areaB = System.Math.Abs(CalculateArea(loopB));

            return areaA >= areaB ? loopA : loopB;
        }

        private static bool SegmentsIntersect(Vector a1, Vector a2, Vector b1, Vector b2, out Vector pt)
        {
            var da = a2 - a1;
            var db = b2 - b1;
            var cross = da.X * db.Y - da.Y * db.X;

            if (cross.IsEqualTo(0.0))
            {
                pt = Vector.Zero;
                return false;
            }

            var dc = b1 - a1;
            var t = (dc.X * db.Y - dc.Y * db.X) / cross;
            var u = (dc.X * da.Y - dc.Y * da.X) / cross;

            if (t > Tolerance.Epsilon && t < 1.0 - Tolerance.Epsilon &&
                u > Tolerance.Epsilon && u < 1.0 - Tolerance.Epsilon)
            {
                pt = new Vector(a1.X + t * da.X, a1.Y + t * da.Y);
                return true;
            }

            pt = Vector.Zero;
            return false;
        }

        private static double CalculateArea(List<Vector> vertices)
        {
            double xsum = 0;
            double ysum = 0;

            for (int i = 0; i < vertices.Count - 1; i++)
            {
                var current = vertices[i];
                var next = vertices[i + 1];

                xsum += current.X * next.Y;
                ysum += current.Y * next.X;
            }

            return (xsum - ysum) * 0.5;
        }

        internal void Cleanup()
        {
            for (int i = Vertices.Count - 1; i > 0; i--)
            {
                var vertex = Vertices[i];
                var nextVertex = Vertices[i - 1];

                if (vertex == nextVertex)
                    Vertices.RemoveAt(i);
            }
        }

        public BoundingRectangleResult FindBestRotation()
        {
            return RotatingCalipers.MinimumBoundingRectangle(Vertices);
        }

        public BoundingRectangleResult FindBestRotation(double startAngle, double endAngle)
        {
            var hull = ConvexHull.Compute(Vertices);
            return RotatingCalipers.MinimumBoundingRectangle(hull, startAngle, endAngle);
        }

        public bool ContainsPoint(Vector pt)
        {
            var n = IsClosed() ? Vertices.Count - 1 : Vertices.Count;

            if (n < 3)
                return false;

            var inside = false;

            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                var vi = Vertices[i];
                var vj = Vertices[j];

                if ((vi.Y > pt.Y) != (vj.Y > pt.Y) &&
                    pt.X < (vj.X - vi.X) * (pt.Y - vi.Y) / (vj.Y - vi.Y) + vi.X)
                {
                    inside = !inside;
                }
            }

            return inside;
        }
    }
}
