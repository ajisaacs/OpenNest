using System;
using System.Collections.Generic;
using OpenNest.Math;

namespace OpenNest.Geometry
{
    public class Line : Entity
    {
        internal Vector pt1;
        internal Vector pt2;

        public Line()
        {
        }

        public Line(double x1, double y1, double x2, double y2)
            : this(new Vector(x1, y1), new Vector(x2, y2))
        {
        }

        public Line(Vector startPoint, Vector endPoint)
        {
            pt1 = startPoint;
            pt2 = endPoint;
            UpdateBounds();
        }

        /// <summary>
        /// Start point of the line.
        /// </summary>
        public Vector StartPoint
        {
            get { return pt1; }
            set
            {
                pt1 = value;
                UpdateBounds();
            }
        }

        /// <summary>
        /// Mid-point of the line.
        /// </summary>
        public Vector MidPoint
        {
            get
            {
                var x = (pt1.X + pt2.X) * 0.5;
                var y = (pt1.Y + pt2.Y) * 0.5;
                return new Vector(x, y);
            }
        }

        /// <summary>
        /// End point of the line.
        /// </summary>
        public Vector EndPoint
        {
            get { return pt2; }
            set
            {
                pt2 = value;
                UpdateBounds();
            }
        }

        /// <summary>
        /// Gets the point on the line that is perpendicular from the given point.
        /// </summary>
        /// <param name="pt"></param>
        /// <returns></returns>
        public Vector PointPerpendicularFrom(Vector pt)
        {
            var diff1 = pt - StartPoint;
            var diff2 = EndPoint - StartPoint;
            var dotProduct = diff1.X * diff2.X + diff1.Y * diff2.Y;
            var lengthSquared = diff2.X * diff2.X + diff2.Y * diff2.Y;
            var param = dotProduct / lengthSquared;

            if (param < 0)
                return StartPoint;
            else if (param > 1)
                return EndPoint;
            else
            {
                return new Vector(
                    StartPoint.X + param * diff2.X,
                    StartPoint.Y + param * diff2.Y);
            }
        }

        /// <summary>
        /// Returns true if the given line is parallel to this.
        /// </summary>
        /// <param name="line"></param>
        /// <returns></returns>
        public bool IsParallelTo(Line line)
        {
            bool line1Vertical = IsVertical();
            bool line2Vertical = line.IsVertical();

            if (line1Vertical)
                return line2Vertical;
            else if (line2Vertical)
                return false;

            return Slope().IsEqualTo(line.Slope());
        }

        /// <summary>
        /// Returns true if the given line is perpendicular to this.
        /// </summary>
        /// <param name="line"></param>
        /// <returns></returns>
        public bool IsPerpendicularTo(Line line)
        {
            bool line1Vertical = IsVertical();
            bool line2Vertical = line.IsVertical();

            if (line1Vertical)
                return line.IsHorizontal();
            else if (line.IsVertical())
                return IsHorizontal();

            return Slope().IsEqualTo(-1 / line.Slope());
        }

        /// <summary>
        /// Returns true if the given line is intersecting this.
        /// </summary>
        /// <param name="line"></param>
        /// <param name="pt">Point of intersection.</param>
        /// <returns></returns>
        public bool Intersects(Line line, out Vector pt)
        {
            var a1 = EndPoint.Y - StartPoint.Y;
            var b1 = StartPoint.X - EndPoint.X;
            var c1 = a1 * StartPoint.X + b1 * StartPoint.Y;

            var a2 = line.EndPoint.Y - line.StartPoint.Y;
            var b2 = line.StartPoint.X - line.EndPoint.X;
            var c2 = a2 * line.StartPoint.X + b2 * line.StartPoint.Y;

            var d = a1 * b2 - a2 * b1;

            if (d.IsEqualTo(0.0))
            {
                pt = new Vector();
                return false;
            }
            else
            {
                var x = (b2 * c1 - b1 * c2) / d;
                var y = (a1 * c2 - a2 * c1) / d;

                pt = new Vector(x, y);
                return boundingBox.Contains(pt) && line.boundingBox.Contains(pt);
            }
        }

        /// <summary>
        /// Returns true if this is vertical.
        /// </summary>
        /// <returns></returns>
        public bool IsVertical()
        {
            return pt1.X.IsEqualTo(pt2.X);
        }

