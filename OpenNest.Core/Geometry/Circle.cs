using System;
using System.Collections.Generic;
using OpenNest.Math;

namespace OpenNest.Geometry
{
    public class Circle : Entity
    {
        private Vector center;
        private double radius;

        public Circle()
        {
        }

        public Circle(double x, double y, double radius)
            : this(new Vector(x, y), radius)
        {
        }

        public Circle(Vector center, double radius)
        {
            this.center = center;
            this.radius = radius;
            this.Rotation = RotationType.CCW;
            UpdateBounds();
        }

        /// <summary>
        /// Creates a circle from two points.
        /// </summary>
        /// <param name="pt1"></param>
        /// <param name="pt2"></param>
        /// <returns></returns>
        public static Circle CreateFrom2Points(Vector pt1, Vector pt2)
        {
            var line = new Line(pt1, pt2);
            return new Circle(line.MidPoint, line.Length * 0.5);
        }

        /// <summary>
        /// Center point of the circle.
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
        /// Radius of the circle.
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
        /// Radius * 2. Value NOT stored.
        /// </summary>
        public double Diameter
        {
            get { return Radius * 2.0; }
            set { Radius = value / 2.0; }
        }

        /// <summary>
        /// Rotation direction.
        /// </summary>
        public RotationType Rotation { get; set; }

        /// <summary>
        /// Area of the circle.
        /// </summary>
        /// <returns></returns>
        public double Area()
        {
            return System.Math.PI * Radius * Radius;
        }

        /// <summary>
        /// Linear distance around the circle.
        /// </summary>
        /// <returns></returns>
        public double Circumference()
        {
            return System.Math.PI * Diameter;
        }

        /// <summary>
        /// Returns true if the given circle has the same radius as this.
        /// </summary>
        /// <param name="circle"></param>
        /// <returns></returns>
        public bool IsConcentricTo(Circle circle)
        {
            return center == circle.center;
        }

        /// <summary>
        /// Returns true if the given circle has the same radius as this.
        /// </summary>
        /// <param name="arc"></param>
        /// <returns></returns>
        public bool IsConcentricTo(Arc arc)
        {
            return center == arc.Center;
        }

        public bool ContainsPoint(Vector pt)
        {
            return Center.DistanceTo(pt) <= Radius;
        }

        /// <summary>
        /// Returns the minimum number of segments needed so that the chord-to-arc
        /// deviation (sagitta) does not exceed the given tolerance.
        /// </summary>
        public int SegmentsForTolerance(double tolerance)
        {
            if (tolerance >= Radius)
                return 3;

            var maxAngle = 2.0 * System.Math.Acos(1.0 - tolerance / Radius);
            return System.Math.Max(3, (int)System.Math.Ceiling(Angle.TwoPI / maxAngle));
        }

        public List<Vector> ToPoints(int segments = 1000)
        {
            var points = new List<Vector>();
            var stepAngle = Angle.TwoPI / segments;

            for (int i = 0; i <= segments; ++i)
            {
                var angle = stepAngle * i;

                points.Add(new Vector(
                    System.Math.Cos(angle) * Radius + Center.X,
                    System.Math.Sin(angle) * Radius + Center.Y));
            }

            return points;
        }

        /// <summary>
        /// Linear distance around the circle.
        /// </summary>
        public override double Length
        {
            get { return Circumference(); }
        }

        /// <summary>
        /// Reverses the rotation direction.
        /// </summary>
        public override void Reverse()
        {
            if (Rotation == RotationType.CCW)
                Rotation = RotationType.CW;
            else
                Rotation = RotationType.CCW;
        }

        /// <summary>
        /// Moves the center point to the given coordinates.
        /// </summary>
        /// <param name="x"></param>
        /// <param name="y"></param>
        public override void MoveTo(double x, double y)
        {
            Center = new Vector(x, y);
        }

