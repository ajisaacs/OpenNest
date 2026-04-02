using OpenNest.Geometry;
using OpenNest.Math;
using System.Collections.Generic;

namespace OpenNest.CNC.CuttingStrategy
{
    public class ContourCuttingStrategy
    {
        public CuttingParameters Parameters { get; set; }

        private record ContourEntry(Shape Shape, Vector Point, Entity Entity);

        public CuttingResult Apply(Program partProgram, Vector approachPoint)
        {
            var entities = partProgram.ToGeometry();
            entities.RemoveAll(e => e.Layer == SpecialLayers.Rapid);

            var scribeEntities = entities.FindAll(e => e.Layer == SpecialLayers.Scribe);
            entities.RemoveAll(e => e.Layer == SpecialLayers.Scribe);

            var profile = new ShapeProfile(entities);

            // Forward pass: sequence cutouts nearest-neighbor from perimeter
            var perimeterPoint = profile.Perimeter.ClosestPointTo(approachPoint, out _);
            var orderedCutouts = SequenceCutouts(profile.Cutouts, perimeterPoint);
            orderedCutouts.Reverse();

            // Backward pass: walk from perimeter back through cutting order
            // so each lead-in faces the next cutout to be cut, not the previous
            var cutoutEntries = ResolveLeadInPoints(orderedCutouts, perimeterPoint);

            var result = new Program(Mode.Absolute);

            EmitScribeContours(result, scribeEntities);

            foreach (var entry in cutoutEntries)
                EmitContour(result, entry.Shape, entry.Point, entry.Entity);

            // Perimeter last
            var lastRefPoint = cutoutEntries.Count > 0 ? cutoutEntries[cutoutEntries.Count - 1].Point : approachPoint;
            var perimeterPt = profile.Perimeter.ClosestPointTo(lastRefPoint, out var perimeterEntity);
            EmitContour(result, profile.Perimeter, perimeterPt, perimeterEntity, ContourType.External);

            result.Mode = Mode.Incremental;

            return new CuttingResult
            {
                Program = result,
                LastCutPoint = perimeterPt
            };
        }

        private static List<ContourEntry> ResolveLeadInPoints(List<Shape> cutouts, Vector startPoint)
        {
            var entries = new ContourEntry[cutouts.Count];
            var currentPoint = startPoint;

            // Walk backward through cutting order (from perimeter outward)
            // so each cutout's lead-in point faces the next cutout to be cut
            for (var i = cutouts.Count - 1; i >= 0; i--)
            {
                var closestPt = cutouts[i].ClosestPointTo(currentPoint, out var entity);
                entries[i] = new ContourEntry(cutouts[i], closestPt, entity);
                currentPoint = closestPt;
            }

            return new List<ContourEntry>(entries);
        }

        private void EmitContour(Program program, Shape shape, Vector point, Entity entity, ContourType? forceType = null)
        {
            var contourType = forceType ?? DetectContourType(shape);
            var winding = DetermineWinding(shape);
            var normal = ComputeNormal(point, entity, contourType, winding);

            var leadIn = SelectLeadIn(contourType);
            var leadOut = SelectLeadOut(contourType);

            if (contourType == ContourType.ArcCircle && entity is Circle circle)
                leadIn = ClampLeadInForCircle(leadIn, circle, point, normal);

            program.Codes.AddRange(leadIn.Generate(point, normal, winding));

            var reindexed = shape.ReindexAt(point, entity);

            if (Parameters.TabsEnabled && Parameters.TabConfig != null)
                reindexed = TrimShapeForTab(reindexed, point, Parameters.TabConfig.Size);

            program.Codes.AddRange(ConvertShapeToMoves(reindexed, point));
            program.Codes.AddRange(leadOut.Generate(point, normal, winding));
        }

        private void EmitScribeContours(Program program, List<Entity> scribeEntities)
        {
            if (scribeEntities.Count == 0) return;

            var shapes = ShapeBuilder.GetShapes(scribeEntities);
            foreach (var shape in shapes)
            {
                var startPt = GetShapeStartPoint(shape);
                program.Codes.Add(new RapidMove(startPt));
                program.Codes.AddRange(ConvertShapeToMoves(shape, startPt, LayerType.Scribe));
            }
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

        public static double ComputeNormal(Vector point, Entity entity, ContourType contourType,
            RotationType winding = RotationType.CW)
        {
            double normal;

            if (entity is Line line)
            {
                // Perpendicular to line direction: tangent + π/2 = left side.
                // Left side = outward for CW winding; for CCW winding, outward
                // is on the right side, so flip.
                var tangent = line.EndPoint.AngleFrom(line.StartPoint);
                normal = tangent + Math.Angle.HalfPI;
                if (winding == RotationType.CCW)
                    normal += System.Math.PI;
            }
            else if (entity is Arc arc)
            {
                // Radial direction from center to point.
                // Flip when the arc direction differs from the contour winding —
                // that indicates a concave feature where radial points inward.
                normal = point.AngleFrom(arc.Center);
                if (arc.Rotation != winding)
                    normal += System.Math.PI;
            }
            else if (entity is Circle circle)
            {
                // Radial outward — always correct regardless of winding
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
            if (shape.Entities.Count == 1 && shape.Entities[0] is Circle circle)
                return circle.Rotation;

            return shape.ToPolygon().RotationDirection();
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
