using OpenNest.Engine.ML;
using OpenNest.Geometry;
using OpenNest.Math;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace OpenNest.Engine.Fill
{
    /// <summary>
    /// Builds candidate rotation angles for single-item fill. Encapsulates the
    /// full pipeline: base angles, narrow-area sweep, ML prediction, and
    /// known-good pruning across fills.
    /// </summary>
    public class AngleCandidateBuilder
    {
        private readonly HashSet<double> knownGoodAngles = new();

        public bool ForceFullSweep { get; set; }

        public List<double> Build(NestItem item, double bestRotation, Box workArea)
        {
            var baseAngles = new[] { bestRotation, bestRotation + Angle.HalfPI };

            if (knownGoodAngles.Count > 0 && !ForceFullSweep)
                return BuildPrunedList(baseAngles);

            var angles = new List<double>(baseAngles);

            if (ForceFullSweep)
                AddSweepAngles(angles);

            if (!ForceFullSweep && angles.Count > 2)
                angles = ApplyMlPrediction(item, workArea, baseAngles, angles);

            return angles;
        }

        private static void AddSweepAngles(List<double> angles)
        {
            var step = Angle.ToRadians(5);
            for (var a = 0.0; a < System.Math.PI; a += step)
            {
                if (!ContainsAngle(angles, a))
                    angles.Add(a);
            }
        }

        private static List<double> ApplyMlPrediction(
            NestItem item, Box workArea, double[] baseAngles, List<double> fallback)
        {
            var features = FeatureExtractor.Extract(item.Drawing);
            if (features == null)
                return fallback;

            var predicted = AnglePredictor.PredictAngles(features, workArea.Width, workArea.Length);
            if (predicted == null)
                return fallback;

            var mlAngles = new List<double>(predicted);
            foreach (var b in baseAngles)
            {
                if (!ContainsAngle(mlAngles, b))
                    mlAngles.Add(b);
            }

            Debug.WriteLine($"[AngleCandidateBuilder] ML: {fallback.Count} angles -> {mlAngles.Count} predicted");
            return mlAngles;
        }

        private List<double> BuildPrunedList(double[] baseAngles)
        {
            var pruned = new List<double>(baseAngles);
            foreach (var a in knownGoodAngles)
            {
                if (!ContainsAngle(pruned, a))
                    pruned.Add(a);
            }

            Debug.WriteLine($"[AngleCandidateBuilder] Pruned to {pruned.Count} angles (known-good)");
            return pruned;
        }

        private static bool ContainsAngle(List<double> angles, double angle)
        {
            return angles.Any(existing => existing.IsEqualTo(angle));
        }

        /// <summary>
        /// Records angles that produced results. These are used to prune
        /// subsequent Build() calls.
        /// </summary>
        public void RecordProductive(List<AngleResult> angleResults)
        {
            foreach (var ar in angleResults)
            {
                if (ar.PartCount > 0)
                    knownGoodAngles.Add(Angle.ToRadians(ar.AngleDeg));
            }
        }
    }
}
