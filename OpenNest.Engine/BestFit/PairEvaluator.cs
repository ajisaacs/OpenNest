using System.Collections.Generic;
using System.Linq;
using OpenNest.Converters;
using OpenNest.Geometry;
using OpenNest.Math;

namespace OpenNest.Engine.BestFit
{
    public class PairEvaluator
    {
        private const double ChordTolerance = 0.01;

        public BestFitResult Evaluate(PairCandidate candidate)
        {
            var drawing = candidate.Drawing;

            // Build part1 at origin
            var part1 = new Part(drawing);
            var bbox1 = part1.Program.BoundingBox();
            part1.Offset(-bbox1.Location.X, -bbox1.Location.Y);
            part1.UpdateBounds();

            // Build part2 with rotation, normalized to origin, then positioned
            var part2 = new Part(drawing);
            if (!candidate.Part2Rotation.IsEqualTo(0))
                part2.Rotate(candidate.Part2Rotation);
            var bbox2 = part2.Program.BoundingBox();
            part2.Offset(-bbox2.Location.X, -bbox2.Location.Y);
            part2.Location = candidate.Part2Offset;
            part2.UpdateBounds();

            // Check overlap via shape intersection
            var overlaps = CheckOverlap(part1, part2);

            // Collect all polygon vertices for convex hull / optimal rotation
            var allPoints = GetPartVertices(part1);
            allPoints.AddRange(GetPartVertices(part2));

            // Find optimal bounding rectangle via rotating calipers
            double bestArea, bestWidth, bestHeight, bestRotation;

            if (allPoints.Count >= 3)
            {
                var hull = ConvexHull.Compute(allPoints);
                var result = RotatingCalipers.MinimumBoundingRectangle(hull);
                bestArea = result.Area;
                bestWidth = result.Width;
                bestHeight = result.Height;
                bestRotation = result.Angle;
            }
            else
            {
                var combinedBox = ((IEnumerable<IBoundable>)new IBoundable[] { part1, part2 }).GetBoundingBox();
                bestArea = combinedBox.Area();
                bestWidth = combinedBox.Width;
                bestHeight = combinedBox.Height;
                bestRotation = 0;
            }

            var trueArea = drawing.Area * 2;

            return new BestFitResult
            {
                Candidate = candidate,
                RotatedArea = bestArea,
                BoundingWidth = bestWidth,
                BoundingHeight = bestHeight,
                OptimalRotation = bestRotation,
                TrueArea = trueArea,
                Keep = !overlaps,
                Reason = overlaps ? "Overlap detected" : "Valid"
            };
        }

        private bool CheckOverlap(Part part1, Part part2)
        {
            var shapes1 = GetPartShapes(part1);
            var shapes2 = GetPartShapes(part2);

            for (var i = 0; i < shapes1.Count; i++)
            {
                for (var j = 0; j < shapes2.Count; j++)
                {
                    List<Vector> pts;

                    if (shapes1[i].Intersects(shapes2[j], out pts))
                        return true;
                }
            }

            return false;
        }

        private List<Shape> GetPartShapes(Part part)
        {
            var entities = ConvertProgram.ToGeometry(part.Program)
                .Where(e => e.Layer != SpecialLayers.Rapid);
            var shapes = Helper.GetShapes(entities);
            shapes.ForEach(s => s.Offset(part.Location));
            return shapes;
        }

        private List<Vector> GetPartVertices(Part part)
        {
            var entities = ConvertProgram.ToGeometry(part.Program)
                .Where(e => e.Layer != SpecialLayers.Rapid);
            var shapes = Helper.GetShapes(entities);
            var points = new List<Vector>();

            foreach (var shape in shapes)
            {
                var polygon = shape.ToPolygonWithTolerance(ChordTolerance);
                polygon.Offset(part.Location);

                foreach (var vertex in polygon.Vertices)
                    points.Add(vertex);
            }

            return points;
        }
    }
}
