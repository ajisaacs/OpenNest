using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using OpenNest.Engine.Jobs;

namespace OpenNest.Engine.BestFit
{
    /// <summary>
    /// Best-fit pair results per drawing. Entries are keyed weakly by the source drawing (canonical
    /// copies resolve through <see cref="CanonicalFrame.SourceOf"/>), so every fill of one drawing
    /// shares them and closed nests or finished solves release theirs. Candidates are computed once
    /// per spacing and filtered per plate size. An entry is dropped when the drawing's
    /// <see cref="Drawing.Program"/> instance or canonical angle changes.
    /// <para>
    /// A drawing reconstructed from job geometry (<see cref="ShareByGeometry"/>) draws its
    /// candidates from a process-wide pool keyed by the geometry's exact content and the spacing,
    /// so every later job of the same geometry, in any engine instance or Auto Nest run, skips the
    /// search. Candidates depend on the program geometry and spacing alone, so a pooled set is
    /// the set the search would produce. The pool keeps the <see cref="SharedCapacity"/> most
    /// recently used sets and outlives the drawings; <see cref="Invalidate"/> never touches it.
    /// </para>
    /// </summary>
    public static class BestFitCache
    {
        private const double StepSize = 0.25;

        private static readonly ConditionalWeakTable<Drawing, Entry> _entries = new();
        private static readonly object _entriesLock = new();

        private static readonly ConditionalWeakTable<Drawing, SharedGeometry> _geometry = new();
        private static readonly object _poolLock = new();
        private static readonly LinkedList<PoolEntry> _poolOrder = new();
        private static readonly Dictionary<(PartGeometrySnapshot Geometry, double Spacing), LinkedListNode<PoolEntry>>
            _pool = new(new PoolKeyComparer());
        private static int _sharedCapacity = 512;

        public static Func<Drawing, double, IPairEvaluator> CreateEvaluator { get; set; }
        public static Func<ISlideComputer> CreateSlideComputer { get; set; }

        /// <summary>
        /// Candidate sets (one per geometry and spacing) the process-wide pool keeps; the least
        /// recently used set is dropped first. Zero disables pooling for later computations.
        /// </summary>
        public static int SharedCapacity
        {
            get => Volatile.Read(ref _sharedCapacity);
            set
            {
                if (value < 0)
                    throw new ArgumentOutOfRangeException(nameof(value));
                lock (_poolLock)
                {
                    _sharedCapacity = value;
                    Trim();
                }
            }
        }

        /// <summary>
        /// Declares <paramref name="drawing"/> an exact reconstruction of <paramref name="geometry"/>
        /// (its program unrotated, as <c>DrawingJobMapper.ToProgram</c> builds it). Its unfiltered
        /// candidates then come from, and go to, the process-wide pool for that geometry content
        /// and spacing. The declaration lapses if the drawing's <see cref="Drawing.Program"/>
        /// instance is replaced.
        /// </summary>
        public static void ShareByGeometry(Drawing drawing, PartGeometrySnapshot geometry)
        {
            ArgumentNullException.ThrowIfNull(drawing);
            ArgumentNullException.ThrowIfNull(geometry);
            _geometry.AddOrUpdate(drawing, new SharedGeometry(geometry, drawing.Program));
        }

        /// <summary>
        /// True when the pool holds computed candidates for this geometry content and spacing, so
        /// a drawing sharing it would skip the search.
        /// </summary>
        public static bool IsShared(PartGeometrySnapshot geometry, double spacing)
        {
            ArgumentNullException.ThrowIfNull(geometry);
            lock (_poolLock)
                return _pool.TryGetValue((geometry, spacing), out var node)
                    && node.Value.Candidates.IsValueCreated;
        }

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

        /// <summary>
        /// Like <see cref="GetOrCompute(Drawing, double, double, double)"/>, but canceling stops
        /// the wait, not the computation: it finishes in the background and caches its result.
        /// Callers must pass a drawing nothing else mutates, such as a canonical copy.
        /// </summary>
        public static List<BestFitResult> GetOrCompute(
            Drawing drawing,
            double plateWidth,
            double plateHeight,
            double spacing,
            CancellationToken token
        )
        {
            if (!token.CanBeCanceled)
                return GetOrCompute(drawing, plateWidth, plateHeight, spacing);

            token.ThrowIfCancellationRequested();
            return Task.Run(() => GetOrCompute(drawing, plateWidth, plateHeight, spacing))
                .WaitAsync(token)
                .GetAwaiter()
                .GetResult();
        }

