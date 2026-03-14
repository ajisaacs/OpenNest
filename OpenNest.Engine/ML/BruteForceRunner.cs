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
    }

    public static class BruteForceRunner
    {
        public static BruteForceResult Run(Drawing drawing, Plate plate)
        {
            var engine = new NestEngine(plate);
            var item = new NestItem { Drawing = drawing };

            var sw = Stopwatch.StartNew();
            var parts = engine.Fill(item, plate.WorkArea(), null, System.Threading.CancellationToken.None);
            sw.Stop();

            if (parts == null || parts.Count == 0)
                return null;

            return new BruteForceResult
            {
                PartCount = parts.Count,
                Utilization = CalculateUtilization(parts, plate.Area()),
                TimeMs = sw.ElapsedMilliseconds,
                LayoutData = SerializeLayout(parts),
                PlacedParts = parts
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
