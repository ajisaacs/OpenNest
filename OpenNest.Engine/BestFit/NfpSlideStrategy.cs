using OpenNest.Geometry;
using OpenNest.Math;
using System.Collections.Generic;
using System.IO;

namespace OpenNest.Engine.BestFit
{
    public class NfpSlideStrategy : IBestFitStrategy
    {
        private static readonly string LogPath = Path.Combine(
            System.Environment.GetFolderPath(System.Environment.SpecialFolder.Desktop),
            "nfp-slide-debug.log");

        private static readonly object LogLock = new object();

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

            Log($"=== Create: drawing={drawing.Name}, rotation={Angle.ToDegrees(part2Rotation):F1}deg ===");
            Log($"  Perimeter: {result.Polygon.Vertices.Count} verts, bounds={FormatBounds(result.Polygon)}");
            Log($"  Hull: {hull.Vertices.Count} verts, bounds={FormatBounds(hull)}");
            Log($"  Correction: ({result.Correction.X:F4}, {result.Correction.Y:F4})");
            Log($"  ProgramBBox: {drawing.Program.BoundingBox()}");

            return new NfpSlideStrategy(part2Rotation, type, description,
                result.Polygon, hull, result.Correction);
        }

        public List<PairCandidate> GenerateCandidates(Drawing drawing, double spacing, double stepSize)
        {
            var candidates = new List<PairCandidate>();

            if (stepSize <= 0)
                return candidates;

            Log($"--- GenerateCandidates: drawing={drawing.Name}, part2Rot={Angle.ToDegrees(_part2Rotation):F1}deg, spacing={spacing}, stepSize={stepSize} ---");

            // Orbiting polygon: same shape rotated to Part2's angle.
            var orbitingPerimeter = PolygonHelper.RotatePolygon(_stationaryPerimeter, _part2Rotation, reNormalize: true);
            var orbitingPoly = ConvexHull.Compute(orbitingPerimeter.Vertices);

            Log($"  Stationary hull: {_stationaryHull.Vertices.Count} verts, bounds={FormatBounds(_stationaryHull)}");
            Log($"  Orbiting perimeter (rotated): {orbitingPerimeter.Vertices.Count} verts, bounds={FormatBounds(orbitingPerimeter)}");
            Log($"  Orbiting hull: {orbitingPoly.Vertices.Count} verts, bounds={FormatBounds(orbitingPoly)}");

            var nfp = NoFitPolygon.ComputeConvex(_stationaryHull, orbitingPoly);

            if (nfp == null || nfp.Vertices.Count < 3)
            {
                Log($"  NFP failed or degenerate (verts={nfp?.Vertices.Count ?? 0})");
                return candidates;
            }

            var verts = nfp.Vertices;
            var vertCount = nfp.IsClosed() ? verts.Count - 1 : verts.Count;

            Log($"  NFP: {verts.Count} verts (closed={nfp.IsClosed()}, walking {vertCount}), bounds={FormatBounds(nfp)}");
            Log($"  Correction: ({_correction.X:F4}, {_correction.Y:F4})");

            // Log NFP vertices
            for (var v = 0; v < vertCount; v++)
                Log($"    NFP vert[{v}]: ({verts[v].X:F4}, {verts[v].Y:F4}) -> corrected: ({verts[v].X - _correction.X:F4}, {verts[v].Y - _correction.Y:F4})");

            // Compare with what RotationSlideStrategy would produce
            var part1 = Part.CreateAtOrigin(drawing);
            var part2 = Part.CreateAtOrigin(drawing, _part2Rotation);
            Log($"  Part1 (rot=0): loc=({part1.Location.X:F4}, {part1.Location.Y:F4}), bbox={part1.BoundingBox}");
            Log($"  Part2 (rot={Angle.ToDegrees(_part2Rotation):F1}): loc=({part2.Location.X:F4}, {part2.Location.Y:F4}), bbox={part2.BoundingBox}");

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

            // Log overlap check for vertex candidates (first few)
            var checkCount = System.Math.Min(vertCount, 8);
            for (var c = 0; c < checkCount; c++)
            {
                var cand = candidates[c];
                var p2 = Part.CreateAtOrigin(drawing, cand.Part2Rotation);
                p2.Location = cand.Part2Offset;
                var overlaps = part1.Intersects(p2, out _);
                Log($"  Candidate[{c}]: offset=({cand.Part2Offset.X:F4}, {cand.Part2Offset.Y:F4}), overlaps={overlaps}");
            }

            Log($"  Total candidates: {candidates.Count}");
            Log("");

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

        private static string FormatBounds(Polygon polygon)
        {
            polygon.UpdateBounds();
            var bb = polygon.BoundingBox;
            return $"[({bb.Left:F4}, {bb.Bottom:F4})-({bb.Right:F4}, {bb.Top:F4}), {bb.Width:F2}x{bb.Length:F2}]";
        }

        private static void Log(string message)
        {
            lock (LogLock)
            {
                File.AppendAllText(LogPath, message + "\n");
            }
        }
    }
}
