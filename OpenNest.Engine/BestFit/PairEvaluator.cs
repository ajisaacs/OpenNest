using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using OpenNest.Converters;
using OpenNest.Engine.Fill;
using OpenNest.Geometry;
using OpenNest.Math;

namespace OpenNest.Engine.BestFit
{
    public class PairEvaluator : IPairEvaluator
    {
        private const double ChordTolerance = 0.01;

        /// <summary>
        /// Tighter chord tolerance for the overlap check only. Rounded-corner arcs
        /// polygonized at the coarser <see cref="ChordTolerance"/> can "cut the corner"
        /// enough to hide a genuine but tiny sliver overlap between two candidates —
        /// this needs to match the precision Part.Intersects uses elsewhere so BestFit's
        /// Keep decision agrees with the same overlap check callers rely on downstream.
        /// </summary>
        private const double OverlapChordTolerance = 0.001;

        public List<BestFitResult> EvaluateAll(List<PairCandidate> candidates)
        {
            if (candidates.Count == 0)
                return new List<BestFitResult>();

            // Build a perimeter-only drawing once — all candidates share the same drawing.
            // This avoids cloning the full program (with all cutouts) for every candidate.
            var perimeterDrawing = CreatePerimeterDrawing(candidates[0].Drawing);

            var resultBag = new ConcurrentBag<BestFitResult>();

            Parallel.ForEach(
                candidates,
                c =>
                {
                    resultBag.Add(Evaluate(c, perimeterDrawing));
                }
            );

            return resultBag.ToList();
        }

        public BestFitResult Evaluate(PairCandidate candidate)
        {
            var perimeterDrawing = CreatePerimeterDrawing(candidate.Drawing);
            return Evaluate(candidate, perimeterDrawing);
        }

        private BestFitResult Evaluate(PairCandidate candidate, Drawing perimeterDrawing)
        {
            var part1 = Part.CreateAtOrigin(perimeterDrawing);

            var part2 = Part.CreateAtOrigin(perimeterDrawing, candidate.Part2Rotation);
            part2.Location = candidate.Part2Offset;
            part2.UpdateBounds();

            // Convex hull vertices from perimeter polygons only
            var allPoints = GetPartVertices(part1);
            allPoints.AddRange(GetPartVertices(part2));

            // Find optimal bounding rectangle via rotating calipers
            double bestArea,
                bestWidth,
                bestHeight,
                bestRotation;
            List<double> hullAngles = null;

            if (allPoints.Count >= 3)
            {
                var hull = ConvexHull.Compute(allPoints);
                var result = RotatingCalipers.MinimumBoundingRectangle(hull);
                bestArea = result.Area;
                bestWidth = result.Width;
                bestHeight = result.Height;
                bestRotation = result.Angle;
                hullAngles = RotationAnalysis.GetHullEdgeAngles(hull);
            }
            else
            {
                var combinedBox = (
                    (IEnumerable<IBoundable>)new IBoundable[] { part1, part2 }
                ).GetBoundingBox();
                bestArea = combinedBox.Area();
                bestWidth = combinedBox.Width;
                bestHeight = combinedBox.Length;
                bestRotation = 0;
                hullAngles = new List<double> { 0 };
            }

            var trueArea = candidate.Drawing.Area * 2;

            // Normalize to landscape (width >= height) for consistent display. Do this before
            // the overlap check so bestRotation already matches the final OptimalRotation that
            // BuildParts will apply.
            if (bestHeight > bestWidth)
            {
                var tmp = bestWidth;
                bestWidth = bestHeight;
                bestHeight = tmp;
                bestRotation += Angle.HalfPI;
            }

            // Overlap check — perimeter vs perimeter, in the same final orientation BuildParts
            // uses downstream. Uses Collision.HasOverlap (full polygon clip) rather than
            // Shape.Intersects (edge-crossing only), which misses containment-style overlaps
            // where one perimeter's boundary never crosses the other's. Checking pre-rotation
            // geometry here (rather than rotating part1/part2 first, matching BuildParts) would
            // tessellate arcs at a different orientation than the geometry actually gets placed
            // with, letting tangent-corner slivers slip through in one frame but not the other.
            if (!bestRotation.IsEqualTo(0))
            {
                var pairBounds = (
                    (IEnumerable<IBoundable>)new IBoundable[] { part1, part2 }
                ).GetBoundingBox();
                var center = pairBounds.Center;
                part1.Rotate(-bestRotation, center);
                part2.Rotate(-bestRotation, center);
            }

            var shape1 = GetPerimeterShape(part1);
            var shape2 = GetPerimeterShape(part2);
            var overlaps =
                shape1 != null
                && shape2 != null
                && Collision.HasOverlap(
                    shape1.ToPolygonWithTolerance(OverlapChordTolerance),
                    shape2.ToPolygonWithTolerance(OverlapChordTolerance)
                );

            return new BestFitResult
            {
                Candidate = candidate,
                RotatedArea = bestArea,
                BoundingWidth = bestWidth,
                BoundingHeight = bestHeight,
                OptimalRotation = bestRotation,
                TrueArea = trueArea,
                HullAngles = hullAngles,
                Keep = !overlaps,
                Reason = overlaps ? "Overlap detected" : "Valid",
            };
        }

        private static Drawing CreatePerimeterDrawing(Drawing source)
        {
            var entities = ConvertProgram
                .ToGeometry(source.Program)
                .Where(e => SpecialLayers.IsMaterial(e.Layer))
                .ToList();
            var profile = new ShapeProfile(entities);
            var program = ConvertGeometry.ToProgram(profile.Perimeter);
            return new Drawing(source.Name, program);
        }

        private static Shape GetPerimeterShape(Part part)
        {
            var entities = ConvertProgram
                .ToGeometry(part.Program)
                .Where(e => SpecialLayers.IsMaterial(e.Layer))
                .ToList();
            var shapes = ShapeBuilder.GetShapes(entities);
            if (shapes.Count == 0)
                return null;
            shapes[0].Offset(part.Location);
            return shapes[0];
        }

        private static List<Vector> GetPartVertices(Part part)
        {
            var entities = ConvertProgram
                .ToGeometry(part.Program)
                .Where(e => SpecialLayers.IsMaterial(e.Layer))
                .ToList();
            var shapes = ShapeBuilder.GetShapes(entities);
            var points = new List<Vector>();

            foreach (var shape in shapes)
            {
                var polygon = shape.ToPolygonWithTolerance(ChordTolerance);
                polygon.Offset(part.Location);
                points.AddRange(polygon.Vertices);
            }

            return points;
        }
    }
}
