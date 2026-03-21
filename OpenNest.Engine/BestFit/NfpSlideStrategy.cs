using OpenNest.Geometry;
using System.Collections.Generic;

namespace OpenNest.Engine.BestFit
{
    public class NfpSlideStrategy : IBestFitStrategy
    {
        private readonly double _part2Rotation;

        public NfpSlideStrategy(double part2Rotation, int type, string description)
        {
            _part2Rotation = part2Rotation;
            Type = type;
            Description = description;
        }

        public int Type { get; }
        public string Description { get; }

        public List<PairCandidate> GenerateCandidates(Drawing drawing, double spacing, double stepSize)
        {
            var candidates = new List<PairCandidate>();

            if (stepSize <= 0)
                return candidates;

            var halfSpacing = spacing / 2;

            // Extract stationary polygon (Part1 at rotation 0), with spacing applied.
            var stationaryResult = PolygonHelper.ExtractPerimeterPolygon(drawing, halfSpacing);

            if (stationaryResult.Polygon == null)
                return candidates;

            var stationaryPoly = stationaryResult.Polygon;

            // Orbiting polygon: same shape rotated to Part2's angle.
            var orbitingPoly = PolygonHelper.RotatePolygon(stationaryResult.Polygon, _part2Rotation);

            // Compute NFP.
            var nfp = NoFitPolygon.Compute(stationaryPoly, orbitingPoly);

            if (nfp == null || nfp.Vertices.Count < 3)
                return candidates;

            // Coordinate correction: NFP offsets are in polygon-space.
            // Part.CreateAtOrigin uses program bbox origin.
            var correction = stationaryResult.Correction;

            // Walk NFP boundary — vertices + edge samples.
            var verts = nfp.Vertices;
            var vertCount = nfp.IsClosed() ? verts.Count - 1 : verts.Count;
            var testNumber = 0;

            for (var i = 0; i < vertCount; i++)
            {
                // Add vertex candidate.
                var offset = ApplyCorrection(verts[i], correction);
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
                        var sampleOffset = ApplyCorrection(sample, correction);
                        candidates.Add(MakeCandidate(drawing, sampleOffset, spacing, testNumber++));
                    }
                }
            }

            return candidates;
        }

        private static Vector ApplyCorrection(Vector nfpVertex, Vector correction)
        {
            return new Vector(nfpVertex.X + correction.X, nfpVertex.Y + correction.Y);
        }

        private PairCandidate MakeCandidate(Drawing drawing, Vector offset, double spacing, int testNumber)
        {
            return new PairCandidate
            {
                Drawing = drawing,
                Part1Rotation = 0,
                Part2Rotation = _part2Rotation,
                Part2Offset = offset,
                StrategyType = Type,
                TestNumber = testNumber,
                Spacing = spacing
            };
        }
    }
}
