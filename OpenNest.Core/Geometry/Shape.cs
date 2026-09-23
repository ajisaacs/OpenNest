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

            var tol = Math.Tolerance.ChainTolerance;
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

                        if (arc.StartPoint().DistanceTo(endpt) > tol)
                            return false;

                        endpt = arc.EndPoint();
                        break;

                    case EntityType.Circle:
                        return Entities.Count == 1;

                    case EntityType.Line:
                        var line = (Line)geo;

                        if (line.StartPoint.DistanceTo(endpt) > tol)
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

            return lastEndPoint.DistanceTo(firstStartPoint) <= tol;
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

            GeometryOptimizer.Optimize(lines);
            GeometryOptimizer.Optimize(arcs);
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
        /// Returns a new shape with entities reordered so that the given point on
        /// the given entity becomes the new start point of the contour.
        /// </summary>
        /// <param name="point">The point on the entity to reindex at.</param>
        /// <param name="entity">The entity containing the point.</param>
        /// <returns>A new reindexed shape.</returns>
        public Shape ReindexAt(Vector point, Entity entity)
        {
            // Circle case: return a new shape with just the circle
            if (entity is Circle)
            {
                var result = new Shape();
                result.Entities.Add(entity);
                return result;
            }

            var i = Entities.IndexOf(entity);
            if (i < 0)
                throw new ArgumentException("Entity not found in shape", nameof(entity));

            // Split the entity at the point
            Entity firstHalf = null;
            Entity secondHalf = null;

            if (entity is Line line)
            {
                var (f, s) = line.SplitAt(point);
                firstHalf = f;
                secondHalf = s;
            }
            else if (entity is Arc arc)
            {
                var (f, s) = arc.SplitAt(point);
                firstHalf = f;
                secondHalf = s;
            }

            // Build reindexed entity list
            var entities = new List<Entity>();

            // secondHalf of split entity (if not null)
            if (secondHalf != null)
                entities.Add(secondHalf);

            // Entities after the split index (wrapping)
            for (var j = i + 1; j < Entities.Count; j++)
                entities.Add(Entities[j]);

            // Entities before the split index (wrapping)
            for (var j = 0; j < i; j++)
                entities.Add(Entities[j]);

            // firstHalf of split entity (if not null)
            if (firstHalf != null)
                entities.Add(firstHalf);

            var reindexed = new Shape();
            reindexed.Entities.AddRange(entities);
            return reindexed;
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
                        polygon.Vertices.AddRange(new[] { line.StartPoint, line.EndPoint });
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
            polygon.UpdateBounds();

            return polygon;
        }

        /// <summary>
        /// Converts the shape to a polygon using a chord tolerance to determine
        /// the number of segments per arc/circle.
        /// </summary>
        public Polygon ToPolygonWithTolerance(double tolerance, bool circumscribe = false)
        {
            var polygon = new Polygon();

            foreach (var entity in Entities)
            {
                switch (entity.Type)
                {
                    case EntityType.Arc:
                        var arc = (Arc)entity;
                        polygon.Vertices.AddRange(
                            arc.ToPoints(arc.SegmentsForTolerance(tolerance), circumscribe)
                        );
                        break;

                    case EntityType.Line:
                        var line = (Line)entity;
                        polygon.Vertices.AddRange(new[] { line.StartPoint, line.EndPoint });
                        break;

                    case EntityType.Circle:
                        var circle = (Circle)entity;
                        polygon.Vertices.AddRange(
                            circle.ToPoints(circle.SegmentsForTolerance(tolerance), circumscribe)
                        );
                        break;

                    default:
                        Debug.Fail("Unhandled geometry type");
                        break;
                }
            }

            polygon.Close();
            polygon.Cleanup();
            polygon.UpdateBounds();

            return polygon;
        }

        public override Entity Clone()
        {
            var copy = new Shape();
            foreach (var e in Entities)
                copy.Entities.Add(e.Clone());
            CopyBaseTo(copy);
            return copy;
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
            boundingBox = Entities.Select(geo => geo.BoundingBox).ToList().GetBoundingBox();
        }

        /// <summary>
        /// Offsets each perimeter entity to the given side and joins the pieces into a
        /// closed chain: line-line corners get a round join (convex) or a miter (concave),
        /// other convex corners get a round join, and any remaining gap (a concave corner
        /// involving an arc, or an entity that collapsed under the offset) is bridged
        /// with a line. Cutouts are offset the same way.
        /// <para>
        /// Where a feature is narrower than twice the distance, the result keeps zero-area
        /// spikes and inverted loops. They lie inside the true offset envelope, so they are
        /// harmless to directional-distance queries, which only need a closed boundary
        /// that never falls inside the envelope. Use <see cref="ClipperBridge"/> when a
        /// clean region is needed.
        /// </para>
        /// </summary>
        public override Entity OffsetEntity(double distance, OffsetSide side)
        {
            var offsetShape = new Shape();
            var definedShape = new ShapeProfile(this);

            var pieces = new List<OffsetPiece>();
            var collapsed = false;

            foreach (var entity in definedShape.Perimeter.Entities)
            {
                var offsetEntity = entity.OffsetEntity(distance, side);

                if (offsetEntity == null)
                {
                    collapsed = true;
                    continue;
                }

                pieces.Add(new OffsetPiece(entity, offsetEntity, collapsed));
                collapsed = false;
            }

            // Entities that collapsed at the end of the loop sit before the first piece.
            if (collapsed && pieces.Count > 0)
                pieces[0] = pieces[0] with { CollapsedBefore = true };

            for (var i = 0; i < pieces.Count; i++)
            {
                offsetShape.Entities.Add(pieces[i].Offset);

                if (pieces.Count > 1)
                {
                    JoinOffsetPieces(
                        pieces[i],
                        pieces[(i + 1) % pieces.Count],
                        distance,
                        side,
                        offsetShape
                    );
                }
            }

            foreach (var cutout in definedShape.Cutouts)
                offsetShape.Entities.AddRange(
                    ((Shape)cutout.OffsetEntity(distance, side)).Entities
                );

            return offsetShape;
        }

        private readonly record struct OffsetPiece(
            Entity Source,
            Entity Offset,
            bool CollapsedBefore
        );

        private static void JoinOffsetPieces(
            OffsetPiece last,
            OffsetPiece next,
            double distance,
            OffsetSide side,
            Shape offsetShape
        )
        {
            // Lines meeting across a collapsed fillet are concave, so a miter trims both at
            // their intersection. Parallel ones (a round-bottomed slot) fall through to
            // the bridge below.
            if (
                next.CollapsedBefore
                && last.Offset is Line lastOffsetLine
                && next.Offset is Line nextOffsetLine
                && Intersect.IntersectsUnbounded(nextOffsetLine, lastOffsetLine, out var miter)
            )
            {
                lastOffsetLine.EndPoint = miter;
                nextOffsetLine.StartPoint = miter;
                return;
            }

            if (!next.CollapsedBefore && last.Source is Line lastLine && next.Source is Line nextLine)
            {
                JoinOffsetLines(
                    lastLine,
                    (Line)last.Offset,
                    nextLine,
                    (Line)next.Offset,
                    distance,
                    side,
                    offsetShape
                );
                return;
            }

            if (
                !TryGetEnds(last.Offset, out _, out var gapStart)
                || !TryGetEnds(next.Offset, out var gapEnd, out _)
            )
                return;

            if (gapStart.DistanceTo(gapEnd) <= OpenNest.Math.Tolerance.Epsilon)
                return;

            if (
                !next.CollapsedBefore
                && IsConvexCorner(last.Source, next.Source, side, out var corner)
            )
            {
                offsetShape.Entities.Add(
                    new Arc(
                        corner,
                        distance,
                        corner.AngleTo(gapStart),
                        corner.AngleTo(gapEnd),
                        side == OffsetSide.Left
                    )
                );
                return;
            }

            // Concave corner or collapsed entity: the neighbors' offsets overlap, so a
            // straight bridge stays inside the offset envelope and closes the chain.
            offsetShape.Entities.Add(new Line(gapStart, gapEnd));
        }

        private static bool IsConvexCorner(
            Entity last,
            Entity next,
            OffsetSide side,
            out Vector corner
        )
        {
            corner = default;

            if (
                !TryGetEnds(last, out _, out corner)
                || !TryGetTangents(last, out _, out var d1)
                || !TryGetTangents(next, out var d2, out _)
            )
                return false;

            var cross = d1.X * d2.Y - d1.Y * d2.X;

            return (side == OffsetSide.Left && cross < -OpenNest.Math.Tolerance.Epsilon)
                || (side == OffsetSide.Right && cross > OpenNest.Math.Tolerance.Epsilon);
        }

        private static bool TryGetEnds(Entity entity, out Vector start, out Vector end)
        {
            switch (entity)
            {
                case Line line:
                    start = line.StartPoint;
                    end = line.EndPoint;
                    return true;

                case Arc arc:
                    start = arc.StartPoint();
                    end = arc.EndPoint();
                    return true;

                default:
                    start = end = default;
                    return false;
            }
        }

        /// <summary>
        /// Direction of travel at the start and end of a line or arc.
        /// </summary>
        private static bool TryGetTangents(Entity entity, out Vector start, out Vector end)
        {
            switch (entity)
            {
                case Line line:
                    start = end = line.EndPoint - line.StartPoint;
                    return true;

                case Arc arc:
                    start = ArcTangent(arc, arc.StartAngle);
                    end = ArcTangent(arc, arc.EndAngle);
                    return true;

                default:
                    start = end = default;
                    return false;
            }
        }

        private static Vector ArcTangent(Arc arc, double angle)
        {
            var sin = System.Math.Sin(angle);
            var cos = System.Math.Cos(angle);
            return arc.IsReversed ? new Vector(sin, -cos) : new Vector(-sin, cos);
        }

        private static void JoinOffsetLines(
            Line lastLine,
            Line lastOffsetLine,
            Line line,
            Line offsetLine,
            double distance,
            OffsetSide side,
            Shape offsetShape
        )
        {
            // Determine if this is a convex corner using the cross product of
            // the original line directions. Convex corners need an arc; concave
            // corners use the line intersection (miter join).
            var d1 = lastLine.EndPoint - lastLine.StartPoint;
            var d2 = line.EndPoint - line.StartPoint;
            var cross = d1.X * d2.Y - d1.Y * d2.X;

            var isConvex =
                (side == OffsetSide.Left && cross < -OpenNest.Math.Tolerance.Epsilon)
                || (side == OffsetSide.Right && cross > OpenNest.Math.Tolerance.Epsilon);

            if (isConvex)
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
            else if (
                Intersect.IntersectsUnbounded(offsetLine, lastOffsetLine, out var intersection)
            )
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

        public override Entity OffsetEntity(double distance, Vector pt)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Offsets the shape outward by the given distance.
        /// Normalizes to CW winding before offsetting Left (which is outward for CW),
        /// making the method independent of the original contour winding direction.
        /// </summary>
        internal Shape OffsetOutward(double distance)
        {
            var poly = ToPolygon();

            if (
                poly == null
                || poly.Vertices.Count < 3
                || poly.RotationDirection() == RotationType.CW
            )
                return OffsetEntity(distance, OffsetSide.Left) as Shape;

            // Shape is CCW — reverse to CW so Left offset goes outward.
            var copy = new Shape();

            for (var i = Entities.Count - 1; i >= 0; i--)
            {
                switch (Entities[i])
                {
                    case Line l:
                        copy.Entities.Add(new Line(l.EndPoint, l.StartPoint) { Layer = l.Layer });
                        break;
                    case Arc a:
                        copy.Entities.Add(
                            new Arc(a.Center, a.Radius, a.EndAngle, a.StartAngle, !a.IsReversed)
                            {
                                Layer = a.Layer,
                            }
                        );
                        break;
                    case Circle c:
                        copy.Entities.Add(
                            new Circle(c.Center, c.Radius)
                            {
                                Layer = c.Layer,
                                Rotation = RotationType.CW,
                            }
                        );
                        break;
                }
            }

            return copy.OffsetEntity(distance, OffsetSide.Left) as Shape;
        }

        /// <summary>
        /// Offsets the shape inward by the given distance.
        /// Normalizes to CCW winding before offsetting Left (which is inward for CCW),
        /// making the method independent of the original contour winding direction.
        /// </summary>
        internal Shape OffsetInward(double distance)
        {
            var poly = ToPolygon();

            if (
                poly == null
                || poly.Vertices.Count < 3
                || poly.RotationDirection() == RotationType.CCW
            )
                return OffsetEntity(distance, OffsetSide.Left) as Shape;

            // Create a reversed copy to avoid mutating shared entity objects.
            var copy = new Shape();

            for (var i = Entities.Count - 1; i >= 0; i--)
            {
                switch (Entities[i])
                {
                    case Line l:
                        copy.Entities.Add(new Line(l.EndPoint, l.StartPoint) { Layer = l.Layer });
                        break;
                    case Arc a:
                        copy.Entities.Add(
                            new Arc(a.Center, a.Radius, a.EndAngle, a.StartAngle, !a.IsReversed)
                            {
                                Layer = a.Layer,
                            }
                        );
                        break;
                    case Circle c:
                        copy.Entities.Add(
                            new Circle(c.Center, c.Radius)
                            {
                                Layer = c.Layer,
                                Rotation = RotationType.CCW,
                            }
                        );
                        break;
                }
            }

            return copy.OffsetEntity(distance, OffsetSide.Left) as Shape;
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
            return Intersect.Intersects(this, shape, out pts);
        }

        /// <summary>
        /// Returns true if the given shape is intersecting this.
        /// </summary>
        /// <param name="shape"></param>
        /// <param name="pts"></param>
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
            get { return EntityType.Shape; }
        }

        public BoundingRectangleResult FindBestRotation()
        {
            return Entities.FindBestRotation();
        }

        public BoundingRectangleResult FindBestRotation(double startAngle, double endAngle)
        {
            return Entities.FindBestRotation(startAngle, endAngle);
        }
    }
}
