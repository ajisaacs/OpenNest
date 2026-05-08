using OpenNest.Math;
using System;
using System.Collections.Generic;

namespace OpenNest.Geometry
{
    public class Arc : Entity
    {
        private double radius;
        private double startAngle;
        private double endAngle;
        private Vector center;
        private bool reversed;

        public Arc()
        {
        }

        public Arc(double x, double y, double r, double a1, double a2, bool reversed = false)
            : this(new Vector(x, y), r, a1, a2, reversed)
        {
        }

        public Arc(Vector center, double radius, double startAngle, double endAngle, bool reversed = false)
        {
            this.center = center;
            this.radius = radius;
            this.startAngle = startAngle;
            this.endAngle = endAngle;
            this.reversed = reversed;
            UpdateBounds();
        }

        /// <summary>
        /// Center point.
        /// </summary>
        public Vector Center
        {
            get { return center; }
            set
            {
                var offset = value - center;
                boundingBox.Offset(offset);
                center = value;
            }
        }

        /// <summary>
        /// Arc radius.
        /// </summary>
        public double Radius
        {
            get { return radius; }
            set
            {
                radius = value;
                UpdateBounds();
            }
        }

        /// <summary>
        /// Arc radius * 2. Value NOT stored.
        /// </summary>
        public double Diameter
        {
            get { return Radius * 2.0; }
            set { Radius = value / 2.0; }
        }

        /// <summary>
        /// Start angle in radians.
        /// </summary>
        public double StartAngle
        {
            get { return startAngle; }
            set
            {
                startAngle = Angle.NormalizeRad(value);
                UpdateBounds();
            }
        }

        /// <summary>
        /// End angle in radians.
        /// </summary>
        public double EndAngle
        {
            get { return endAngle; }
            set
            {
                endAngle = Angle.NormalizeRad(value);
                UpdateBounds();
            }
        }

        public bool IsFullCircle() =>
            SweepAngle() >= Angle.TwoPI - Tolerance.Epsilon;

        /// <summary>
        /// Angle in radians between start and end angles.
        /// </summary>
        /// <returns></returns>
        public double SweepAngle()
        {
            var startAngle = StartAngle;
            var endAngle = EndAngle;

            if (IsReversed)
                Generic.Swap(ref startAngle, ref endAngle);

            if (startAngle > endAngle)
                startAngle -= Angle.TwoPI;

            return endAngle - startAngle;
        }

        /// <summary>
        /// Gets or sets if the arc direction is reversed (clockwise).
        /// </summary>
        public bool IsReversed
        {
            get { return reversed; }
            set
            {
                if (reversed != value)
                    Reverse();
            }
        }

        public RotationType Rotation
        {
            get { return IsReversed ? RotationType.CW : RotationType.CCW; }
            set
            {
                IsReversed = (value == RotationType.CW);
            }
        }

        /// <summary>
        /// Start point of the arc.
        /// </summary>
        /// <returns></returns>
        public Vector StartPoint()
        {
            return new Vector(
                Center.X + Radius * System.Math.Cos(StartAngle),
                Center.Y + Radius * System.Math.Sin(StartAngle));
        }

        /// <summary>
        /// End point of the arc.
        /// </summary>
        /// <returns></returns>
        public Vector EndPoint()
        {
            return new Vector(
                Center.X + Radius * System.Math.Cos(EndAngle),
                Center.Y + Radius * System.Math.Sin(EndAngle));
        }

        /// <summary>
        /// Mid point of the arc (point at the angle midway between start and end).
        /// </summary>
        public Vector MidPoint()
        {
            var midAngle = StartAngle + (IsReversed ? -SweepAngle() / 2 : SweepAngle() / 2);
            return new Vector(
                Center.X + Radius * System.Math.Cos(midAngle),
                Center.Y + Radius * System.Math.Sin(midAngle));
        }

        /// <summary>
        /// Splits the arc at the given point, returning two sub-arcs.
        /// Either half may be null if the split point coincides with an endpoint.
        /// </summary>
        /// <param name="point">The point at which to split the arc.</param>
        /// <returns>A tuple of (first, second) sub-arcs.</returns>
        public (Arc first, Arc second) SplitAt(Vector point)
        {
            if (point.DistanceTo(StartPoint()) < Tolerance.Epsilon)
                return (null, new Arc(Center, Radius, StartAngle, EndAngle, IsReversed));

            if (point.DistanceTo(EndPoint()) < Tolerance.Epsilon)
                return (new Arc(Center, Radius, StartAngle, EndAngle, IsReversed), null);

            var splitAngle = Angle.NormalizeRad(Center.AngleTo(point));

            var firstArc = new Arc(Center, Radius, StartAngle, splitAngle, IsReversed);
            var secondArc = new Arc(Center, Radius, splitAngle, EndAngle, IsReversed);

            return (firstArc, secondArc);
        }

