using OpenNest.Geometry;
using OpenNest.Math;
using System.Collections.Generic;

namespace OpenNest.CNC.CuttingStrategy
{
    public class ContourCuttingStrategy
    {
        public CuttingParameters Parameters { get; set; }

        public CuttingResult Apply(Program partProgram, Vector approachPoint)
        {
            var exitPoint = approachPoint;
            var entities = partProgram.ToGeometry();
            entities.RemoveAll(e => e.Layer == SpecialLayers.Rapid);

            // Separate scribe/etch entities — they don't get lead-ins or kerf
            var scribeEntities = entities.FindAll(e => e.Layer == SpecialLayers.Scribe);
            entities.RemoveAll(e => e.Layer == SpecialLayers.Scribe);

            var profile = new ShapeProfile(entities);

            // Find closest point on perimeter from exit point
            var perimeterPoint = profile.Perimeter.ClosestPointTo(exitPoint, out var perimeterEntity);

            // Chain cutouts by nearest-neighbor from perimeter point, then reverse
            // so farthest cutouts are cut first, nearest-to-perimeter cut last
            var orderedCutouts = SequenceCutouts(profile.Cutouts, perimeterPoint);
            orderedCutouts.Reverse();

            // Build output program: scribe first, cutouts second, perimeter last
            var result = new Program(Mode.Absolute);
            var currentPoint = exitPoint;

            // Emit scribe/etch contours first (no lead-ins, no kerf)
            if (scribeEntities.Count > 0)
            {
                var scribeShapes = ShapeBuilder.GetShapes(scribeEntities);
                foreach (var scribe in scribeShapes)
                {
                    var startPt = GetShapeStartPoint(scribe);
                    result.Codes.Add(new RapidMove(startPt));
                    result.Codes.AddRange(ConvertShapeToMoves(scribe, startPt, LayerType.Scribe));
                    currentPoint = startPt;
                }
            }

            foreach (var cutout in orderedCutouts)
            {
                var contourType = DetectContourType(cutout);
                var closestPt = cutout.ClosestPointTo(currentPoint, out var entity);
                var normal = ComputeNormal(closestPt, entity, contourType);
                var winding = DetermineWinding(cutout);

                var leadIn = SelectLeadIn(contourType);
                var leadOut = SelectLeadOut(contourType);

                if (contourType == ContourType.ArcCircle && entity is Circle circle)
                    leadIn = ClampLeadInForCircle(leadIn, circle, closestPt, normal);

                result.Codes.AddRange(leadIn.Generate(closestPt, normal, winding));
                var reindexed = cutout.ReindexAt(closestPt, entity);

                if (Parameters.TabsEnabled && Parameters.TabConfig != null)
                {
                    var trimmed = TrimShapeForTab(reindexed, closestPt, Parameters.TabConfig.Size);
                    result.Codes.AddRange(ConvertShapeToMoves(trimmed, closestPt));
                    result.Codes.AddRange(leadOut.Generate(closestPt, normal, winding));
                }
                else
                {
                    result.Codes.AddRange(ConvertShapeToMoves(reindexed, closestPt));
                    result.Codes.AddRange(leadOut.Generate(closestPt, normal, winding));
                }

                currentPoint = closestPt;
            }

            var lastCutPoint = exitPoint;

            // Perimeter last
            {
                var perimeterPt = profile.Perimeter.ClosestPointTo(currentPoint, out perimeterEntity);
                lastCutPoint = perimeterPt;
                var normal = ComputeNormal(perimeterPt, perimeterEntity, ContourType.External);
                var winding = DetermineWinding(profile.Perimeter);

                var leadIn = SelectLeadIn(ContourType.External);
                var leadOut = SelectLeadOut(ContourType.External);

                result.Codes.AddRange(leadIn.Generate(perimeterPt, normal, winding));
                var reindexed = profile.Perimeter.ReindexAt(perimeterPt, perimeterEntity);

                if (Parameters.TabsEnabled && Parameters.TabConfig != null)
                {
                    var trimmed = TrimShapeForTab(reindexed, perimeterPt, Parameters.TabConfig.Size);
                    result.Codes.AddRange(ConvertShapeToMoves(trimmed, perimeterPt));
                    result.Codes.AddRange(leadOut.Generate(perimeterPt, normal, winding));
                }
                else
                {
                    result.Codes.AddRange(ConvertShapeToMoves(reindexed, perimeterPt));
                    result.Codes.AddRange(leadOut.Generate(perimeterPt, normal, winding));
                }
            }

            // Convert to incremental mode to match the convention used by
            // the rest of the system (rendering, bounding box, drag, etc.).
            result.Mode = Mode.Incremental;

            return new CuttingResult
            {
                Program = result,
                LastCutPoint = lastCutPoint
            };
        }

