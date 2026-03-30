using OpenNest.Engine.ML;
using OpenNest.Geometry;
using OpenNest.Math;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace OpenNest.Engine.Fill
{
    public class AngleCandidateBuilder
    {
        private readonly HashSet<double> knownGoodAngles = new();

        public bool ForceFullSweep { get; set; }

        public List<double> Build(NestItem item, ClassificationResult classification, Box workArea)
        {
            // User constraints always take precedence over classification.
            if (HasExplicitConstraints(item))
                return BuildFromConstraints(item);

            switch (classification.Type)
            {
                case PartType.Circle:
                    return new List<double> { 0 };

                case PartType.Rectangle:
                    return new List<double> { classification.PrimaryAngle, classification.PrimaryAngle + Angle.HalfPI };

                default:
                    return BuildIrregularAngles(item, classification.PrimaryAngle, workArea);
            }
        }

        private static bool HasExplicitConstraints(NestItem item)
        {
            // Default NestConstraints: Start=0, End=0. Both zero = no constraints.
            return !(item.RotationStart.IsEqualTo(0) && item.RotationEnd.IsEqualTo(0));
        }

        private static List<double> BuildFromConstraints(NestItem item)
        {
            var angles = new List<double>();
            var step = item.StepAngle > Tolerance.Epsilon ? item.StepAngle : Angle.ToRadians(5);

            for (var a = item.RotationStart; a <= item.RotationEnd + Tolerance.Epsilon; a += step)
            {
                if (!ContainsAngle(angles, a))
                    angles.Add(a);
            }

            if (angles.Count == 0)
                angles.Add(item.RotationStart);

            return angles;
        }

        private List<double> BuildIrregularAngles(NestItem item, double primaryAngle, Box workArea)
        {
            var baseAngles = new[] { primaryAngle, primaryAngle + Angle.HalfPI };

            if (knownGoodAngles.Count > 0 && !ForceFullSweep)
                return BuildPrunedList(baseAngles);

            var angles = new List<double>(baseAngles);

            // Full 5-degree sweep for irregular parts.
            AddSweepAngles(angles);

            // ML prediction complements the sweep when available.
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

            // Merge ML angles into the existing sweep so both contribute.
            foreach (var a in fallback)
            {
                if (!ContainsAngle(mlAngles, a))
                    mlAngles.Add(a);
            }

            Debug.WriteLine($"[AngleCandidateBuilder] ML: {fallback.Count} sweep + {predicted.Count} predicted = {mlAngles.Count} total");
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