        /// <summary>
        /// Returns true if the given arc has the same center point and radius as this.
        /// </summary>
        /// <param name="arc"></param>
        /// <returns></returns>
        public bool IsCoradialTo(Arc arc)
        {
            return center == arc.Center && Radius.IsEqualTo(arc.Radius);
        }

        /// <summary>
        /// Returns true if the given arc has the same radius as this.
        /// </summary>
        /// <param name="arc"></param>
        /// <returns></returns>
        public bool IsConcentricTo(Arc arc)
        {
            return center == arc.center;
        }

        /// <summary>
        /// Returns true if the given circle has the same radius as this.
        /// </summary>
        /// <param name="circle"></param>
        /// <returns></returns>
        public bool IsConcentricTo(Circle circle)
        {
            return center == circle.Center;
        }

        /// <summary>
        /// Returns the minimum number of segments needed so that the chord-to-arc
        /// deviation (sagitta) does not exceed the given tolerance.
        /// </summary>
        public int SegmentsForTolerance(double tolerance)
        {
            if (tolerance >= Radius)
                return 1;

            var maxAngle = 2.0 * System.Math.Acos(1.0 - tolerance / Radius);
            return System.Math.Max(1, (int)System.Math.Ceiling(System.Math.Abs(SweepAngle()) / maxAngle));
        }

        /// <summary>
        /// Converts the arc to a group of points.
        /// </summary>
        /// <param name="segments">Number of parts to divide the arc into.</param>
        /// <returns></returns>
        public List<Vector> ToPoints(int segments = 1000, bool circumscribe = false)
        {
            var points = new List<Vector>();
            var stepAngle = reversed
                ? -SweepAngle() / segments
                : SweepAngle() / segments;

            var r = circumscribe && segments > 0
                ? Radius / System.Math.Cos(System.Math.Abs(stepAngle) / 2.0)
                : Radius;

            for (int i = 0; i <= segments; ++i)
            {
                var angle = stepAngle * i + StartAngle;

                points.Add(new Vector(
                    System.Math.Cos(angle) * r + Center.X,
                    System.Math.Sin(angle) * r + Center.Y));
            }

            return points;
        }

        /// <summary>
        /// Linear distance of the arc.
        /// </summary>
        public override double Length
        {
            get { return Diameter * System.Math.PI * SweepAngle() / Angle.TwoPI; }
        }

        public override Entity Clone()
        {
            var copy = new Arc(center, radius, startAngle, endAngle, reversed);
            CopyBaseTo(copy);
            return copy;
        }

        /// <summary>
        /// Reverses the rotation direction.
        /// </summary>
        public override void Reverse()
        {
            reversed = !reversed;
            Generic.Swap(ref startAngle, ref endAngle);
        }

        /// <summary>
        /// Moves the center point to the given coordinates.
        /// </summary>
        /// <param name="x">The x-coordinate</param>
        /// <param name="y">The y-coordinate</param>
        public override void MoveTo(double x, double y)
        {
            Center = new Vector(x, y);
        }

        /// <summary>
        /// Moves the center point to the given point.
        /// </summary>
        /// <param name="pt">The new center point location.</param>
        public override void MoveTo(Vector pt)
        {
            Center = pt;
        }

        /// <summary>
        /// Offsets the center point by the given distances.
        /// </summary>
        /// <param name="x">The x-axis offset distance.</param>
        /// <param name="y">The y-axis offset distance.</param>
        public override void Offset(double x, double y)
        {
            Center = new Vector(Center.X + x, Center.Y + y);
        }

        /// <summary>
        /// Offsets the center point by the given distances.
        /// </summary>
        /// <param name="voffset"></param>
        public override void Offset(Vector voffset)
        {
            Center += voffset;
        }

        /// <summary>
        /// Scales the arc from the zero point.
        /// </summary>
        /// <param name="factor"></param>
        public override void Scale(double factor)
        {
            center *= factor;
            radius *= factor;
            UpdateBounds();
        }

        /// <summary>
        /// Scales the arc from the origin.
        /// </summary>
        /// <param name="factor"></param>
        /// <param name="origin"></param>
        public override void Scale(double factor, Vector origin)
        {
            center = center.Scale(factor, origin);
            radius *= factor;
            UpdateBounds();
        }

        /// <summary>
        /// Rotates the arc from the zero point.
        /// </summary>
        /// <param name="angle"></param>
        public override void Rotate(double angle)
        {
            startAngle += angle;
            endAngle += angle;
            center = center.Rotate(angle);
            UpdateBounds();
        }

        /// <summary>
        /// Rotates the arc from the origin.
        /// </summary>
        /// <param name="angle"></param>
        /// <param name="origin"></param>
        public override void Rotate(double angle, Vector origin)
        {
            startAngle += angle;
            endAngle += angle;
            center = center.Rotate(angle, origin);
            UpdateBounds();
        }