        private List<Shape> SequenceCutouts(List<Shape> cutouts, Vector startPoint)
        {
            var remaining = new List<Shape>(cutouts);
            var ordered = new List<Shape>();
            var currentPoint = startPoint;

            while (remaining.Count > 0)
            {
                var nearest = remaining[0];
                var nearestPt = nearest.ClosestPointTo(currentPoint);
                var nearestDist = nearestPt.DistanceTo(currentPoint);

                for (var i = 1; i < remaining.Count; i++)
                {
                    var pt = remaining[i].ClosestPointTo(currentPoint);
                    var dist = pt.DistanceTo(currentPoint);
                    if (dist < nearestDist)
                    {
                        nearest = remaining[i];
                        nearestPt = pt;
                        nearestDist = dist;
                    }
                }

                ordered.Add(nearest);
                remaining.Remove(nearest);
                currentPoint = nearestPt;
            }

            return ordered;
        }

        public static ContourType DetectContourType(Shape cutout)
        {
            if (cutout.Entities.Count == 1 && cutout.Entities[0] is Circle)
                return ContourType.ArcCircle;

            return ContourType.Internal;
        }

        public static double ComputeNormal(Vector point, Entity entity, ContourType contourType)
        {
            double normal;

            if (entity is Line line)
            {
                // Perpendicular to line direction
                var tangent = line.EndPoint.AngleFrom(line.StartPoint);
                normal = tangent + Math.Angle.HalfPI;
            }
            else if (entity is Arc arc)
            {
                // Radial direction from center to point
                normal = point.AngleFrom(arc.Center);

                // For CCW arcs the radial points the wrong way — flip it.
                // CW arcs are convex features (corners) where radial = outward.
                // CCW arcs are concave features (slots) where radial = inward.
                if (arc.Rotation == RotationType.CCW)
                    normal += System.Math.PI;
            }
            else if (entity is Circle circle)
            {
                normal = point.AngleFrom(circle.Center);
            }
            else
            {
                normal = 0;
            }

            // For internal contours, flip the normal (point into scrap)
            if (contourType == ContourType.Internal || contourType == ContourType.ArcCircle)
                normal += System.Math.PI;

            return Math.Angle.NormalizeRad(normal);
        }

        public static RotationType DetermineWinding(Shape shape)
        {
            // Use signed area: positive = CCW, negative = CW
            var area = shape.Area();
            return area >= 0 ? RotationType.CCW : RotationType.CW;
        }

        private LeadIn ClampLeadInForCircle(LeadIn leadIn, Circle circle, Vector contourPoint, double normalAngle)
        {
            if (leadIn is NoLeadIn || Parameters.PierceClearance <= 0)
                return leadIn;

            var piercePoint = leadIn.GetPiercePoint(contourPoint, normalAngle);
            var maxRadius = circle.Radius - Parameters.PierceClearance;
            if (maxRadius <= 0)
                return leadIn;

            var distFromCenter = piercePoint.DistanceTo(circle.Center);
            if (distFromCenter <= maxRadius)
                return leadIn;

            // Compute max distance from contourPoint toward piercePoint that stays
            // inside a circle of radius maxRadius centered at circle.Center.
            // Solve: |contourPoint + t*d - center|^2 = maxRadius^2
            var currentDist = contourPoint.DistanceTo(piercePoint);
            if (currentDist < Math.Tolerance.Epsilon)
                return leadIn;

            var dx = (piercePoint.X - contourPoint.X) / currentDist;
            var dy = (piercePoint.Y - contourPoint.Y) / currentDist;
            var vx = contourPoint.X - circle.Center.X;
            var vy = contourPoint.Y - circle.Center.Y;

            var b = 2.0 * (vx * dx + vy * dy);
            var c = vx * vx + vy * vy - maxRadius * maxRadius;
            var discriminant = b * b - 4.0 * c;

            if (discriminant < 0)
                return leadIn;

            var t = (-b + System.Math.Sqrt(discriminant)) / 2.0;
            if (t <= 0)
                return leadIn;

            var scale = t / currentDist;
            if (scale >= 1.0)
                return leadIn;

            return leadIn.Scale(scale);
        }

