using System.Diagnostics;
using System.Threading;

namespace OpenNest
{
    /// <summary>
    /// Debug-only call counters for the fill hot paths. They confirm that a cache or shortcut
    /// actually removes the work it claims to; Release builds compile the increments away.
    /// </summary>
    public static class PerfCounters
    {
        private static long findBestFits;
        private static long offsetPerimeterEntities;
        private static long partIntersects;
        private static long fillScoreComputations;
        private static long partBoundaryPreparations;
        private static long partBoundsUpdates;

        public static long FindBestFits => Interlocked.Read(ref findBestFits);
        public static long OffsetPerimeterEntities => Interlocked.Read(ref offsetPerimeterEntities);
        public static long PartIntersects => Interlocked.Read(ref partIntersects);
        public static long FillScoreComputations => Interlocked.Read(ref fillScoreComputations);
        public static long PartBoundaryPreparations => Interlocked.Read(ref partBoundaryPreparations);
        public static long PartBoundsUpdates => Interlocked.Read(ref partBoundsUpdates);

        [Conditional("DEBUG")]
        public static void CountFindBestFits() => Interlocked.Increment(ref findBestFits);

        [Conditional("DEBUG")]
        public static void CountOffsetPerimeterEntities() =>
            Interlocked.Increment(ref offsetPerimeterEntities);

        [Conditional("DEBUG")]
        public static void CountPartIntersects() => Interlocked.Increment(ref partIntersects);

        [Conditional("DEBUG")]
        public static void CountFillScoreComputation() => Interlocked.Increment(ref fillScoreComputations);

        [Conditional("DEBUG")]
        public static void CountPartBoundaryPreparation() => Interlocked.Increment(ref partBoundaryPreparations);

        [Conditional("DEBUG")]
        public static void CountPartBoundsUpdate() => Interlocked.Increment(ref partBoundsUpdates);

        public static void Reset()
        {
            Interlocked.Exchange(ref findBestFits, 0);
            Interlocked.Exchange(ref offsetPerimeterEntities, 0);
            Interlocked.Exchange(ref partIntersects, 0);
            Interlocked.Exchange(ref fillScoreComputations, 0);
            Interlocked.Exchange(ref partBoundaryPreparations, 0);
            Interlocked.Exchange(ref partBoundsUpdates, 0);
        }
    }
}
