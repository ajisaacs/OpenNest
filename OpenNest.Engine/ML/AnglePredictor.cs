using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using OpenNest.Math;

namespace OpenNest.Engine.ML
{
    public static class AnglePredictor
    {
        private static readonly SingleAttemptLoader<InferenceSession> SessionLoader = new(LoadSession);

        internal static bool IsAvailable => GetSession() != null;

        public static List<double> PredictAngles(
            PartFeatures features,
            double sheetWidth,
            double sheetHeight,
            double threshold = 0.3
        )
        {
            var session = GetSession();
            if (session == null)
                return null;

            try
            {
                var input = new float[11];
                input[0] = (float)features.Area;
                input[1] = (float)features.Convexity;
                input[2] = (float)features.AspectRatio;
                input[3] = (float)features.BoundingBoxFill;
                input[4] = (float)features.Circularity;
                input[5] = (float)features.PerimeterToAreaRatio;
                input[6] = features.VertexCount;
                input[7] = (float)sheetWidth;
                input[8] = (float)sheetHeight;
                input[9] = (float)(sheetWidth / (sheetHeight > 0 ? sheetHeight : 1.0));
                input[10] = (float)(features.Area / (sheetWidth * sheetHeight));

                var tensor = new DenseTensor<float>(input, new[] { 1, 11 });
                var inputs = new List<NamedOnnxValue>
                {
                    NamedOnnxValue.CreateFromTensor("features", tensor),
                };

                using var results = session.Run(inputs);
                var probabilities = results.First().AsEnumerable<float>().ToArray();

                var angles = new List<(double angleDeg, float prob)>();
                for (var i = 0; i < 36 && i < probabilities.Length; i++)
                {
                    if (probabilities[i] >= threshold)
                        angles.Add((i * 5.0, probabilities[i]));
                }

                // Minimum 3 angles — take top by probability if fewer pass threshold.
                if (angles.Count < 3)
                {
                    angles = probabilities
                        .Select((p, i) => (angleDeg: i * 5.0, prob: p))
                        .OrderByDescending(x => x.prob)
                        .Take(3)
                        .ToList();
                }

                // Always include 0 and 90 as safety fallback.
                var result = angles.Select(a => Angle.ToRadians(a.angleDeg)).ToList();

                if (!result.Any(a => a.IsEqualTo(0)))
                    result.Add(0);
                if (!result.Any(a => a.IsEqualTo(Angle.HalfPI)))
                    result.Add(Angle.HalfPI);

                return result;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AnglePredictor] Inference failed: {ex.Message}");
                return null;
            }
        }

        private static InferenceSession GetSession() => SessionLoader.GetValue();

        private static InferenceSession LoadSession()
        {
            var dir = Path.GetDirectoryName(typeof(AnglePredictor).Assembly.Location);
            var modelPath = Path.Combine(dir, "Models", "angle_predictor.onnx");

            if (!File.Exists(modelPath))
            {
                Debug.WriteLine($"[AnglePredictor] Model not found: {modelPath}");
                return null;
            }

            var session = new InferenceSession(modelPath);
            Debug.WriteLine("[AnglePredictor] Model loaded successfully");
            return session;
        }
    }

    // One process-lifetime attempt, including missing/failed loads. Generic only so the
    // publication contract can be tested with reference objects instead of real ONNX files.
    internal sealed class SingleAttemptLoader<T> where T : class
    {
        private readonly Func<T> _load;
        private readonly object _lock = new();
        private T _value;
        private volatile bool _loadAttempted;

        internal SingleAttemptLoader(Func<T> load) => _load = load;

        internal T GetValue()
        {
            if (_loadAttempted)
                return _value;

            lock (_lock)
            {
                if (_loadAttempted)
                    return _value;

                try
                {
                    _value = _load();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[AnglePredictor] Failed to load model: {ex.Message}");
                }
                finally
                {
                    // Publish only after assignment or definitive failure. A concurrent
                    // caller must wait on the lock, not observe a transient null session.
                    _loadAttempted = true;
                }

                return _value;
            }
        }
    }
}
