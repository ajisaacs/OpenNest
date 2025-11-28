using System.Collections.Generic;

namespace OpenNest.Geometry
{
    public abstract class Entity : IBoundable
    {
        protected Box boundingBox;

        protected Entity()
        {
            Layer = OpenNest.Geometry.Layer.Default;
            boundingBox = new Box();
        }

        /// <summary>
        /// Smallest box that contains the entity.
        /// </summary>
        public Box BoundingBox
        {
            get { return boundingBox; }
        }

        /// <summary>
        /// Entity layer type.
        /// </summary>
        public Layer Layer { get; set; }

        /// <summary>
        /// X-Coordinate of the left-most point.
        /// </summary>
        public virtual double Left
        {
            get { return boundingBox.Left; }
        }

        /// <summary>
        /// X-Coordinate of the right-most point.
        /// </summary>
        public virtual double Right
        {
            get { return boundingBox.Right; }
        }

        /// <summary>
        /// Y-Coordinate of the highest point.
        /// </summary>
        public virtual double Top
        {
            get { return boundingBox.Top; }
        }

        /// <summary>
        /// Y-Coordinate of the lowest point.
        /// </summary>
        public virtual double Bottom
        {
            get { return boundingBox.Bottom; }
        }

        /// <summary>
        /// Length of the entity.
        /// </summary>
        public abstract double Length { get; }

        /// <summary>
        /// Reverses the entity.
        /// </summary>
        public abstract void Reverse();

        /// <summary>
        /// Moves the entity location to the given coordinates.
        /// </summary>
        /// <param name="x"></param>
        /// <param name="y"></param>
        public abstract void MoveTo(double x, double y);

        /// <summary>
        /// Moves the entity location to the given point.
        /// </summary>
        /// <param name="pt"></param>
        public abstract void MoveTo(Vector pt);

        /// <summary>
        /// Offsets the entity location by the given distances.
        /// </summary>
        /// <param name="x"></param>
        /// <param name="y"></param>
        public abstract void Offset(double x, double y);

        /// <summary>
        /// Offsets the entity location by the given distances.
        /// </summary>
        /// <param name="voffset"></param>
        public abstract void Offset(Vector voffset);

        /// <summary>
        /// Scales the entity from the zero point.
        /// </summary>
        /// <param name="factor"></param>
        public abstract void Scale(double factor);

        /// <summary>
        /// Scales the entity from the origin.
        /// </summary>
        /// <param name="factor"></param>
        /// <param name="origin"></param>
        public abstract void Scale(double factor, Vector origin);

        /// <summary>
        /// Rotates the entity from the zero point.
        /// </summary>
        /// <param name="angle"></param>
        public abstract void Rotate(double angle);

        /// <summary>
        /// Rotates the entity from the origin.
        /// </summary>
        /// <param name="angle"></param>
        /// <param name="origin"></param>
        public abstract void Rotate(double angle, Vector origin);

        /// <summary>
        /// Updates the bounding box.
        /// </summary>
        public abstract void UpdateBounds();

        /// <summary>
        /// Gets a new entity offset the given distance from this entity.
        /// </summary>
        /// <param name="distance"></param>
        /// <param name="side"></param>
        /// <returns></returns>
        public abstract Entity OffsetEntity(double distance, OffsetSide side);

        /// <summary>
        /// Gets a new entity offset the given distance from this entity. Offset side determined by point.
        /// </summary>
        /// <param name="distance"></param>
        /// <param name="pt"></param>
        /// <returns></returns>
        public abstract Entity OffsetEntity(double distance, Vector pt);

        /// <summary>
        /// Gets the closest point on the entity to the given point.
        /// </summary>
        /// <param name="pt"></param>
        /// <returns></returns>
        public abstract Vector ClosestPointTo(Vector pt);

        /// <summary>
        /// Returns true if the given arc is intersecting this.
        /// </summary>
        /// <param name="arc"></param>
        /// <returns></returns>
        public abstract bool Intersects(Arc arc);

        /// <summary>
        /// Returns true if the given arc is intersecting this.
        /// </summary>
        /// <param name="arc"></param>
        /// <param name="pts">List to store the points of intersection.</param>
        /// <returns></returns>
        public abstract bool Intersects(Arc arc, out List<Vector> pts);

        /// <summary>
        /// Returns true if the given circle is intersecting this.
        /// </summary>
        /// <param name="circle"></param>
        /// <returns></returns>
        public abstract bool Intersects(Circle circle);

        /// <summary>
        /// Returns true if the given circle is intersecting this.
        /// </summary>
        /// <param name="circle"></param>
        /// <param name="pts"></param>
        /// <returns></returns>
        public abstract bool Intersects(Circle circle, out List<Vector> pts);

        /// <summary>
        /// Returns true if the given line is intersecting this.
        /// </summary>
        /// <param name="line"></param>
        /// <returns></returns>
        public abstract bool Intersects(Line line);

        /// <summary>
        /// Returns true if the given line is intersecting this.
        /// </summary>
        /// <param name="line"></param>
        /// <param name="pts"></param>
        /// <returns></returns>
        public abstract bool Intersects(Line line, out List<Vector> pts);

        /// <summary>
        /// Returns true if the given polygon is intersecting this.
        /// </summary>
        /// <param name="polygon"></param>
        /// <returns></returns>
        public abstract bool Intersects(Polygon polygon);

        /// <summary>
        /// Returns true if the given polygon is intersecting this.
        /// </summary>
        /// <param name="polygon"></param>
        /// <param name="pts"></param>
        /// <returns></returns>
        public abstract bool Intersects(Polygon polygon, out List<Vector> pts);

        /// <summary>
        /// Returns true if the given shape is intersecting this.
        /// </summary>
        /// <param name="shape"></param>
        /// <returns></returns>
        public abstract bool Intersects(Shape shape);

        /// <summary>
        /// Returns true if the given shape is intersecting this.
        /// </summary>
        /// <param name="shape"></param>
        /// <param name="pts"></param>
        /// <returns></returns>
        public abstract bool Intersects(Shape shape, out List<Vector> pts);

        /// <summary>
        /// Type of entity.
        /// </summary>
        public abstract EntityType Type { get; }
    }

    public static class EntityExtensions
    {
        public static double FindBestRotation(this List<Entity> entities, double stepAngle, double startAngle = 0, double endAngle = Angle.TwoPI)
        {
            startAngle = Angle.NormalizeRad(startAngle);

            if (!endAngle.IsEqualTo(Angle.TwoPI))
                endAngle = Angle.NormalizeRad(endAngle);

            if (stepAngle.IsEqualTo(0.0))
                return startAngle;

            entities.ForEach(e => e.Rotate(startAngle));

            var bestAngle = startAngle;
            var bestArea = entities.GetBoundingBox().Area();

            var steps = startAngle < endAngle
                ? (endAngle - startAngle) / stepAngle
                : (endAngle + Angle.TwoPI) - startAngle / stepAngle;

            for (int i = 1; i <= steps; ++i)
            {
                entities.ForEach(e => e.Rotate(stepAngle));

                var area = entities.GetBoundingBox().Area();

                if (area < bestArea)
                {
                    bestArea = area;
                    bestAngle = startAngle + stepAngle * i;
                }
            }

            return bestAngle;
        }
    }
}