        /// <summary>True when <see cref="GetOrCompute(Drawing, double, double, double)"/> would return at once.</summary>
        public static bool IsCached(Drawing drawing, double plateWidth, double plateHeight, double spacing)
        {
            var source = CanonicalFrame.SourceOf(drawing);
            return _entries.TryGetValue(source, out var entry)
                && entry.IsCurrentFor(source)
                && entry.Filtered.ContainsKey((plateWidth, plateHeight, spacing));
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

        /// <summary>Drops every drawing's entry and the shared pool.</summary>
        public static void Clear()
        {
            lock (_entriesLock)
                _entries.Clear();
            lock (_poolLock)
            {
                _pool.Clear();
                _poolOrder.Clear();
            }
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
            var shared = SharedGeometryOf(CanonicalFrame.SourceOf(drawing));
            var lazy = entry.Unfiltered.GetOrAdd(
                spacing,
                _ =>
                {
                    var computation = new Lazy<List<BestFitResult>>(
                        () => ComputeCandidates(drawing, spacing),
                        LazyThreadSafetyMode.ExecutionAndPublication
                    );
                    return shared == null ? computation : PoolGetOrAdd(shared, spacing, computation);
                }
            );

            try
            {
                return lazy.Value;
            }
            catch
            {
                // Don't cache the failure; the next caller retries.
                entry.Unfiltered.TryRemove(new KeyValuePair<double, Lazy<List<BestFitResult>>>(spacing, lazy));
                if (shared != null)
                    PoolRemove(shared, spacing, lazy);
                throw;
            }
        }

        private static PartGeometrySnapshot SharedGeometryOf(Drawing source)
        {
            if (source == null || !_geometry.TryGetValue(source, out var shared))
                return null;
            // A replaced program no longer matches the declared geometry.
            return ReferenceEquals(shared.Program, source.Program) ? shared.Geometry : null;
        }

        /// <summary>The pooled computation for this geometry and spacing, adding
        /// <paramref name="computation"/> when there is none; a hit becomes most recently used.</summary>
        private static Lazy<List<BestFitResult>> PoolGetOrAdd(
            PartGeometrySnapshot geometry,
            double spacing,
            Lazy<List<BestFitResult>> computation
        )
        {
            lock (_poolLock)
            {
                var key = (geometry, spacing);
                if (_pool.TryGetValue(key, out var node))
                {
                    _poolOrder.Remove(node);
                    _poolOrder.AddFirst(node);
                    return node.Value.Candidates;
                }

                if (_sharedCapacity == 0)
                    return computation;

                node = _poolOrder.AddFirst(new PoolEntry(geometry, spacing, computation));
                _pool[key] = node;
                Trim();
                return computation;
            }
        }

        private static void PoolRemove(PartGeometrySnapshot geometry, double spacing, Lazy<List<BestFitResult>> computation)
        {
            lock (_poolLock)
            {
                var key = (geometry, spacing);
                if (_pool.TryGetValue(key, out var node) && ReferenceEquals(node.Value.Candidates, computation))
                {
                    _pool.Remove(key);
                    _poolOrder.Remove(node);
                }
            }
        }

        // Caller holds _poolLock.
        private static void Trim()
        {
            while (_poolOrder.Count > _sharedCapacity)
            {
                var last = _poolOrder.Last;
                _pool.Remove((last.Value.Geometry, last.Value.Spacing));
                _poolOrder.RemoveLast();
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

        /// <summary>The geometry a drawing was reconstructed from, and the program built then.</summary>
        private sealed record SharedGeometry(PartGeometrySnapshot Geometry, CNC.Program Program);

        private sealed record PoolEntry(
            PartGeometrySnapshot Geometry,
            double Spacing,
            Lazy<List<BestFitResult>> Candidates
        );

        private sealed class PoolKeyComparer
            : IEqualityComparer<(PartGeometrySnapshot Geometry, double Spacing)>
        {
            public bool Equals(
                (PartGeometrySnapshot Geometry, double Spacing) x,
                (PartGeometrySnapshot Geometry, double Spacing) y
            ) =>
                x.Spacing.Equals(y.Spacing)
                && PartGeometrySnapshot.ContentComparer.Equals(x.Geometry, y.Geometry);

            public int GetHashCode((PartGeometrySnapshot Geometry, double Spacing) key) =>
                HashCode.Combine(PartGeometrySnapshot.ContentComparer.GetHashCode(key.Geometry), key.Spacing);
        }
    }
}