        /// <summary>
        /// Returns true if this is horizontal.
        /// </summary>
        /// <returns></returns>
        public bool IsHorizontal()
        {
            return pt1.Y.IsEqualTo(pt2.Y);
        }

        /// <summary>
        /// Returns true if the given line is collinear to this.
        /// </summary>
        /// <param name="line"></param>
        /// <returns></returns>
        public bool IsCollinearTo(Line line)
        {
            if (IsVertical())
            {
                if (!line.IsVertical())
                    return false;

                return StartPoint.X.IsEqualTo(line.StartPoint.X);
            }
            else if (line.IsVertical())
                return false;

            if (!YIntercept().IsEqualTo(line.YIntercept()))
                return false;

            if (!Slope().IsEqualTo(line.Slope()))
                return false;

            return true;
        }

        /// <summary>
        /// Angle of the line from start point to end point.
        /// </summary>
        /// <returns></returns>
        public double Angle()
        {
            return StartPoint.AngleTo(EndPoint);
        }

        /// <summary>
        /// Returns the angle between the two lines in radians.
        /// </summary>
        /// <param name="line"></param>
        /// <returns></returns>
        public double AngleBetween(Line line)
        {
            var m1 = Slope();
            var m2 = line.Slope();
            return System.Math.Atan(System.Math.Abs((m2 - m1) / (1 + m2 * m1)));
        }

        /// <summary>
        /// Slope of the line.
        /// </summary>
        /// <returns></returns>
        public double Slope()
        {
            if (IsVertical())
                throw new DivideByZeroException();

            return (EndPoint.Y - StartPoint.Y) / (EndPoint.X - StartPoint.X);
        }

        /// <summary>
        /// Gets the y-axis intersection coordinate.
        /// </summary>
        /// <returns></returns>
        public double YIntercept()
        {
            return StartPoint.Y - Slope() * StartPoint.X;
        }

        /// <summary>
        /// Length of the line from start point to end point.
        /// </summary>
        public override double Length
        {
            get
            {
                var x = EndPoint.X - StartPoint.X;
                var y = EndPoint.Y - StartPoint.Y;
                return System.Math.Sqrt(x * x + y * y);
            }
        }

        /// <summary>
        /// Reversed the line.
        /// </summary>
        public override void Reverse()
        {
            Generic.Swap<Vector>(ref pt1, ref pt2);
        }

        /// <summary>
        /// Moves the start point to the given coordinates.
        /// </summary>
        /// <param name="x"></param>
        /// <param name="y"></param>
        public override void MoveTo(double x, double y)
        {
            var xoffset = pt1.X - x;
            var yoffset = pt1.Y - y;

            pt2.X += xoffset;
            pt2.Y += yoffset;
            pt1.X = x;
            pt1.Y = y;
            boundingBox.Offset(xoffset, yoffset);
        }

        /// <summary>
        /// Moves the start point to the given point.
        /// </summary>
        /// <param name="pt"></param>
        public override void MoveTo(Vector pt)
        {
            var offset = pt1 - pt;

            pt2 += offset;
            pt1 = pt;
            boundingBox.Offset(offset);
        }

        /// <summary>
        /// Offsets the line location by the given distances.
        /// </summary>
        /// <param name="x"></param>
        /// <param name="y"></param>
        public override void Offset(double x, double y)
        {
            pt2.X += x;
            pt2.Y += y;
            pt1.X += x;
            pt1.Y += y;
            boundingBox.Offset(x, y);
        }

        /// <summary>
        /// Offsets the line location by the given distances.
        /// </summary>
        /// <param name="voffset"></param>
        public override void Offset(Vector voffset)
        {
            pt1 += voffset;
            pt2 += voffset;
            boundingBox.Offset(voffset);
        }

        /// <summary>
        /// Scales the line from the zero point.
        /// </summary>
        /// <param name="factor"></param>
        public override void Scale(double factor)
        {
            pt1 *= factor;
            pt2 *= factor;
        }

        /// <summary>
        /// Scales the line from the origin.
        /// </summary>
        /// <param name="factor"></param>
        /// <param name="origin"></param>
        public override void Scale(double factor, Vector origin)
        {
            pt1 = (pt1 - origin) * factor + origin;
            pt2 = (pt2 - origin) * factor + origin;
        }

