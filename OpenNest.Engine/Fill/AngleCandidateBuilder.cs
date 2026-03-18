using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using OpenNest.Engine.ML;
using OpenNest.Geometry;
using OpenNest.Math;

namespace OpenNest
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
            var angles = new List<double> { bestRotation, bestRotation + Angle.HalfPI };

            var testPart = new Part(item.Drawing);
            if (!bestRotation.IsEqualTo(0))
                testPart.Rotate(bestRotation);
            testPart.UpdateBounds();

            var partLongestSide = System.Math.Max(testPart.BoundingBox.Width, testPart.BoundingBox.Length);
            var workAreaShortSide = System.Math.Min(workArea.Width, workArea.Length);
            var needsSweep = workAreaShortSide < partLongestSide || ForceFullSweep;

            if (needsSweep)
            {
                var step = Angle.ToRadians(5);
                for (var a = 0.0; a < System.Math.PI; a += step)
                {
                    if (!angles.Any(existing => existing.IsEqualTo(a)))
                        angles.Add(a);
                }
            }

            if (!ForceFullSweep && angles.Count > 2)
            {
                var features = FeatureExtractor.Extract(item.Drawing);
                if (features != null)
                {
                    var predicted = AnglePredictor.PredictAngles(
                        features, workArea.Width, workArea.Length);

                    if (predicted != null)
                    {
                        var mlAngles = new List<double>(predicted);

                        if (!mlAngles.Any(a => a.IsEqualTo(bestRotation)))
                            mlAngles.Add(bestRotation);
                        if (!mlAngles.Any(a => a.IsEqualTo(bestRotation + Angle.HalfPI)))
                            mlAngles.Add(bestRotation + Angle.HalfPI);

                        Debug.WriteLine($"[AngleCandidateBuilder] ML: {angles.Count} angles -> {mlAngles.Count} predicted");
                        angles = mlAngles;
                    }
                }
            }

            if (knownGoodAngles.Count > 0 && !ForceFullSweep)
            {
                var pruned = new List<double> { bestRotation, bestRotation + Angle.HalfPI };

                foreach (var a in knownGoodAngles)
                {
                    if (!pruned.Any(existing => existing.IsEqualTo(a)))
                        pruned.Add(a);
                }

                Debug.WriteLine($"[AngleCandidateBuilder] Pruned: {angles.Count} -> {pruned.Count} angles (known-good)");
                return pruned;
            }

            return angles;
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
