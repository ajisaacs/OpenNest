using OpenNest.Geometry;
using System.Collections.Generic;

namespace OpenNest.Engine.BestFit
{
    public class NfpSlideStrategy : IBestFitStrategy
    {
        private readonly double _part2Rotation;
        private readonly Polygon _stationaryPerimeter;
        private readonly Polygon _stationaryHull;
        private readonly Vector _correction;

        public NfpSlideStrategy(double part2Rotation, int type, string description,
            Polygon stationaryPerimeter, Polygon stationaryHull, Vector correction)
        {
            _part2Rotation = part2Rotation;
            StrategyIndex = type;
            Description = description;
            _stationaryPerimeter = stationaryPerimeter;
            _stationaryHull = stationaryHull;
            _correction = correction;
        }

        public int StrategyIndex { get; }
        public string Description { get; }

        /// <summary>
        /// Creates an NfpSlideStrategy by extracting polygon data from a drawing.
        /// Returns null if the drawing has no valid perimeter.
        /// </summary>
        public static NfpSlideStrategy Create(Drawing drawing, double part2Rotation,
            int type, string description, double spacing)
        {
            var result = PolygonHelper.ExtractPerimeterPolygon(drawing, spacing / 2);

            if (result.Polygon == null)
                return null;

            var hull = ConvexHull.Compute(result.Polygon.Vertices);

            return new NfpSlideStrategy(part2Rotation, type, description,
                result.Polygon, hull, result.Correction);
        }

        public List<PairCandidate> GenerateCandidates(Drawing drawing, double spacing, double stepSize)
        {
            var candidates = new List<PairCandidate>();

            if (stepSize <= 0)
                return candidates;

            var orbitingPerimeter = PolygonHelper.RotatePolygon(_stationaryPerimeter, _part2Rotation, reNormalize: true);
            var orbitingPoly = ConvexHull.Compute(orbitingPerimeter.Vertices);

            var nfp = NoFitPolygon.ComputeConvex(_stationaryHull, orbitingPoly);

            if (nfp == null || nfp.Vertices.Count < 3)
                return candidates;

            var verts = nfp.Vertices;
            var vertCount = nfp.IsClosed() ? verts.Count - 1 : verts.Count;

            var testNumber = 0;

            for (var i = 0; i < vertCount; i++)
            {
                var offset = ApplyCorrection(verts[i], _correction);
                candidates.Add(MakeCandidate(drawing, offset, spacing, testNumber++));

                // Add edge samples for long edges.
                var next = (i + 1) % vertCount;
                var dx = verts[next].X - verts[i].X;
                var dy = verts[next].Y - verts[i].Y;
                var edgeLength = System.Math.Sqrt(dx * dx + dy * dy);

                if (edgeLength > stepSize)
                {
                    var steps = (int)(edgeLength / stepSize);
                    for (var s = 1; s < steps; s++)
                    {
                        var t = (double)s / steps;
                        var sample = new Vector(
                            verts[i].X + dx * t,
                            verts[i].Y + dy * t);
                        var sampleOffset = ApplyCorrection(sample, _correction);
                        candidates.Add(MakeCandidate(drawing, sampleOffset, spacing, testNumber++));
                    }
                }
            }

            return candidates;
        }

        private static Vector ApplyCorrection(Vector nfpVertex, Vector correction)
        {
            return new Vector(nfpVertex.X - correction.X, nfpVertex.Y - correction.Y);
        }

        private PairCandidate MakeCandidate(Drawing drawing, Vector offset, double spacing, int testNumber)
        {
            return new PairCandidate
            {
                Drawing = drawing,
                Part1Rotation = 0,
                Part2Rotation = _part2Rotation,
                Part2Offset = offset,
                StrategyIndex = StrategyIndex,
                TestNumber = testNumber,
                Spacing = spacing
            };
        }
    }
}