        private LeadIn SelectLeadIn(ContourType contourType)
        {
            return contourType switch
            {
                ContourType.ArcCircle => Parameters.ArcCircleLeadIn ?? Parameters.InternalLeadIn,
                ContourType.Internal => Parameters.InternalLeadIn,
                _ => Parameters.ExternalLeadIn
            };
        }

        private LeadOut SelectLeadOut(ContourType contourType)
        {
            return contourType switch
            {
                ContourType.ArcCircle => Parameters.ArcCircleLeadOut ?? Parameters.InternalLeadOut,
                ContourType.Internal => Parameters.InternalLeadOut,
                _ => Parameters.ExternalLeadOut
            };
        }

        private static Shape TrimShapeForTab(Shape shape, Vector center, double tabSize)
        {
            var tabCircle = new Circle(center, tabSize);
            var entities = new List<Entity>(shape.Entities);

            // Trim end: walk backward removing entities inside the tab circle
            while (entities.Count > 0)
            {
                var entity = entities[entities.Count - 1];
                if (entity.Intersects(tabCircle, out var pts) && pts.Count > 0)
                {
                    // Find intersection furthest from center (furthest along path from end)
                    var best = pts[0];
                    var bestDist = best.DistanceTo(center);
                    for (var j = 1; j < pts.Count; j++)
                    {
                        var dist = pts[j].DistanceTo(center);
                        if (dist > bestDist)
                        {
                            best = pts[j];
                            bestDist = dist;
                        }
                    }

                    if (entity is Line line)
                    {
                        var (first, _) = line.SplitAt(best);
                        entities.RemoveAt(entities.Count - 1);
                        if (first != null)
                            entities.Add(first);
                    }
                    else if (entity is Arc arc)
                    {
                        var (first, _) = arc.SplitAt(best);
                        entities.RemoveAt(entities.Count - 1);
                        if (first != null)
                            entities.Add(first);
                    }

                    break;
                }

                // No intersection — entity is entirely inside circle, remove it
                if (EntityStartPoint(entity).DistanceTo(center) <= tabSize + Tolerance.Epsilon)
                {
                    entities.RemoveAt(entities.Count - 1);
                    continue;
                }

                break;
            }

            var result = new Shape();
            result.Entities.AddRange(entities);
            return result;
        }

        private static Vector EntityStartPoint(Entity entity)
        {
            if (entity is Line line) return line.StartPoint;
            if (entity is Arc arc) return arc.StartPoint();
            return Vector.Zero;
        }

        private List<ICode> ConvertShapeToMoves(Shape shape, Vector startPoint, LayerType layer = LayerType.Display)
        {
            var moves = new List<ICode>();

            foreach (var entity in shape.Entities)
            {
                if (entity is Line line)
                {
                    moves.Add(new LinearMove(line.EndPoint) { Layer = layer });
                }
                else if (entity is Arc arc)
                {
                    moves.Add(new ArcMove(arc.EndPoint(), arc.Center, arc.IsReversed ? RotationType.CW : RotationType.CCW) { Layer = layer });
                }
                else if (entity is Circle circle)
                {
                    moves.Add(new ArcMove(startPoint, circle.Center, circle.Rotation) { Layer = layer });
                }
                else
                {
                    throw new System.InvalidOperationException($"Unsupported entity type: {entity.Type}");
                }
            }

            return moves;
        }

        private static Vector GetShapeStartPoint(Shape shape)
        {
            var first = shape.Entities[0];
            if (first is Line line) return line.StartPoint;
            if (first is Arc arc) return arc.StartPoint();
            if (first is Circle circle) return new Vector(circle.Center.X + circle.Radius, circle.Center.Y);
            return Vector.Zero;
        }
    }
}