        /// <summary>
        /// Updates the bounding box.
        /// </summary>
        public override void UpdateBounds()
        {
            var startpt = StartPoint();
            var endpt = EndPoint();

            double minX;
            double minY;
            double maxX;
            double maxY;

            if (startpt.X < endpt.X)
            {
                minX = startpt.X;
                maxX = endpt.X;
            }
            else
            {
                minX = endpt.X;
                maxX = startpt.X;
            }

            if (startpt.Y < endpt.Y)
            {
                minY = startpt.Y;
                maxY = endpt.Y;
            }
            else
            {
                minY = endpt.Y;
                maxY = startpt.Y;
            }

            var sweep = SweepAngle();
            if (sweep > Tolerance.Epsilon)
            {
                var angle1 = StartAngle;
                var angle2 = EndAngle;

                if (IsReversed)
                    Generic.Swap(ref angle1, ref angle2);

                if (Angle.IsBetweenRad(Angle.HalfPI, angle1, angle2))
                    maxY = Center.Y + Radius;

                if (Angle.IsBetweenRad(System.Math.PI, angle1, angle2))
                    minX = Center.X - Radius;

                const double oneHalfPI = System.Math.PI * 1.5;

                if (Angle.IsBetweenRad(oneHalfPI, angle1, angle2))
                    minY = Center.Y - Radius;

                if (Angle.IsBetweenRad(Angle.TwoPI, angle1, angle2))
                    maxX = Center.X + Radius;
            }

            boundingBox.X = minX;
            boundingBox.Y = minY;
            boundingBox.Length = maxX - minX;
            boundingBox.Width = maxY - minY;
        }

        public override Entity OffsetEntity(double distance, OffsetSide side)
        {
            if (side == OffsetSide.Left && reversed)
            {
                return new Arc(center, radius + distance, startAngle, endAngle, reversed);
            }
            else
            {
                if (distance >= radius)
                    return null;

                return new Arc(center, radius - distance, startAngle, endAngle, reversed);
            }
        }

        public override Entity OffsetEntity(double distance, Vector pt)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Gets the closest point on the arc to the given point.
        /// </summary>
        /// <param name="pt"></param>
        /// <returns></returns>
        public override Vector ClosestPointTo(Vector pt)
        {
            var angle = Center.AngleTo(pt);

            if (Angle.IsBetweenRad(angle, StartAngle, EndAngle, IsReversed))
            {
                return new Vector(
                    System.Math.Cos(angle) * Radius + Center.X,
                    System.Math.Sin(angle) * Radius + Center.Y);
            }
            else
            {
                var sp = StartPoint();
                var ep = EndPoint();

                return pt.DistanceTo(sp) <= pt.DistanceTo(ep) ? sp : ep;
            }
        }

        /// <summary>
        /// Returns true if the given arc is intersecting this.
        /// </summary>
        /// <param name="arc"></param>
        /// <returns></returns>
        public override bool Intersects(Arc arc)
        {
            List<Vector> pts;
            return Intersect.Intersects(this, arc, out pts);
        }

        /// <summary>
        /// Returns true if the given arc is intersecting this.
        /// </summary>
        /// <param name="arc"></param>
        /// <param name="pts">Points of intersection.</param>
        /// <returns></returns>
        public override bool Intersects(Arc arc, out List<Vector> pts)
        {
            return Intersect.Intersects(this, arc, out pts); ;
        }

        /// <summary>
        /// Returns true if the given circle is intersecting this.
        /// </summary>
        /// <param name="circle"></param>
        /// <returns></returns>
        public override bool Intersects(Circle circle)
        {
            List<Vector> pts;
            return Intersect.Intersects(this, circle, out pts);
        }

        /// <summary>
        /// Returns true if the given circle is intersecting this.
        /// </summary>
        /// <param name="circle"></param>
        /// <param name="pts">Points of intersection.</param>
        /// <returns></returns>
        public override bool Intersects(Circle circle, out List<Vector> pts)
        {
            return Intersect.Intersects(this, circle, out pts);
        }

        /// <summary>
        /// Returns true if the given line is intersecting this.
        /// </summary>
        /// <param name="line"></param>
        /// <returns></returns>
        public override bool Intersects(Line line)
        {
            List<Vector> pts;
            return Intersect.Intersects(this, line, out pts);
        }

        /// <summary>
        /// Returns true if the given line is intersecting this.
        /// </summary>
        /// <param name="line"></param>
        /// <param name="pts">Points of intersection.</param>
        /// <returns></returns>
        public override bool Intersects(Line line, out List<Vector> pts)
        {
            return Intersect.Intersects(this, line, out pts);
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
        /// <param name="pts">Points of intersection.</param>
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
            return Intersect.Intersects(this, shape, out pts);
        }

        /// <summary>
        /// Returns true if the given shape is intersecting this.
        /// </summary>
        /// <param name="shape"></param>
        /// <param name="pts">Points of intersection.</param>
        /// <returns></returns>
        public override bool Intersects(Shape shape, out List<Vector> pts)
        {
            return Intersect.Intersects(this, shape, out pts);
        }

        /// <summary>
        /// Type of entity.
        /// </summary>
        public override EntityType Type
        {
            get { return EntityType.Arc; }
        }
    }
}
