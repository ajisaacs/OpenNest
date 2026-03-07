using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace OpenNest.Geometry
{
    public class Shape : Entity
    {
        /// <summary>
        /// Entities that make up the shape.
        /// </summary>
        public List<Entity> Entities;

        public Shape()
        {
            Entities = new List<Entity>();
        }

        /// <summary>
        /// Returns true if the shape is closed.
        /// </summary>
        /// <returns></returns>
        public bool IsClosed()
        {
            if (Entities.Count == 0)
                return false;

            var first = Entities[0];
            Vector firstStartPoint;
            Vector firstEndPoint;

            switch (first.Type)
            {
                case EntityType.Arc:
                    var arc = (Arc)first;
                    firstStartPoint = arc.StartPoint();
                    firstEndPoint = arc.EndPoint();
                    break;

                case EntityType.Circle:
                    return Entities.Count == 1;

                case EntityType.Line:
                    var line = (Line)first;
                    firstStartPoint = line.StartPoint;
                    firstEndPoint = line.EndPoint;
                    break;

                default:
                    Debug.Fail("Unhandled geometry type");
                    return false;
            }

            var endpt = firstEndPoint;

            Entity geo = null;

            for (int i = 1; i < Entities.Count; ++i)
            {
                geo = Entities[i];

                switch (geo.Type)
                {
                    case EntityType.Arc:
                        var arc = (Arc)geo;

                        if (arc.StartPoint() != endpt)
                            return false;

                        endpt = arc.EndPoint();
                        break;

                    case EntityType.Circle:
                        return Entities.Count == 1;

                    case EntityType.Line:
                        var line = (Line)geo;

                        if (line.StartPoint != endpt)
                            return false;

                        endpt = line.EndPoint;
                        break;

                    default:
                        Debug.Fail("Unhandled geometry type");
                        return false;
                }
            }

            if (geo == null)
                return false;

            var last = geo;
            Vector lastEndPoint;

            switch (last.Type)
            {
                case EntityType.Arc:
                    var arc = (Arc)last;
                    lastEndPoint = arc.EndPoint();
                    break;

                case EntityType.Line:
                    var line = (Line)last;
                    lastEndPoint = line.EndPoint;
                    break;

                default:
                    Debug.Fail("Unhandled geometry type");
                    return false;
            }

            return lastEndPoint == firstStartPoint;
        }

        /// <summary>
        /// Gets the area.
        /// </summary>
        /// <returns>Returns the area or 0 if the shape is NOT closed.</returns>
        public double Area()
        {
            // Check if the shape is closed so we can get the area.
            if (!IsClosed())
                return 0;

            // If the shape is closed and only one entity in the geometry
            // then that entity would have to be a circle.
            if (Entities.Count == 1)
            {
                var circle = Entities[0] as Circle;
                return circle == null ? 0 : circle.Area();
            }

            return ToPolygon().Area();
        }

        /// <summary>
        /// Joins all overlapping lines and arcs.
        /// </summary>
        public void Optimize()
        {
            var lines = new List<Line>();
            var arcs = new List<Arc>();

            foreach (var geo in Entities)
            {
                switch (geo.Type)
                {
                    case EntityType.Arc:
                        arcs.Add((Arc)geo);
                        break;

                    case EntityType.Line:
                        lines.Add((Line)geo);
                        break;
                }
            }

            Helper.Optimize(lines);
            Helper.Optimize(arcs);
        }

        /// <summary>
        /// Gets the closest point on the shape to the given point.
        /// </summary>
        /// <param name="pt"></param>
        /// <param name="entity">Entity that contains the point.</param>
        /// <returns></returns>
        public Vector ClosestPointTo(Vector pt, out Entity entity)
        {
            if (Entities.Count == 0)
            {
                entity = null;
                return Vector.Invalid;
            }

            var first = Entities[0];

            Vector closestPt = first.ClosestPointTo(pt);
            double distance = closestPt.DistanceTo(pt);

            entity = first;

            for (int i = 1; i < Entities.Count; i++)
            {
                var entity2 = Entities[i];
                var closestPt2 = entity2.ClosestPointTo(pt);
                var distance2 = closestPt2.DistanceTo(pt);

                if (distance2 < distance)
                {
                    closestPt = closestPt2;
                    distance = distance2;
                    entity = entity2;
                }
            }

            return closestPt;
        }

        /// <summary>
        /// Converts the shape to a polygon.
        /// </summary>
        /// <returns></returns>
        public Polygon ToPolygon(int arcSegments = 1000)
        {
            var polygon = new Polygon();

            foreach (var entity in Entities)
            {
                switch (entity.Type)
                {
                    case EntityType.Arc:
                        var arc = (Arc)entity;
                        polygon.Vertices.AddRange(arc.ToPoints(arcSegments));
                        break;

                    case EntityType.Line:
                        var line = (Line)entity;
                        polygon.Vertices.AddRange(new[]
                        {
                            line.StartPoint,
                            line.EndPoint
                        });
                        break;

                    case EntityType.Circle:
                        var circle = (Circle)entity;
                        polygon.Vertices.AddRange(circle.ToPoints(arcSegments));
                        break;

                    default:
                        Debug.Fail("Unhandled geometry type");
                        break;
                }
            }

            polygon.Close();
            polygon.Cleanup();

            return polygon;
        }

        /// <summary>
        /// Converts the shape to a polygon using a chord tolerance to determine
        /// the number of segments per arc/circle.
        /// </summary>
        public Polygon ToPolygonWithTolerance(double tolerance)
        {
            var polygon = new Polygon();

            foreach (var entity in Entities)
            {
                switch (entity.Type)
                {
                    case EntityType.Arc:
                        var arc = (Arc)entity;
                        polygon.Vertices.AddRange(arc.ToPoints(arc.SegmentsForTolerance(tolerance)));
                        break;

                    case EntityType.Line:
                        var line = (Line)entity;
                        polygon.Vertices.AddRange(new[]
                        {
                            line.StartPoint,
                            line.EndPoint
                        });
                        break;

                    case EntityType.Circle:
                        var circle = (Circle)entity;
                        polygon.Vertices.AddRange(circle.ToPoints(circle.SegmentsForTolerance(tolerance)));
                        break;

                    default:
                        Debug.Fail("Unhandled geometry type");
                        break;
                }
            }

            polygon.Close();
            polygon.Cleanup();

            return polygon;
        }

        /// <summary>
        /// Reverses the rotation direction of the shape.
        /// </summary>
        public override void Reverse()
        {
            Entities.ForEach(e => e.Reverse());
            Entities.Reverse();
        }

        /// <summary>
        /// Linear distance of the shape.
        /// </summary>
        public override double Length
        {
            get { return Entities.Sum(geo => geo.Length); }
        }

        /// <summary>
        /// Moves the start point to the given coordinates.
        /// </summary>
        /// <param name="x"></param>
        /// <param name="y"></param>
        public override void MoveTo(double x, double y)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Moves the start point to the given point.
        /// </summary>
        /// <param name="pt"></param>
        public override void MoveTo(Vector pt)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Offsets the shape location by the given distances.
        /// </summary>
        /// <param name="x"></param>
        /// <param name="y"></param>
        public override void Offset(double x, double y)
        {
            Entities.ForEach(e => e.Offset(x, y));
            boundingBox.Offset(x, y);
        }

        /// <summary>
        /// Offsets the shape location by the given distances.
        /// </summary>
        /// <param name="voffset"></param>
        public override void Offset(Vector voffset)
        {
            Entities.ForEach(e => e.Offset(voffset));
            boundingBox.Offset(voffset);
        }

        /// <summary>
        /// Scales the shape from the zero point.
        /// </summary>
        /// <param name="factor"></param>
        public override void Scale(double factor)
        {
            Entities.ForEach(e => e.Scale(factor));
            UpdateBounds();
        }

        /// <summary>
        /// Scales the shape from the origin.
        /// </summary>
        /// <param name="factor"></param>
        /// <param name="origin"></param>
        public override void Scale(double factor, Vector origin)
        {
            Entities.ForEach(e => e.Scale(factor, origin));
            UpdateBounds();
        }

        /// <summary>
        /// Rotates the shape from the zero point.
        /// </summary>
        /// <param name="angle"></param>
        public override void Rotate(double angle)
        {
            Entities.ForEach(e => e.Rotate(angle));
            UpdateBounds();
        }

        /// <summary>
        /// Rotates the shape from the origin.
        /// </summary>
        /// <param name="angle"></param>
        /// <param name="origin"></param>
        public override void Rotate(double angle, Vector origin)
        {
            Entities.ForEach(e => e.Rotate(angle, origin));
            UpdateBounds();
        }

        /// <summary>
        /// Updates the bounding box.
        /// </summary>
        public override void UpdateBounds()
        {
            boundingBox = Entities.Select(geo => geo.BoundingBox)
                .ToList()
                .GetBoundingBox();
        }

        public override Entity OffsetEntity(double distance, OffsetSide side)
        {
            var offsetShape = new Shape();
            var definedShape = new DefinedShape(this);

            Entity lastEntity = null;
            Entity lastOffsetEntity = null;

            foreach (var entity in definedShape.Perimeter.Entities)
            {
                var offsetEntity = entity.OffsetEntity(distance, side);

                if (offsetEntity == null)
                    continue;

                switch (entity.Type)
                {
                    case EntityType.Line:
                        {
                            var line = (Line)entity;
                            var offsetLine = (Line)offsetEntity;

                            if (lastOffsetEntity != null && lastOffsetEntity.Type == EntityType.Line)
                            {
                                var lastLine = lastEntity as Line;
                                var lastOffsetLine = lastOffsetEntity as Line;

                                if (lastLine == null || lastOffsetLine == null)
                                    continue;

                                Vector intersection;

                                if (Helper.Intersects(offsetLine, lastOffsetLine, out intersection))
                                {
                                    offsetLine.StartPoint = intersection;
                                    lastOffsetLine.EndPoint = intersection;
                                }
                                else
                                {
                                    var arc = new Arc(
                                        line.StartPoint,
                                        distance,
                                        line.StartPoint.AngleTo(lastOffsetLine.EndPoint),
                                        line.StartPoint.AngleTo(offsetLine.StartPoint), 
                                        side == OffsetSide.Left
                                        );

                                    offsetShape.Entities.Add(arc);
                                }
                            }

                            offsetShape.Entities.Add(offsetLine);
                            break;
                        }

                    default:
                        offsetShape.Entities.Add(offsetEntity);
                        break;
                }

                lastOffsetEntity = offsetEntity;
                lastEntity = entity;
            }

            foreach (var cutout in definedShape.Cutouts)
                offsetShape.Entities.AddRange(((Shape)cutout.OffsetEntity(distance, side)).Entities);

            return offsetShape;
        }

        public override Entity OffsetEntity(double distance, Vector pt)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Gets the closest point on the shape to the given point.
        /// </summary>
        /// <param name="pt"></param>
        /// <returns></returns>
        public override Vector ClosestPointTo(Vector pt)
        {
            Entity entity;
            return ClosestPointTo(pt, out entity);
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
            get { return EntityType.Shape; }
        }

        public double FindBestRotation(double stepAngle)
        {
            return Entities.FindBestRotation(stepAngle);
        }

        public double FindBestRotation(double stepAngle, double startAngle, double endAngle)
        {
            return Entities.FindBestRotation(stepAngle, startAngle, endAngle);
        }
    }
}