        /// <summary>
        /// Moves the center point to the given point.
        /// </summary>
        /// <param name="pt"></param>
        public override void MoveTo(Vector pt)
        {
            Center = pt;
        }

        /// <summary>
        /// Offsets the center point by the given distances.
        /// </summary>
        /// <param name="x"></param>
        /// <param name="y"></param>
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
        /// Scales the circle from the zero point.
        /// </summary>
        /// <param name="factor"></param>
        public override void Scale(double factor)
        {
            center *= factor;
            radius *= factor;
            UpdateBounds();
        }

        /// <summary>
        /// Scales the circle from the origin.
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
        /// Rotates the circle from the zero point.
        /// </summary>
        /// <param name="angle"></param>
        public override void Rotate(double angle)
        {
            Center = Center.Rotate(angle);
        }

        /// <summary>
        /// /// Rotates the circle from the origin.
        /// </summary>
        /// <param name="angle"></param>
        /// <param name="origin"></param>
        public override void Rotate(double angle, Vector origin)
        {
            Center = Center.Rotate(angle, origin);
        }

        /// <summary>
        /// Updates the bounding box.
        /// </summary>
        public override void UpdateBounds()
        {
            boundingBox.X = Center.X - Radius;
            boundingBox.Y = Center.Y - Radius;
            boundingBox.Width = Diameter;
            boundingBox.Height = Diameter;
        }

        public override Entity OffsetEntity(double distance, OffsetSide side)
        {
            if (side == OffsetSide.Left && Rotation == RotationType.CCW)
            {
                return Radius <= distance ? null : new Circle(center, Radius - distance)
                {
                    Layer = Layer,
                    Rotation = Rotation
                };
            }
            else
            {
                return new Circle(center, Radius + distance) { Layer = Layer };
            }
        }

        public override Entity OffsetEntity(double distance, Vector pt)
        {
            if (ContainsPoint(pt))
            {
                return Radius <= distance ? null : new Circle(center, Radius - distance)
                {
                    Layer = Layer,
                    Rotation = Rotation
                };
            }
            else
            {
                return new Circle(center, Radius + distance) { Layer = Layer };
            }
        }

        /// <summary>
        /// Gets the closest point on the circle to the specified point.
        /// </summary>
        /// <param name="pt"></param>
        /// <returns></returns>
        public override Vector ClosestPointTo(Vector pt)
        {
            var angle = Center.AngleTo(pt);

            return new Vector(
                System.Math.Cos(angle) * Radius + Center.X,
                System.Math.Sin(angle) * Radius + Center.Y);
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
        /// <param name="pts">Points of intersection.</param>
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
            var dist = Center.DistanceTo(circle.Center);
            return (dist < (Radius + circle.Radius) && dist > System.Math.Abs(Radius - circle.Radius));
        }

        /// <summary>
        /// Returns true if the given circle is intersecting this.
        /// </summary>
        /// <param name="circle"></param>
        /// <param name="pts">Points of intersection.</param>
        /// <returns></returns>
        public override bool Intersects(Circle circle, out List<Vector> pts)
        {
            return Helper.Intersects(this, circle, out pts);
        }

        /// <summary>
        /// Returns true if the given line is intersecting this.
        /// </summary>
        /// <param name="line"></param>
        /// <returns></returns>
        public override bool Intersects(Line line)
        {
            List<Vector> pts;
            return Helper.Intersects(this, line, out pts);
        }

        /// <summary>
        /// Returns true if the given line is intersecting this.
        /// </summary>
        /// <param name="line"></param>
        /// <param name="pts">Points of intersection.</param>
        /// <returns></returns>
        public override bool Intersects(Line line, out List<Vector> pts)
        {
            return Helper.Intersects(this, line, out pts);
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
        /// <param name="pts">Points of intersection.</param>
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
        /// <param name="pts">Points of intersection.</param>
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
            get { return EntityType.Circle; }
        }
    }
}
