using System.Collections.Generic;

namespace OpenNest.Benchmark
{
    /// <summary>
    /// Outcome of running one engine against one job. An invalid or crashed run
    /// always scores zero utilization for that job, per the benchmark rules.
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
        public double UsedBoundingBoxArea { get; init; }
        public long ElapsedMs { get; init; }

        public bool Crashed => Error != null;
        public bool FullyPlaced => Valid && PartsRequested > 0 && PartsPlaced >= PartsRequested;
        public double Utilization => Valid && PlateArea > 0 ? PlacedArea / PlateArea : 0;
    }
}
