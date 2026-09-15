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
    /// valid beats invalid; higher utilization wins; if utilization ties and both
    /// engines fully placed every requested part, the smaller used-bounding-box
    /// (more compact remnant) wins. Ties beyond that are a shared win.
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

                Console.WriteLine($"{"Engine",-16} {"Result",-9} {"Parts",-10} {"Util%",-8} {"Remnant",-12} {"Time(ms)",-9} Notes");

                foreach (var r in ranked)
                {
                    var isWinner = best != null && Compare(r, best) == 0 && r.Valid;
                    var marker = isWinner ? "*" : " ";
                    var status = r.Crashed ? "CRASH" : r.Valid ? "ok" : "INVALID";
                    var partsCol = $"{r.PartsPlaced}/{r.PartsRequested}";
                    var utilCol = r.Valid ? $"{r.Utilization * 100:F1}" : "-";
                    var remnantCol = r.Valid ? $"{r.UsedBoundingBoxArea:F0}" : "-";
                    var notes = r.Crashed ? r.Error : string.Join("; ", r.Violations.Take(2));

                    Console.WriteLine($"{marker}{r.EngineName,-15} {status,-9} {partsCol,-10} {utilCol,-8} {remnantCol,-12} {r.ElapsedMs,-9} {notes}");
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
                    TotalUtilization = g.Sum(r => r.Utilization),
                    TotalTimeMs = g.Sum(r => r.ElapsedMs),
                })
                .OrderByDescending(e => e.TotalUtilization)
                .ToList();

            var wins = CountWins(results);

            Console.WriteLine($"{"Engine",-16} {"Jobs",-6} {"Valid",-7} {"Crashed",-8} {"Wins",-6} {"AvgUtil%",-10} {"TotalTime(ms)",-14}");

            foreach (var e in byEngine)
            {
                var avgUtil = e.Jobs > 0 ? e.TotalUtilization / e.Jobs * 100 : 0;
                var winCount = wins.TryGetValue(e.Engine, out var w) ? w : 0;
                Console.WriteLine($"{e.Engine,-16} {e.Jobs,-6} {e.Valid,-7} {e.Crashed,-8} {winCount,-6} {avgUtil,-10:F1} {e.TotalTimeMs,-14}");
            }
        }

        public static void WriteCsv(string path, List<JobResult> results)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Job,Engine,Valid,Crashed,PartsPlaced,PartsRequested,Utilization,UsedBoundingBoxArea,ElapsedMs,Notes");

            foreach (var r in results)
            {
                var notes = r.Crashed ? r.Error : string.Join(" | ", r.Violations);
                sb.AppendLine(string.Join(",",
                    Csv(r.JobName), Csv(r.EngineName), r.Valid, r.Crashed,
                    r.PartsPlaced, r.PartsRequested,
                    r.Utilization.ToString("F4", CultureInfo.InvariantCulture),
                    r.UsedBoundingBoxArea.ToString("F2", CultureInfo.InvariantCulture),
                    r.ElapsedMs, Csv(notes)));
            }

            File.WriteAllText(path, sb.ToString());
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
        /// utilization, then (if both fully placed) smaller used-bounding-box.</summary>
        private static int Compare(JobResult a, JobResult b)
        {
            if (a.Valid != b.Valid)
                return a.Valid ? -1 : 1;

            if (!a.Valid)
                return 0;

            var utilDiff = b.Utilization - a.Utilization;

            if (System.Math.Abs(utilDiff) > Epsilon)
                return utilDiff > 0 ? 1 : -1;

            if (a.FullyPlaced && b.FullyPlaced)
            {
                var bboxDiff = a.UsedBoundingBoxArea - b.UsedBoundingBoxArea;

                if (System.Math.Abs(bboxDiff) > Epsilon)
                    return bboxDiff > 0 ? 1 : -1;
            }

            return 0;
        }
    }
}
