using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using OpenNest.Geometry;

namespace OpenNest.Engine.ML
{
    public class BruteForceResult
    {
        public int PartCount { get; set; }
        public double Utilization { get; set; }
        public long TimeMs { get; set; }
        public string LayoutData { get; set; }
        public List<Part> PlacedParts { get; set; }
        public string WinnerEngine { get; set; } = "";
        public long WinnerTimeMs { get; set; }
        public string RunnerUpEngine { get; set; } = "";
        public int RunnerUpPartCount { get; set; }
        public long RunnerUpTimeMs { get; set; }
        public string ThirdPlaceEngine { get; set; } = "";
        public int ThirdPlacePartCount { get; set; }
        public long ThirdPlaceTimeMs { get; set; }
        public List<AngleResult> AngleResults { get; set; } = new();
    }

    public static class BruteForceRunner
    {
        public static BruteForceResult Run(Drawing drawing, Plate plate, bool forceFullAngleSweep = false)
        {
            var engine = new DefaultNestEngine(plate);
            engine.ForceFullAngleSweep = forceFullAngleSweep;
            var item = new NestItem { Drawing = drawing };

            var sw = Stopwatch.StartNew();
            var parts = engine.Fill(item, plate.WorkArea(), null, System.Threading.CancellationToken.None);
            sw.Stop();

            if (parts == null || parts.Count == 0)
                return null;

            // Rank phase results — winner is explicit, runners-up sorted by count.
            var winner = engine.PhaseResults
                .FirstOrDefault(r => r.Phase == engine.WinnerPhase);
            var runnerUps = engine.PhaseResults
                .Where(r => r.PartCount > 0 && r.Phase != engine.WinnerPhase)
                .OrderByDescending(r => r.PartCount)
                .ToList();

            return new BruteForceResult
            {
                PartCount = parts.Count,
                Utilization = CalculateUtilization(parts, plate.Area()),
                TimeMs = sw.ElapsedMilliseconds,
                LayoutData = SerializeLayout(parts),
                PlacedParts = parts,
                WinnerEngine = engine.WinnerPhase.ToString(),
                WinnerTimeMs = winner?.TimeMs ?? 0,
                RunnerUpEngine = runnerUps.Count > 0 ? runnerUps[0].Phase.ToString() : "",
                RunnerUpPartCount = runnerUps.Count > 0 ? runnerUps[0].PartCount : 0,
                RunnerUpTimeMs = runnerUps.Count > 0 ? runnerUps[0].TimeMs : 0,
                ThirdPlaceEngine = runnerUps.Count > 1 ? runnerUps[1].Phase.ToString() : "",
                ThirdPlacePartCount = runnerUps.Count > 1 ? runnerUps[1].PartCount : 0,
                ThirdPlaceTimeMs = runnerUps.Count > 1 ? runnerUps[1].TimeMs : 0,
                AngleResults = engine.AngleResults.ToList()
            };
        }

        private static string SerializeLayout(List<Part> parts)
        {
            var data = parts.Select(p => new { X = p.Location.X, Y = p.Location.Y, R = p.Rotation }).ToList();
            return System.Text.Json.JsonSerializer.Serialize(data);
        }

        private static double CalculateUtilization(List<Part> parts, double plateArea)
        {
            if (plateArea <= 0) return 0;
            return parts.Sum(p => p.BaseDrawing.Area) / plateArea;
        }
    }
}
