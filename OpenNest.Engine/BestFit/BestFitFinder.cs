using OpenNest.Converters;
using OpenNest.Engine.BestFit.Tiling;
using OpenNest.Geometry;
using OpenNest.Math;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace OpenNest.Engine.BestFit
{
    public class BestFitFinder
    {
        private readonly IPairEvaluator _evaluator;
        private readonly ISlideComputer _slideComputer;
        private readonly BestFitFilter _filter;

        public BestFitFinder(double maxPlateWidth, double maxPlateHeight,
            IPairEvaluator evaluator = null, ISlideComputer slideComputer = null)
        {
            _evaluator = evaluator ?? new PairEvaluator();
            _slideComputer = slideComputer;
            var plateAspect = System.Math.Max(maxPlateWidth, maxPlateHeight) /
                              System.Math.Max(System.Math.Min(maxPlateWidth, maxPlateHeight), 0.001);
            _filter = new BestFitFilter
            {
                MaxPlateWidth = maxPlateWidth,
                MaxPlateHeight = maxPlateHeight,
                MaxAspectRatio = System.Math.Max(5.0, plateAspect)
            };
        }

        public List<BestFitResult> FindBestFits(
            Drawing drawing,
            double spacing = 0.25,
            double stepSize = 0.25,
            BestFitSortField sortBy = BestFitSortField.Area)
        {
            var strategies = BuildStrategies(drawing, spacing);

            var candidateBags = new ConcurrentBag<List<PairCandidate>>();

            Parallel.ForEach(strategies, strategy =>
            {
                candidateBags.Add(strategy.GenerateCandidates(drawing, spacing, stepSize));
            });

            var allCandidates = candidateBags.SelectMany(c => c).ToList();

            var results = _evaluator.EvaluateAll(allCandidates);

            _filter.Apply(results);

            results = SortResults(results, sortBy);

            for (var i = 0; i < results.Count; i++)
                results[i].Candidate.TestNumber = i;

            return results;
        }

        public List<TileResult> FindAndTile(
            Drawing drawing, Plate plate,
            double spacing = 0.25, double stepSize = 0.25, int topN = 10)
        {
            var bestFits = FindBestFits(drawing, spacing, stepSize);
            var tileEvaluator = new TileEvaluator();

            return bestFits
                .Where(r => r.Keep)
                .Take(topN)
                .Select(r => tileEvaluator.Evaluate(r, plate))
                .OrderByDescending(t => t.PartsNested)
                .ThenByDescending(t => t.Utilization)
                .ToList();
        }

        private List<IBestFitStrategy> BuildStrategies(Drawing drawing, double spacing)
        {
            var angles = GetRotationAngles(drawing);
            var strategies = new List<IBestFitStrategy>();
            var type = 1;

            foreach (var angle in angles)
            {
                var desc = string.Format("{0:F1} deg rotated, offset slide", Angle.ToDegrees(angle));
                strategies.Add(new RotationSlideStrategy(angle, type++, desc, _slideComputer));
            }

            return strategies;
        }

        private List<double> GetRotationAngles(Drawing drawing)
        {
            var angles = new List<double>
            {
                0,
                Angle.HalfPI,
                System.Math.PI,
                Angle.HalfPI * 3
            };

            var hullAngles = GetHullEdgeAngles(drawing);

            foreach (var hullAngle in hullAngles)
            {
                AddUniqueAngle(angles, hullAngle);
                AddUniqueAngle(angles, Angle.NormalizeRad(hullAngle + System.Math.PI));
            }

            angles.Sort();
            return angles;
        }

        private List<double> GetHullEdgeAngles(Drawing drawing)
        {
            var entities = ConvertProgram.ToGeometry(drawing.Program)
                .Where(e => e.Layer != SpecialLayers.Rapid);
            var shapes = ShapeBuilder.GetShapes(entities);

            var points = new List<Vector>();

            foreach (var shape in shapes)
            {
                // Extract key points from original geometry — line endpoints
                // plus arc endpoints and cardinal extreme points. This avoids
                // tessellating arcs into many chords that flood the hull with
                // near-duplicate edge angles.
                foreach (var entity in shape.Entities)
                {
                    if (entity is Line line)
                    {
                        points.Add(line.StartPoint);
                        points.Add(line.EndPoint);
                    }
                    else if (entity is Arc arc)
                    {
                        points.Add(arc.StartPoint());
                        points.Add(arc.EndPoint());
                        AddArcExtremes(points, arc);
                    }
                }
            }

            if (points.Count < 3)
                return new List<double>();

            var hull = ConvexHull.Compute(points);
            var vertices = hull.Vertices;
            var n = hull.IsClosed() ? vertices.Count - 1 : vertices.Count;
            var hullAngles = new List<double>();

            for (var i = 0; i < n; i++)
            {
                var next = (i + 1) % n;
                var dx = vertices[next].X - vertices[i].X;
                var dy = vertices[next].Y - vertices[i].Y;

                if (dx * dx + dy * dy < Tolerance.Epsilon)
                    continue;

                var angle = Angle.NormalizeRad(System.Math.Atan2(dy, dx));
                AddUniqueAngle(hullAngles, angle);
            }

            return hullAngles;
        }

        /// <summary>
        /// Adds the cardinal extreme points of an arc (0°, 90°, 180°, 270°)
        /// if they fall within the arc's angular span.
        /// </summary>
        private static void AddArcExtremes(List<Vector> points, Arc arc)
        {
            var a1 = arc.StartAngle;
            var a2 = arc.EndAngle;

            if (arc.IsReversed)
                Generic.Swap(ref a1, ref a2);

            // Right (0°)
            if (Angle.IsBetweenRad(Angle.TwoPI, a1, a2))
                points.Add(new Vector(arc.Center.X + arc.Radius, arc.Center.Y));

            // Top (90°)
            if (Angle.IsBetweenRad(Angle.HalfPI, a1, a2))
                points.Add(new Vector(arc.Center.X, arc.Center.Y + arc.Radius));

            // Left (180°)
            if (Angle.IsBetweenRad(System.Math.PI, a1, a2))
                points.Add(new Vector(arc.Center.X - arc.Radius, arc.Center.Y));

            // Bottom (270°)
            if (Angle.IsBetweenRad(System.Math.PI * 1.5, a1, a2))
                points.Add(new Vector(arc.Center.X, arc.Center.Y - arc.Radius));
        }

        /// <summary>
        /// Minimum angular separation (radians) between hull-derived rotation candidates.
        /// Tessellated arcs produce many hull edges with nearly identical angles;
        /// a 1° threshold collapses those into a single representative.
        /// </summary>
        private const double AngleTolerance = System.Math.PI / 36; // 5 degrees

        private static void AddUniqueAngle(List<double> angles, double angle)
        {
            angle = Angle.NormalizeRad(angle);

            foreach (var existing in angles)
            {
                if (existing.IsEqualTo(angle, AngleTolerance))
                    return;
            }

            angles.Add(angle);
        }

        private List<BestFitResult> SortResults(List<BestFitResult> results, BestFitSortField sortBy)
        {
            switch (sortBy)
            {
                case BestFitSortField.Area:
                    return results.OrderBy(r => r.RotatedArea).ToList();
                case BestFitSortField.LongestSide:
                    return results.OrderBy(r => r.LongestSide).ToList();
                case BestFitSortField.ShortestSide:
                    return results.OrderBy(r => r.ShortestSide).ToList();
                case BestFitSortField.Type:
                    return results.OrderBy(r => r.Candidate.StrategyType)
                        .ThenBy(r => r.Candidate.TestNumber).ToList();
                case BestFitSortField.OriginalSequence:
                    return results.OrderBy(r => r.Candidate.TestNumber).ToList();
                case BestFitSortField.Keep:
                    return results.OrderByDescending(r => r.Keep)
                        .ThenBy(r => r.RotatedArea).ToList();
                case BestFitSortField.WhyKeepDrop:
                    return results.OrderBy(r => r.Reason)
                        .ThenBy(r => r.RotatedArea).ToList();
                default:
                    return results;
            }
        }
    }
}
