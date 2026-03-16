using System;
using System.Collections.Generic;
using OpenNest.Math;

namespace OpenNest.Engine.Sequencing
{
    public class LeastCodeSequencer : IPartSequencer
    {
        private readonly int _maxIterations;

        public LeastCodeSequencer(int maxIterations = 100)
        {
            _maxIterations = maxIterations;
        }

        public List<SequencedPart> Sequence(IReadOnlyList<Part> parts, Plate plate)
        {
            if (parts.Count == 0)
                return new List<SequencedPart>();

            var exit = PlateHelper.GetExitPoint(plate);
            var ordered = NearestNeighbor(parts, exit);
            TwoOpt(ordered, exit);

            var result = new List<SequencedPart>(ordered.Count);
            foreach (var p in ordered)
                result.Add(new SequencedPart { Part = p });
            return result;
        }

        private static List<Part> NearestNeighbor(IReadOnlyList<Part> parts, OpenNest.Geometry.Vector exit)
        {
            var remaining = new List<Part>(parts);
            var ordered = new List<Part>(parts.Count);

            var current = exit;
            while (remaining.Count > 0)
            {
                var bestIdx = 0;
                var bestDist = Distance(current, Center(remaining[0]));

                for (var i = 1; i < remaining.Count; i++)
                {
                    var d = Distance(current, Center(remaining[i]));
                    if (d < bestDist - Tolerance.Epsilon)
                    {
                        bestDist = d;
                        bestIdx = i;
                    }
                }

                var next = remaining[bestIdx];
                ordered.Add(next);
                remaining.RemoveAt(bestIdx);
                current = Center(next);
            }

            return ordered;
        }

        private void TwoOpt(List<Part> ordered, OpenNest.Geometry.Vector exit)
        {
            var n = ordered.Count;
            if (n < 3)
                return;

            for (var iter = 0; iter < _maxIterations; iter++)
            {
                var improved = false;

                for (var i = 0; i < n - 1; i++)
                {
                    for (var j = i + 1; j < n; j++)
                    {
                        var before = RouteDistance(ordered, exit, i, j);
                        Reverse(ordered, i, j);
                        var after = RouteDistance(ordered, exit, i, j);

                        if (after < before - Tolerance.Epsilon)
                        {
                            improved = true;
                        }
                        else
                        {
                            // Revert
                            Reverse(ordered, i, j);
                        }
                    }
                }

                if (!improved)
                    break;
            }
        }

        /// <summary>
        /// Computes the total distance of the route starting from exit through all parts.
        /// Only the segment around the reversed segment [i..j] needs to be checked,
        /// but here we compute the full route cost for correctness.
        /// </summary>
        private static double RouteDistance(List<Part> ordered, OpenNest.Geometry.Vector exit, int i, int j)
        {
            // Full route distance: exit -> ordered[0] -> ... -> ordered[n-1]
            var total = 0.0;
            var prev = exit;
            foreach (var p in ordered)
            {
                var c = Center(p);
                total += Distance(prev, c);
                prev = c;
            }
            return total;
        }

        private static void Reverse(List<Part> list, int i, int j)
        {
            while (i < j)
            {
                var tmp = list[i];
                list[i] = list[j];
                list[j] = tmp;
                i++;
                j--;
            }
        }

        private static OpenNest.Geometry.Vector Center(Part part)
        {
            return part.BoundingBox.Center;
        }

        private static double Distance(OpenNest.Geometry.Vector a, OpenNest.Geometry.Vector b)
        {
            var dx = b.X - a.X;
            var dy = b.Y - a.Y;
            return System.Math.Sqrt(dx * dx + dy * dy);
        }
    }
}