        /// <summary>
        /// Rotates the line from the zero point.
        /// </summary>
        /// <param name="angle"></param>
        public override void Rotate(double angle)
        {
            StartPoint = StartPoint.Rotate(angle);
            EndPoint = EndPoint.Rotate(angle);
        }

        /// <summary>
        /// Rotates the line from the origin.
        /// </summary>
        /// <param name="angle"></param>
        /// <param name="origin"></param>
        public override void Rotate(double angle, Vector origin)
        {
            StartPoint = StartPoint.Rotate(angle, origin);
            EndPoint = EndPoint.Rotate(angle, origin);
        }

        /// <summary>
        /// Updates the bounding box.
        /// </summary>
        public override sealed void UpdateBounds()
        {
            if (StartPoint.X < EndPoint.X)
            {
                boundingBox.X = StartPoint.X;
                boundingBox.Width = EndPoint.X - StartPoint.X;
            }
            else
            {
                boundingBox.X = EndPoint.X;
                boundingBox.Width = StartPoint.X - EndPoint.X;
            }

            if (StartPoint.Y < EndPoint.Y)
            {
                boundingBox.Y = StartPoint.Y;
                boundingBox.Length = EndPoint.Y - StartPoint.Y;
            }
            else
            {
                boundingBox.Y = EndPoint.Y;
                boundingBox.Length = StartPoint.Y - EndPoint.Y;
            }
        }

        public override Entity OffsetEntity(double distance, OffsetSide side)
        {
            var angle = OpenNest.Math.Angle.NormalizeRad(Angle() + OpenNest.Math.Angle.HalfPI);

            var x = System.Math.Cos(angle) * distance;
            var y = System.Math.Sin(angle) * distance;

            var pt = new Vector(x, y);

            return side == OffsetSide.Left
                ? new Line(StartPoint + pt, EndPoint + pt)
                : new Line(EndPoint + pt, StartPoint + pt);
        }

        public override Entity OffsetEntity(double distance, Vector pt)
        {
            var a = pt - StartPoint;
            var b = EndPoint - StartPoint;
            var c = a.DotProduct(b);
            var side = c < 0 ? OffsetSide.Left : OffsetSide.Right;

            return OffsetEntity(distance, side);
        }

        /// <summary>
        /// Splits the line at the given point, returning two sub-lines.
        /// Either half may be null if the split point coincides with an endpoint.
        /// </summary>
        /// <param name="point">The point at which to split the line.</param>
        /// <returns>A tuple of (first, second) sub-lines.</returns>
        public (Line first, Line second) SplitAt(Vector point)
        {
            var first = point.DistanceTo(StartPoint) < Tolerance.Epsilon
                ? null
                : new Line(StartPoint, point);

            var second = point.DistanceTo(EndPoint) < Tolerance.Epsilon
                ? null
                : new Line(point, EndPoint);

            return (first, second);
        }

        /// <summary>
        /// Gets the closest point on the line to the given point.
        /// </summary>
        /// <param name="pt"></param>
        /// <returns></returns>
        public override Vector ClosestPointTo(Vector pt)
        {
            var perpendicularPt = PointPerpendicularFrom(pt);

            if (BoundingBox.Contains(perpendicularPt))
                return perpendicularPt;
            else
                return pt.DistanceTo(StartPoint) <= pt.DistanceTo(EndPoint) ? StartPoint : EndPoint;
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
            Vector pt;
            return Intersects(line, out pt);
        }

        /// <summary>
        /// Returns true if the given line is intersecting this.
        /// </summary>
        /// <param name="line"></param>
        /// <param name="pts"></param>
        /// <returns></returns>
        public override bool Intersects(Line line, out List<Vector> pts)
        {
            Vector pt;
            var success = Helper.Intersects(this, line, out pt);
            pts = new List<Vector>(new[] { pt });
            return success;
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
            return Helper.Intersects(this, shape, out pts);
        }

        /// <summary>
        /// Returns true if the given shape is intersecting this.
        /// </summary>
        /// <param name="shape"></param>
        /// <param name="pts"></param>
        /// <returns></returns>
        public override bool Intersects(Shape shape, out List<Vector> pts)
        {
            return Helper.Intersects(this, shape, out pts);
        }

        /// <summary>
        /// Type of entity.
        /// </summary>
        public override EntityType Type
        {
            get { return EntityType.Line; }
        }
    }
}
