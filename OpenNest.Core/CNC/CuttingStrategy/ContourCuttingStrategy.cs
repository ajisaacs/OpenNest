using System.Collections.Generic;
using OpenNest.Geometry;

namespace OpenNest.CNC.CuttingStrategy
{
    public class ContourCuttingStrategy
    {
        public CuttingParameters Parameters { get; set; }

        public Program Apply(Program partProgram, Plate plate)
        {
            var exitPoint = GetExitPoint(plate);
            var entities = partProgram.ToGeometry();
            var profile = new ShapeProfile(entities);

            // Find closest point on perimeter from exit point
            var perimeterPoint = profile.Perimeter.ClosestPointTo(exitPoint, out var perimeterEntity);

            // Chain cutouts by nearest-neighbor from perimeter point, then reverse
            // so farthest cutouts are cut first, nearest-to-perimeter cut last
            var orderedCutouts = SequenceCutouts(profile.Cutouts, perimeterPoint);
            orderedCutouts.Reverse();

            // Build output program: cutouts first (farthest to nearest), perimeter last
            var result = new Program();
            var currentPoint = exitPoint;

            foreach (var cutout in orderedCutouts)
            {
                var contourType = DetectContourType(cutout);
                var closestPt = cutout.ClosestPointTo(currentPoint, out var entity);
                var normal = ComputeNormal(closestPt, entity, contourType);
                var winding = DetermineWinding(cutout);

                var leadIn = SelectLeadIn(contourType);
                var leadOut = SelectLeadOut(contourType);

                result.Codes.AddRange(leadIn.Generate(closestPt, normal, winding));
                // Contour re-indexing: split shape entities at closestPt so cutting
                // starts there, convert to ICode, and add to result.Codes
                throw new System.NotImplementedException("Contour re-indexing not yet implemented");
                result.Codes.AddRange(leadOut.Generate(closestPt, normal, winding));

                currentPoint = closestPt;
            }

            // Perimeter last
            {
                var perimeterPt = profile.Perimeter.ClosestPointTo(currentPoint, out perimeterEntity);
                var normal = ComputeNormal(perimeterPt, perimeterEntity, ContourType.External);
                var winding = DetermineWinding(profile.Perimeter);

                var leadIn = SelectLeadIn(ContourType.External);
                var leadOut = SelectLeadOut(ContourType.External);

                result.Codes.AddRange(leadIn.Generate(perimeterPt, normal, winding));
                throw new System.NotImplementedException("Contour re-indexing not yet implemented");
                result.Codes.AddRange(leadOut.Generate(perimeterPt, normal, winding));
            }

            return result;
        }

        private Vector GetExitPoint(Plate plate)
        {
            var w = plate.Size.Width;
            var l = plate.Size.Length;

            return plate.Quadrant switch
            {
                1 => new Vector(0, 0),        // Q1 TopRight origin -> exit BottomLeft
                2 => new Vector(w, 0),         // Q2 TopLeft origin -> exit BottomRight
                3 => new Vector(w, l),         // Q3 BottomLeft origin -> exit TopRight
                4 => new Vector(0, l),         // Q4 BottomRight origin -> exit TopLeft
                _ => new Vector(0, 0)
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

        private ContourType DetectContourType(Shape cutout)
        {
            if (cutout.Entities.Count == 1 && cutout.Entities[0] is Circle)
                return ContourType.ArcCircle;

            return ContourType.Internal;
        }

        private double ComputeNormal(Vector point, Entity entity, ContourType contourType)
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

        private RotationType DetermineWinding(Shape shape)
        {
            // Use signed area: positive = CCW, negative = CW
            var area = shape.Area();
            return area >= 0 ? RotationType.CCW : RotationType.CW;
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
    }
}
