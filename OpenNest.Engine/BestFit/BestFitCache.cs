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
        public static Func<ISlideComputer> CreateSlideComputer { get; set; }

        public static List<BestFitResult> GetOrCompute(
            Drawing drawing, double plateWidth, double plateHeight,
            double spacing)
        {
            var key = new CacheKey(drawing, plateWidth, plateHeight, spacing);

            if (_cache.TryGetValue(key, out var cached))
                return cached;

            // Operate on the canonical frame so cached pair positions are orientation-invariant.
            var canonical = CanonicalFrame.AsCanonicalCopy(drawing);

            IPairEvaluator evaluator = null;
            ISlideComputer slideComputer = null;

            try
            {
                if (CreateEvaluator != null)
                {
                    try { evaluator = CreateEvaluator(canonical, spacing); }
                    catch { /* fall back to default evaluator */ }
                }

                if (CreateSlideComputer != null)
                {
                    try { slideComputer = CreateSlideComputer(); }
                    catch { /* fall back to CPU slide computation */ }
                }

                var finder = new BestFitFinder(plateWidth, plateHeight, evaluator, slideComputer);
                var results = finder.FindBestFits(canonical, spacing, StepSize);

                _cache.TryAdd(key, results);
                return results;
            }
            finally
            {
                (evaluator as IDisposable)?.Dispose();
                // Slide computer is managed by the factory as a singleton — don't dispose here
            }
        }

        public static void ComputeForSizes(
            Drawing drawing, double spacing,
            IEnumerable<(double Width, double Height)> plateSizes)
        {
            // Skip sizes that are already cached.
            var needed = new List<(double Width, double Height)>();
            foreach (var size in plateSizes)
            {
                var key = new CacheKey(drawing, size.Width, size.Height, spacing);
                if (!_cache.ContainsKey(key))
                    needed.Add(size);
            }

            if (needed.Count == 0)
                return;

            // Find the largest plate to use for the initial computation — this
            // keeps the filter maximally permissive so we don't discard results
            // that a smaller plate might still use after re-filtering.
            var maxWidth = 0.0;
            var maxHeight = 0.0;
            foreach (var size in needed)
            {
                if (size.Width > maxWidth) maxWidth = size.Width;
                if (size.Height > maxHeight) maxHeight = size.Height;
            }

            IPairEvaluator evaluator = null;
            ISlideComputer slideComputer = null;

            try
            {
                // Operate on the canonical frame so cached pair positions are orientation-invariant.
                var canonical = CanonicalFrame.AsCanonicalCopy(drawing);

                if (CreateEvaluator != null)
                {
                    try { evaluator = CreateEvaluator(canonical, spacing); }
                    catch { /* fall back to default evaluator */ }
                }

                if (CreateSlideComputer != null)
                {
                    try { slideComputer = CreateSlideComputer(); }
                    catch { /* fall back to CPU slide computation */ }
                }

                // Compute candidates and evaluate once with the largest plate.
                var finder = new BestFitFinder(maxWidth, maxHeight, evaluator, slideComputer);
                var baseResults = finder.FindBestFits(canonical, spacing, StepSize);

                // Cache a filtered copy for each plate size.
                foreach (var size in needed)
                {
                    var filter = new BestFitFilter
                    {
                        MaxPlateWidth = size.Width,
                        MaxPlateHeight = size.Height
                    };

                    var copy = new List<BestFitResult>(baseResults.Count);
                    for (var i = 0; i < baseResults.Count; i++)
                    {
                        var r = baseResults[i];
                        copy.Add(new BestFitResult
                        {
                            Candidate = r.Candidate,
                            RotatedArea = r.RotatedArea,
                            BoundingWidth = r.BoundingWidth,
                            BoundingHeight = r.BoundingHeight,
                            OptimalRotation = r.OptimalRotation,
                            TrueArea = r.TrueArea,
                            HullAngles = r.HullAngles,
                            Keep = r.Keep,
                            Reason = r.Reason
                        });
                    }

                    filter.Apply(copy);

                    var key = new CacheKey(drawing, size.Width, size.Height, spacing);
                    _cache.TryAdd(key, copy);
                }
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

        public static void Populate(Drawing drawing, double plateWidth, double plateHeight,
            double spacing, List<BestFitResult> results)
        {
            if (results == null || results.Count == 0)
                return;

            var key = new CacheKey(drawing, plateWidth, plateHeight, spacing);
            _cache.TryAdd(key, results);
        }

        public static Dictionary<(double PlateWidth, double PlateHeight, double Spacing), List<BestFitResult>>
            GetAllForDrawing(Drawing drawing)
        {
            var result = new Dictionary<(double, double, double), List<BestFitResult>>();
            foreach (var kvp in _cache)
            {
                if (ReferenceEquals(kvp.Key.Drawing, drawing))
                    result[(kvp.Key.PlateWidth, kvp.Key.PlateHeight, kvp.Key.Spacing)] = kvp.Value;
            }
            return result;
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
