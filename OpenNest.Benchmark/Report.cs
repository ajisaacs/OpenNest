using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace OpenNest.Benchmark
{
    /// <summary>
    /// Console + CSV reporting for benchmark results. Ranking rule per job:
    /// valid beats invalid; placing every requested part beats not; then lower
    /// JobResult.Cost wins - salvage-credited sheet area consumed plus a
    /// largest-sheet penalty per unplaced part, so dropping awkward parts can
    /// never buy a better score; then fewer plates. Ties beyond that are a
    /// shared win. Across jobs, costs and areas are summed (not averaged), so
    /// a big job weighs more than a three-part one.
    /// </summary>
    public static class Report
    {
        private const double Epsilon = 1e-6;

        public static void PrintDetailed(List<JobResult> results)
        {
            foreach (var jobGroup in results.GroupBy(r => r.JobName))
            {
                Console.WriteLine();
                Console.WriteLine($"=== {jobGroup.Key} ===");

                var ranked = jobGroup.OrderBy(r => r, Comparer<JobResult>.Create(Compare)).ToList();
                var best = ranked.Count > 0 ? ranked[0] : null;

                Console.WriteLine(
                    $"{"Engine", -16} {"Result", -9} {"Parts", -10} {"Util%", -7} {"Net%", -7} {"Cost", -12} {"Plates", -18} {"Time(ms)", -9} Notes"
                );

                foreach (var r in ranked)
                {
                    var isWinner = best != null && Compare(r, best) == 0 && r.Valid;
                    var marker = isWinner ? "*" : " ";
                    var status =
                        r.Crashed ? "CRASH"
                        : r.Valid ? "ok"
                        : "INVALID";
                    var partsCol = $"{r.PartsPlaced}/{r.PartsRequested}";
                    var utilCol = r.Valid ? $"{r.Utilization * 100:F1}" : "-";
                    var netCol = r.Valid ? $"{r.NetUtilization * 100:F1}" : "-";
                    var platesCol =
                        r.PlatesUsed > 0 ? $"{r.PlatesUsed} ({SizeSummary(r.SizeBreakdown)})" : "-";
                    var notes = r.Crashed ? r.Error : string.Join("; ", r.Violations.Take(2));

                    Console.WriteLine(
                        $"{marker}{r.EngineName, -15} {status, -9} {partsCol, -10} {utilCol, -7} {netCol, -7} {r.Cost, -12:F1} {platesCol, -18} {r.ElapsedMs, -9} {notes}"
                    );
                }
            }
        }

        public static void PrintSummary(List<JobResult> results)
        {
            Console.WriteLine();
            Console.WriteLine("=== Summary ===");

            var byEngine = results
                .GroupBy(r => r.EngineName)
                .Select(g => new
                {
                    Engine = g.Key,
                    Jobs = g.Count(),
                    Valid = g.Count(r => r.Valid),
                    Crashed = g.Count(r => r.Crashed),
                    FullyPlaced = g.Count(r => r.FullyPlaced),
                    Unplaced = g.Sum(r => r.PartsUnplaced),
                    PlacedArea = g.Where(r => r.Valid).Sum(r => r.PlacedArea),
                    PlateArea = g.Where(r => r.Valid).Sum(r => r.PlateArea),
                    NetSheetArea = g.Where(r => r.Valid).Sum(r => r.NetSheetArea),
                    TotalCost = g.Sum(r => r.Cost),
                    TotalPlates = g.Sum(r => r.PlatesUsed),
                    TotalTimeMs = g.Sum(r => r.ElapsedMs),
                })
                .OrderBy(e => e.TotalCost)
                .ToList();

            var wins = CountWins(results);

            // Util% and Net% are area-weighted over valid runs (sum placed / sum
            // sheet), not a mean of per-job percentages. TotalCost sums across
            // jobs, so it is only meaningful when every job uses the same units.
            Console.WriteLine(
                $"{"Engine", -16} {"Jobs", -6} {"Valid", -7} {"Complete", -9} {"Unplaced", -9} {"Wins", -6} {"Util%", -7} {"Net%", -7} {"TotalCost", -14} {"Plates", -8} {"TotalTime(ms)", -14}"
            );

            foreach (var e in byEngine)
            {
                var util = e.PlateArea > 0 ? e.PlacedArea / e.PlateArea * 100 : 0;
                var netUtil = e.NetSheetArea > 0 ? e.PlacedArea / e.NetSheetArea * 100 : 0;
                var winCount = wins.TryGetValue(e.Engine, out var w) ? w : 0;
                Console.WriteLine(
                    $"{e.Engine, -16} {e.Jobs, -6} {e.Valid, -7} {e.FullyPlaced, -9} {e.Unplaced, -9} {winCount, -6} {util, -7:F1} {netUtil, -7:F1} {e.TotalCost, -14:F1} {e.TotalPlates, -8} {e.TotalTimeMs, -14}"
                );
            }
        }

        public static void WriteCsv(string path, List<JobResult> results)
        {
            var sb = new StringBuilder();
            sb.AppendLine(
                "Job,Engine,Valid,Crashed,FullyPlaced,PartsPlaced,PartsRequested,Utilization,NetUtilization,PlateArea,NetSheetArea,Cost,PlatesUsed,SizeBreakdown,ElapsedMs,Notes"
            );

            foreach (var r in results)
            {
                var notes = r.Crashed ? r.Error : string.Join(" | ", r.Violations);
                sb.AppendLine(
                    string.Join(
                        ",",
                        Csv(r.JobName),
                        Csv(r.EngineName),
                        r.Valid,
                        r.Crashed,
                        r.FullyPlaced,
                        r.PartsPlaced,
                        r.PartsRequested,
                        r.Utilization.ToString("F4", CultureInfo.InvariantCulture),
                        r.NetUtilization.ToString("F4", CultureInfo.InvariantCulture),
                        r.PlateArea.ToString("F2", CultureInfo.InvariantCulture),
                        r.NetSheetArea.ToString("F2", CultureInfo.InvariantCulture),
                        r.Cost.ToString("F2", CultureInfo.InvariantCulture),
                        r.PlatesUsed,
                        Csv(SizeSummary(r.SizeBreakdown)),
                        r.ElapsedMs,
                        Csv(notes)
                    )
                );
            }

            File.WriteAllText(path, sb.ToString());
        }

        private static string SizeSummary(Dictionary<string, int> breakdown)
        {
            if (breakdown == null || breakdown.Count == 0)
                return "-";

            return string.Join("; ", breakdown.Select(kv => $"{kv.Key}×{kv.Value}"));
        }

        private static string Csv(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
                return $"\"{value.Replace("\"", "\"\"")}\"";

            return value;
        }

        private static Dictionary<string, int> CountWins(List<JobResult> results)
        {
            var wins = new Dictionary<string, int>();

            foreach (var jobGroup in results.GroupBy(r => r.JobName))
            {
                var ranked = jobGroup.OrderBy(r => r, Comparer<JobResult>.Create(Compare)).ToList();

                if (ranked.Count == 0 || !ranked[0].Valid)
                    continue;

                foreach (var r in ranked.TakeWhile(r => Compare(r, ranked[0]) == 0))
                    wins[r.EngineName] = wins.GetValueOrDefault(r.EngineName) + 1;
            }

            return wins;
        }

        /// <summary>Lower sorts first (better). Valid beats invalid, complete beats
        /// incomplete, then lower cost (relative tolerance, since costs are areas
        /// in whatever units the job uses), then fewer plates.</summary>
        public static int Compare(JobResult a, JobResult b)
        {
            if (a.Valid != b.Valid)
                return a.Valid ? -1 : 1;

            if (!a.Valid)
                return 0;

            if (a.FullyPlaced != b.FullyPlaced)
                return a.FullyPlaced ? -1 : 1;

            var costDiff = a.Cost - b.Cost;
            var scale = System.Math.Max(1, System.Math.Max(a.Cost, b.Cost));

            if (System.Math.Abs(costDiff) > Epsilon * scale)
                return costDiff > 0 ? 1 : -1;

            if (a.PlatesUsed != b.PlatesUsed)
                return a.PlatesUsed > b.PlatesUsed ? 1 : -1;

            return 0;
        }
    }
}
