using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace OpenNest.Engine.BestFit
{
    public static class BestFitCache
    {
        private const double StepSize = 0.25;

        private static readonly ConcurrentDictionary<CacheKey, List<BestFitResult>> _cache =
            new ConcurrentDictionary<CacheKey, List<BestFitResult>>();

        public static Func<Drawing, double, IPairEvaluator> CreateEvaluator { get; set; }

        public static List<BestFitResult> GetOrCompute(
            Drawing drawing, double plateWidth, double plateHeight,
            double spacing)
        {
            var key = new CacheKey(drawing, plateWidth, plateHeight, spacing);

            if (_cache.TryGetValue(key, out var cached))
                return cached;

            IPairEvaluator evaluator = null;

            try
            {
                if (CreateEvaluator != null)
                {
                    try { evaluator = CreateEvaluator(drawing, spacing); }
                    catch { /* fall back to default evaluator */ }
                }

                var finder = new BestFitFinder(plateWidth, plateHeight, evaluator);
                var results = finder.FindBestFits(drawing, spacing, StepSize);

                _cache.TryAdd(key, results);
                return results;
            }
            finally
            {
                (evaluator as IDisposable)?.Dispose();
            }
        }

        public static void Invalidate(Drawing drawing)
        {
            foreach (var key in _cache.Keys)
            {
                if (ReferenceEquals(key.Drawing, drawing))
                    _cache.TryRemove(key, out _);
            }
        }

        public static void Clear()
        {
            _cache.Clear();
        }

        private readonly struct CacheKey : IEquatable<CacheKey>
        {
            public readonly Drawing Drawing;
            public readonly double PlateWidth;
            public readonly double PlateHeight;
            public readonly double Spacing;

            public CacheKey(Drawing drawing, double plateWidth, double plateHeight, double spacing)
            {
                Drawing = drawing;
                PlateWidth = plateWidth;
                PlateHeight = plateHeight;
                Spacing = spacing;
            }

            public bool Equals(CacheKey other)
            {
                return ReferenceEquals(Drawing, other.Drawing) &&
                       PlateWidth == other.PlateWidth &&
                       PlateHeight == other.PlateHeight &&
                       Spacing == other.Spacing;
            }

            public override bool Equals(object obj) => obj is CacheKey other && Equals(other);

            public override int GetHashCode()
            {
                unchecked
                {
                    var hash = RuntimeHelpers.GetHashCode(Drawing);
                    hash = hash * 397 ^ PlateWidth.GetHashCode();
                    hash = hash * 397 ^ PlateHeight.GetHashCode();
                    hash = hash * 397 ^ Spacing.GetHashCode();
                    return hash;
                }
            }
        }
    }
}
