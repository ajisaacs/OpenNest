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
    /// valid beats invalid; higher aggregate utilization wins; if utilization
    /// ties and both engines fully placed every requested part, fewer plates
    /// used wins (the multi-plate analogue of "smaller remnant" - both are
    /// proxies for wasting less material). Ties beyond that are a shared win.
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
                    $"{"Engine", -16} {"Result", -9} {"Parts", -10} {"Util%", -8} {"Plates", -18} {"Time(ms)", -9} Notes"
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
                    var platesCol =
                        r.PlatesUsed > 0 ? $"{r.PlatesUsed} ({SizeSummary(r.SizeBreakdown)})" : "-";
                    var notes = r.Crashed ? r.Error : string.Join("; ", r.Violations.Take(2));

                    Console.WriteLine(
                        $"{marker}{r.EngineName, -15} {status, -9} {partsCol, -10} {utilCol, -8} {platesCol, -18} {r.ElapsedMs, -9} {notes}"
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
                    TotalUtilization = g.Sum(r => r.Utilization),
                    TotalPlates = g.Sum(r => r.PlatesUsed),
                    TotalTimeMs = g.Sum(r => r.ElapsedMs),
                })
                .OrderByDescending(e => e.TotalUtilization)
                .ToList();

            var wins = CountWins(results);

            Console.WriteLine(
                $"{"Engine", -16} {"Jobs", -6} {"Valid", -7} {"Complete", -9} {"Wins", -6} {"AvgUtil%", -10} {"Plates", -8} {"TotalTime(ms)", -14}"
            );

            foreach (var e in byEngine)
            {
                var avgUtil = e.Jobs > 0 ? e.TotalUtilization / e.Jobs * 100 : 0;
                var winCount = wins.TryGetValue(e.Engine, out var w) ? w : 0;
                Console.WriteLine(
                    $"{e.Engine, -16} {e.Jobs, -6} {e.Valid, -7} {e.FullyPlaced, -9} {winCount, -6} {avgUtil, -10:F1} {e.TotalPlates, -8} {e.TotalTimeMs, -14}"
                );
            }
        }

        public static void WriteCsv(string path, List<JobResult> results)
        {
            var sb = new StringBuilder();
            sb.AppendLine(
                "Job,Engine,Valid,Crashed,FullyPlaced,PartsPlaced,PartsRequested,Utilization,PlatesUsed,SizeBreakdown,ElapsedMs,Notes"
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

        /// <summary>Lower sorts first (better). Valid beats invalid, then higher
        /// aggregate utilization, then (if both fully placed) fewer plates used.</summary>
        private static int Compare(JobResult a, JobResult b)
        {
            if (a.Valid != b.Valid)
                return a.Valid ? -1 : 1;

            if (!a.Valid)
                return 0;

            var utilDiff = b.Utilization - a.Utilization;

            if (System.Math.Abs(utilDiff) > Epsilon)
                return utilDiff > 0 ? 1 : -1;

            if (a.FullyPlaced && b.FullyPlaced && a.PlatesUsed != b.PlatesUsed)
                return a.PlatesUsed > b.PlatesUsed ? 1 : -1;

            return 0;
        }
    }
}
