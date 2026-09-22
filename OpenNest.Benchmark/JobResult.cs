using System.Collections.Generic;

namespace OpenNest.Benchmark
{
    /// <summary>
    /// Outcome of running one engine against one job. A job may span several
    /// plates (PlatesUsed, SizeBreakdown), since the engine may need more than
    /// one sheet - possibly of different sizes - to place everything asked of
    /// it. An invalid or crashed run places nothing as far as scoring is
    /// concerned: it earns no area and pays the unplaced penalty on every
    /// requested part.
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

        /// <summary>Sheet area consumed after crediting salvageable offcuts
        /// (StockLadderNestingEngine.EstimateNetArea summed over every plate).
        /// Equals PlateArea when salvage credit is disabled.</summary>
        public double NetSheetArea { get; init; }

        /// <summary>Sheet area charged for each requested part that was not
        /// placed: the largest candidate sheet's area, so leaving a part out
        /// always costs at least as much as the extra sheet it would need.</summary>
        public double UnplacedPartPenalty { get; init; }

        public int PlatesUsed { get; init; }
        public Dictionary<string, int> SizeBreakdown { get; init; } = new();
        public long ElapsedMs { get; init; }

        public bool Crashed => Error != null;
        public bool FullyPlaced => Valid && PartsRequested > 0 && PartsPlaced >= PartsRequested;

        /// <summary>Aggregate utilization across every plate the engine used:
        /// total placed drawing area over total plate area, matching
        /// Plate.Utilization()'s per-plate definition summed across the job.</summary>
        public double Utilization => Valid && PlateArea > 0 ? PlacedArea / PlateArea : 0;

        /// <summary>Placed area over salvage-credited sheet area.</summary>
        public double NetUtilization =>
            Valid && NetSheetArea > 0 ? PlacedArea / NetSheetArea : 0;

        public int PartsUnplaced =>
            Valid ? System.Math.Max(0, PartsRequested - PartsPlaced) : PartsRequested;

        /// <summary>
        /// The ranking score, in sheet area (lower is better): net sheet area
        /// consumed plus the unplaced penalty. An engine cannot improve it by
        /// dropping awkward parts, and it sums honestly across jobs of
        /// different sizes. Invalid runs consume no sheet but pay the penalty
        /// on every requested part.
        /// </summary>
        public double Cost => (Valid ? NetSheetArea : 0) + PartsUnplaced * UnplacedPartPenalty;
    }
}
