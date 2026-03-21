using OpenNest.Engine.Fill;
using OpenNest.Geometry;
using OpenNest.Math;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace OpenNest.Engine.Strategies
{
    public static class FillHelpers
    {
        public static Pattern BuildRotatedPattern(List<Part> groupParts, double angle)
        {
            var pattern = new Pattern();
            var center = ((IEnumerable<IBoundable>)groupParts).GetBoundingBox().Center;

            foreach (var part in groupParts)
            {
                var clone = (Part)part.Clone();
                clone.UpdateBounds();

                if (!angle.IsEqualTo(0))
                    clone.Rotate(angle, center);

                pattern.Parts.Add(clone);
            }

            pattern.UpdateBounds();
            return pattern;
        }

        public static List<Part> FillPattern(FillLinear engine, List<Part> groupParts, List<double> angles, Box workArea, IFillComparer comparer = null)
        {
            var results = new ConcurrentBag<(List<Part> Parts, FillScore Score)>();

            Parallel.ForEach(angles, angle =>
            {
                var pattern = BuildRotatedPattern(groupParts, angle);

                if (pattern.Parts.Count == 0)
                    return;

                var h = engine.Fill(pattern, NestDirection.Horizontal);
                if (h != null && h.Count > 0)
                    results.Add((h, FillScore.Compute(h, workArea)));

                var v = engine.Fill(pattern, NestDirection.Vertical);
                if (v != null && v.Count > 0)
                    results.Add((v, FillScore.Compute(v, workArea)));
            });

            List<Part> best = null;
            var bestScore = default(FillScore);

            foreach (var res in results)
            {
                if (comparer != null)
                {
                    if (best == null || comparer.IsBetter(res.Parts, best, workArea))
                        best = res.Parts;
                }
                else
                {
                    if (best == null || res.Score > bestScore)
                    {
                        best = res.Parts;
                        bestScore = res.Score;
                    }
                }
            }

            return best;
        }

        /// <summary>
        /// Runs a fill function with direction preference logic.
        /// If preferred is null, tries both directions and returns the better result.
        /// If preferred is set, tries preferred first; only tries other if preferred yields zero.
        /// </summary>
        public static List<Part> FillWithDirectionPreference(
            Func<NestDirection, List<Part>> fillFunc,
            NestDirection? preferred,
            IFillComparer comparer,
            Box workArea)
        {
            if (preferred == null)
            {
                var h = fillFunc(NestDirection.Horizontal);
                var v = fillFunc(NestDirection.Vertical);

                if ((h == null || h.Count == 0) && (v == null || v.Count == 0))
                    return new List<Part>();

                if (h == null || h.Count == 0) return v;
                if (v == null || v.Count == 0) return h;

                return comparer.IsBetter(h, v, workArea) ? h : v;
            }

            var other = preferred == NestDirection.Horizontal
                ? NestDirection.Vertical
                : NestDirection.Horizontal;

            var pref = fillFunc(preferred.Value);
            if (pref != null && pref.Count > 0)
                return pref;

            var fallback = fillFunc(other);
            return fallback ?? new List<Part>();
        }
    }
}
