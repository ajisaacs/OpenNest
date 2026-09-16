using System.Collections.Generic;

namespace OpenNest.Benchmark
{
    /// <summary>
    /// Outcome of running one engine against one job. A job may span several
    /// plates (PlatesUsed, SizeBreakdown), since the engine may need more than
    /// one sheet - possibly of different sizes - to place everything asked of
    /// it. An invalid or crashed run always scores zero utilization, per the
    /// benchmark rules.
    /// </summary>
    public class JobResult
    {
        public string EngineName { get; init; }
        public string JobName { get; init; }
        public bool Valid { get; init; }
        public List<string> Violations { get; init; } = new();
        public string Error { get; init; }
        public int PartsPlaced { get; init; }
        public int PartsRequested { get; init; }
        public double PlacedArea { get; init; }
        public double PlateArea { get; init; }
        public int PlatesUsed { get; init; }
        public Dictionary<string, int> SizeBreakdown { get; init; } = new();
        public long ElapsedMs { get; init; }

        public bool Crashed => Error != null;
        public bool FullyPlaced => Valid && PartsRequested > 0 && PartsPlaced >= PartsRequested;

        /// <summary>Aggregate utilization across every plate the engine used:
        /// total placed drawing area over total plate area, matching
        /// Plate.Utilization()'s per-plate definition summed across the job.</summary>
        public double Utilization => Valid && PlateArea > 0 ? PlacedArea / PlateArea : 0;
    }
}
