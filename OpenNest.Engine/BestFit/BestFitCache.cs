using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;

namespace OpenNest.Engine.BestFit
{
    /// <summary>
    /// Best-fit pair results per drawing. Entries are keyed weakly by the source drawing (canonical
    /// copies resolve through <see cref="CanonicalFrame.SourceOf"/>), so every fill of one drawing
    /// shares them and closed nests or finished solves release theirs. Candidates are computed once
    /// per spacing and filtered per plate size. An entry is dropped when the drawing's
    /// <see cref="Drawing.Program"/> instance or canonical angle changes.
    /// </summary>
    public static class BestFitCache
    {
        private const double StepSize = 0.25;

        private static readonly ConditionalWeakTable<Drawing, Entry> _entries = new();
        private static readonly object _entriesLock = new();

        public static Func<Drawing, double, IPairEvaluator> CreateEvaluator { get; set; }
        public static Func<ISlideComputer> CreateSlideComputer { get; set; }

        public static List<BestFitResult> GetOrCompute(
            Drawing drawing,
            double plateWidth,
            double plateHeight,
            double spacing
        )
        {
            var entry = GetEntry(drawing);
            var key = (plateWidth, plateHeight, spacing);

            if (entry.Filtered.TryGetValue(key, out var cached))
                return cached;

            var candidates = GetCandidates(entry, drawing, spacing);
            return entry.Filtered.GetOrAdd(key, _ => FilterForSize(candidates, plateWidth, plateHeight));
        }

        public static void ComputeForSizes(
            Drawing drawing,
            double spacing,
            IEnumerable<(double Width, double Height)> plateSizes
        )
        {
            foreach (var size in plateSizes)
                GetOrCompute(drawing, size.Width, size.Height, spacing);
        }

        public static void Invalidate(Drawing drawing)
        {
            lock (_entriesLock)
                _entries.Remove(CanonicalFrame.SourceOf(drawing));
        }

        public static void Populate(
            Drawing drawing,
            double plateWidth,
            double plateHeight,
            double spacing,
            List<BestFitResult> results
        )
        {
            if (results == null || results.Count == 0)
                return;

            GetEntry(drawing).Filtered.TryAdd((plateWidth, plateHeight, spacing), results);
        }

        public static Dictionary<
            (double PlateWidth, double PlateHeight, double Spacing),
            List<BestFitResult>
        > GetAllForDrawing(Drawing drawing)
        {
            var result = new Dictionary<(double, double, double), List<BestFitResult>>();
            var source = CanonicalFrame.SourceOf(drawing);

            if (_entries.TryGetValue(source, out var entry) && entry.IsCurrentFor(source))
            {
                foreach (var kvp in entry.Filtered)
                    result[kvp.Key] = kvp.Value;
            }

            return result;
        }

        public static void Clear()
        {
            lock (_entriesLock)
                _entries.Clear();
        }

        private static Entry GetEntry(Drawing drawing)
        {
            var source = CanonicalFrame.SourceOf(drawing);

            if (_entries.TryGetValue(source, out var entry) && entry.IsCurrentFor(source))
                return entry;

            lock (_entriesLock)
            {
                if (_entries.TryGetValue(source, out entry) && entry.IsCurrentFor(source))
                    return entry;

                entry = new Entry(source);
                _entries.AddOrUpdate(source, entry);
                return entry;
            }
        }

        private static List<BestFitResult> GetCandidates(Entry entry, Drawing drawing, double spacing)
        {
            var lazy = entry.Unfiltered.GetOrAdd(
                spacing,
                _ => new Lazy<List<BestFitResult>>(
                    () => ComputeCandidates(drawing, spacing),
                    LazyThreadSafetyMode.ExecutionAndPublication
                )
            );

            try
            {
                return lazy.Value;
            }
            catch
            {
                // Don't cache the failure; the next caller retries.
                entry.Unfiltered.TryRemove(new KeyValuePair<double, Lazy<List<BestFitResult>>>(spacing, lazy));
                throw;
            }
        }

        private static List<BestFitResult> ComputeCandidates(Drawing drawing, double spacing)
        {
            // Operate on the canonical frame so cached pair positions are orientation-invariant.
            var canonical = CanonicalFrame.AsCanonicalCopy(drawing);

            IPairEvaluator evaluator = null;
            ISlideComputer slideComputer = null;

            try
            {
                if (CreateEvaluator != null)
                {
                    try
                    {
                        evaluator = CreateEvaluator(canonical, spacing);
                    }
                    catch
                    { /* fall back to default evaluator */
                    }
                }

                if (CreateSlideComputer != null)
                {
                    try
                    {
                        slideComputer = CreateSlideComputer();
                    }
                    catch
                    { /* fall back to CPU slide computation */
                    }
                }

                // The plate size only feeds the filter, which FindCandidates skips.
                var finder = new BestFitFinder(0, 0, evaluator, slideComputer);
                return finder.FindCandidates(canonical, spacing, StepSize);
            }
            finally
            {
                (evaluator as IDisposable)?.Dispose();
                // Slide computer is managed by the factory as a singleton — don't dispose here
            }
        }

        private static List<BestFitResult> FilterForSize(
            List<BestFitResult> candidates,
            double plateWidth,
            double plateHeight
        )
        {
            var copy = new List<BestFitResult>(candidates.Count);
            for (var i = 0; i < candidates.Count; i++)
            {
                var r = candidates[i];
                copy.Add(
                    new BestFitResult
                    {
                        Candidate = r.Candidate,
                        RotatedArea = r.RotatedArea,
                        BoundingWidth = r.BoundingWidth,
                        BoundingHeight = r.BoundingHeight,
                        OptimalRotation = r.OptimalRotation,
                        TrueArea = r.TrueArea,
                        HullAngles = r.HullAngles,
                        Keep = r.Keep,
                        Reason = r.Reason,
                    }
                );
            }

            BestFitFinder.CreateFilter(plateWidth, plateHeight).Apply(copy);
            return copy;
        }

        private sealed class Entry
        {
            private readonly CNC.Program _program;
            private readonly double _sourceAngle;

            public readonly ConcurrentDictionary<double, Lazy<List<BestFitResult>>> Unfiltered = new();

            public readonly ConcurrentDictionary<
                (double PlateWidth, double PlateHeight, double Spacing),
                List<BestFitResult>
            > Filtered = new();

            public Entry(Drawing source)
            {
                _program = source.Program;
                _sourceAngle = source.Source?.Angle ?? 0.0;
            }

            public bool IsCurrentFor(Drawing source) =>
                ReferenceEquals(_program, source.Program)
                && _sourceAngle == (source.Source?.Angle ?? 0.0);
        }
    }
}
