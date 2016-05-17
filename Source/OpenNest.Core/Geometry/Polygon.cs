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

            return Math.Abs(CalculateArea());
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
            boundingBox.Height = maxY - minY;
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
            return Helper.Intersects(arc, this, out pts);
        }

        /// <summary>
        /// Returns true if the given arc is intersecting this.
        /// </summary>
        /// <param name="arc"></param>
        /// <param name="pts"></param>
        /// <returns></returns>
        public override bool Intersects(Arc arc, out List<Vector> pts)
        {
            return Helper.Intersects(arc, this, out pts);
        }

        /// <summary>
        /// Returns true if the given circle is intersecting this.
        /// </summary>
        /// <param name="circle"></param>
        /// <returns></returns>
        public override bool Intersects(Circle circle)
        {
            List<Vector> pts;
            return Helper.Intersects(circle, this, out pts);
        }

        /// <summary>
        /// Returns true if the given circle is intersecting this.
        /// </summary>
        /// <param name="circle"></param>
        /// <param name="pts"></param>
        /// <returns></returns>
        public override bool Intersects(Circle circle, out List<Vector> pts)
        {
            return Helper.Intersects(circle, this, out pts);
        }

        /// <summary>
        /// Returns true if the given line is intersecting this.
        /// </summary>
        /// <param name="line"></param>
        /// <returns></returns>
        public override bool Intersects(Line line)
        {
            List<Vector> pts;
            return Helper.Intersects(line, this, out pts);
        }

        /// <summary>
        /// Returns true if the given line is intersecting this.
        /// </summary>
        /// <param name="line"></param>
        /// <param name="pts"></param>
        /// <returns></returns>
        public override bool Intersects(Line line, out List<Vector> pts)
        {
            return Helper.Intersects(line, this, out pts);
        }

        /// <summary>
        /// Returns true if the given polygon is intersecting this.
        /// </summary>
        /// <param name="polygon"></param>
        /// <returns></returns>
        public override bool Intersects(Polygon polygon)
        {
            List<Vector> pts;
            return Helper.Intersects(this, polygon, out pts);
        }

        /// <summary>
        /// Returns true if the given polygon is intersecting this.
        /// </summary>
        /// <param name="polygon"></param>
        /// <param name="pts"></param>
        /// <returns></returns>
        public override bool Intersects(Polygon polygon, out List<Vector> pts)
        {
            return Helper.Intersects(this, polygon, out pts);
        }

        /// <summary>
        /// Returns true if the given shape is intersecting this.
        /// </summary>
        /// <param name="shape"></param>
        /// <returns></returns>
        public override bool Intersects(Shape shape)
        {
            List<Vector> pts;
            return Helper.Intersects(shape, this, out pts);
        }

        /// <summary>
        /// Returns true if the given shape is intersecting this.
        /// </summary>
        /// <param name="shape"></param>
        /// <param name="pts"></param>
        /// <returns></returns>
        public override bool Intersects(Shape shape, out List<Vector> pts)
        {
            return Helper.Intersects(shape, this, out pts);
        }

        /// <summary>
        /// Type of entity.
        /// </summary>
        public override EntityType Type
        {
            get { return EntityType.Polygon; }
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

        public double FindBestRotation(double stepAngle)
        {
            var entities = new List<Entity>(ToLines());
            return entities.FindBestRotation(stepAngle);
        }

        public double FindBestRotation(double stepAngle, double startAngle, double endAngle)
        {
            var entities = new List<Entity>(ToLines());
            return entities.FindBestRotation(stepAngle, startAngle, endAngle);
        }
    }
}
