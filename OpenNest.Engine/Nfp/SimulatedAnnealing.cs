using OpenNest.Engine.Fill;
using OpenNest.Geometry;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;

namespace OpenNest.Engine.Nfp
{
    /// <summary>
    /// Simulated annealing optimizer for NFP-based nesting.
    /// Searches for the best part ordering and rotation to maximize plate utilization.
    /// </summary>
    public class SimulatedAnnealing : INestOptimizer
    {
        private const double DefaultCoolingRate = 0.995;
        private const double DefaultMinTemperature = 0.1;
        private const int DefaultMaxNoImprovement = 500;

        public OptimizationResult Optimize(List<NestItem> items, Box workArea, NfpCache cache,
            Dictionary<int, List<double>> candidateRotations,
            IProgress<NestProgress> progress = null,
            CancellationToken cancellation = default)
        {
            var random = new Random();

            // Build initial sequence: expand NestItems into individual entries,
            // sorted by area descending.
            var sequence = BuildInitialSequence(items, candidateRotations);

            if (sequence.Count == 0)
                return new OptimizationResult { Sequence = sequence, Score = default, Iterations = 0 };

            // Evaluate initial solution.
            var blf = new BottomLeftFill(workArea, cache);
            var bestPlaced = blf.Fill(sequence);
            var bestScore = FillScore.Compute(BottomLeftFill.ToNestParts(bestPlaced), workArea);
            var bestSequence = new List<SequenceEntry>(sequence);

            var currentSequence = new List<SequenceEntry>(sequence);
            var currentScore = bestScore;

            // Calibrate initial temperature so ~80% of worse moves are accepted.
            var initialTemp = CalibrateTemperature(currentSequence, workArea, cache,
                candidateRotations, random);
            var temperature = initialTemp;
            var noImprovement = 0;
            var iteration = 0;

            Debug.WriteLine($"[SA] Initial: {bestScore.Count} parts, density={bestScore.Density:P1}, temp={initialTemp:F2}");

            ReportBest(progress, BottomLeftFill.ToNestParts(bestPlaced), workArea,
                $"NFP: initial {bestScore.Count} parts, density={bestScore.Density:P1}");

            while (temperature > DefaultMinTemperature
                   && noImprovement < DefaultMaxNoImprovement
                   && !cancellation.IsCancellationRequested)
            {
                iteration++;

                var candidate = new List<SequenceEntry>(currentSequence);
                Mutate(candidate, candidateRotations, random);

                var candidatePlaced = blf.Fill(candidate);
                var candidateScore = FillScore.Compute(BottomLeftFill.ToNestParts(candidatePlaced), workArea);

                var delta = candidateScore.CompareTo(currentScore);

                if (delta > 0)
                {
                    // Better solution — always accept.
                    currentSequence = candidate;
                    currentScore = candidateScore;

                    if (currentScore > bestScore)
                    {
                        bestScore = currentScore;
                        bestSequence = new List<SequenceEntry>(currentSequence);
                        noImprovement = 0;

                        Debug.WriteLine($"[SA] New best at iter {iteration}: {bestScore.Count} parts, density={bestScore.Density:P1}");

                        ReportBest(progress, BottomLeftFill.ToNestParts(candidatePlaced), workArea,
                            $"NFP: iter {iteration}, {bestScore.Count} parts, density={bestScore.Density:P1}");
                    }
                    else
                    {
                        noImprovement++;
                    }
                }
                else if (delta < 0)
                {
                    // Worse solution — accept with probability based on temperature.
                    var scoreDiff = ScoreDifference(currentScore, candidateScore);
                    var acceptProb = System.Math.Exp(-scoreDiff / temperature);

                    if (random.NextDouble() < acceptProb)
                    {
                        currentSequence = candidate;
                        currentScore = candidateScore;
                    }

                    noImprovement++;
                }
                else
                {
                    noImprovement++;
                }

                temperature *= DefaultCoolingRate;
            }

            Debug.WriteLine($"[SA] Done: {iteration} iters, best={bestScore.Count} parts, density={bestScore.Density:P1}");

            return new OptimizationResult
            {
                Sequence = bestSequence,
                Score = bestScore,
                Iterations = iteration
            };
        }

        /// <summary>
        /// Builds the initial placement sequence sorted by drawing area descending.
        /// Each NestItem is expanded by its quantity.
        /// </summary>
        private static List<SequenceEntry> BuildInitialSequence(
            List<NestItem> items, Dictionary<int, List<double>> candidateRotations)
        {
            var sequence = new List<SequenceEntry>();

            // Sort items by area descending.
            var sorted = items.OrderByDescending(i => i.Drawing.Area).ToList();

            foreach (var item in sorted)
            {
                var qty = item.Quantity > 0 ? item.Quantity : 1;
                var rotation = 0.0;

                if (candidateRotations.TryGetValue(item.Drawing.Id, out var rotations) && rotations.Count > 0)
                    rotation = rotations[0];

                for (var i = 0; i < qty; i++)
                    sequence.Add(new SequenceEntry(item.Drawing.Id, rotation, item.Drawing));
            }

            return sequence;
        }

        /// <summary>
        /// Applies a random mutation to the sequence.
        /// </summary>
        private static void Mutate(List<SequenceEntry> sequence,
            Dictionary<int, List<double>> candidateRotations, Random random)
        {
            if (sequence.Count < 2)
                return;

            var op = random.Next(3);

            switch (op)
            {
                case 0: // Swap
                    MutateSwap(sequence, random);
                    break;
                case 1: // Rotate
                    MutateRotate(sequence, candidateRotations, random);
                    break;
                case 2: // Segment reverse
                    MutateReverse(sequence, random);
                    break;
            }
        }

        /// <summary>
        /// Swaps two random parts in the sequence.
        /// </summary>
        private static void MutateSwap(List<SequenceEntry> sequence, Random random)
        {
            var i = random.Next(sequence.Count);
            var j = random.Next(sequence.Count);

            while (j == i && sequence.Count > 1)
                j = random.Next(sequence.Count);

            (sequence[i], sequence[j]) = (sequence[j], sequence[i]);
        }

        /// <summary>
        /// Changes a random part's rotation to another candidate angle.
        /// </summary>
        private static void MutateRotate(List<SequenceEntry> sequence,
            Dictionary<int, List<double>> candidateRotations, Random random)
        {
            var idx = random.Next(sequence.Count);
            var entry = sequence[idx];

            if (!candidateRotations.TryGetValue(entry.DrawingId, out var rotations) || rotations.Count <= 1)
                return;

            var newRotation = rotations[random.Next(rotations.Count)];
            sequence[idx] = entry.WithRotation(newRotation);
        }

        /// <summary>
        /// Reverses a random contiguous subsequence.
        /// </summary>
        private static void MutateReverse(List<SequenceEntry> sequence, Random random)
        {
            var i = random.Next(sequence.Count);
            var j = random.Next(sequence.Count);

            if (i > j)
                (i, j) = (j, i);

            while (i < j)
            {
                (sequence[i], sequence[j]) = (sequence[j], sequence[i]);
                i++;
                j--;
            }
        }

        /// <summary>
        /// Calibrates the initial temperature by sampling random mutations and
        /// measuring score differences. Sets temperature so ~80% of worse moves
        /// are accepted initially.
        /// </summary>
        private static double CalibrateTemperature(
            List<SequenceEntry> sequence,
            Box workArea, NfpCache cache,
            Dictionary<int, List<double>> candidateRotations, Random random)
        {
            const int samples = 20;
            var deltas = new List<double>();
            var blf = new BottomLeftFill(workArea, cache);

            var basePlaced = blf.Fill(sequence);
            var baseScore = FillScore.Compute(BottomLeftFill.ToNestParts(basePlaced), workArea);

            for (var i = 0; i < samples; i++)
            {
                var candidate = new List<SequenceEntry>(sequence);
                Mutate(candidate, candidateRotations, random);

                var placed = blf.Fill(candidate);
                var score = FillScore.Compute(BottomLeftFill.ToNestParts(placed), workArea);

                var diff = ScoreDifference(baseScore, score);

                if (diff > 0)
                    deltas.Add(diff);
            }

            if (deltas.Count == 0)
                return 1.0;

            // T = -avgDelta / ln(0.8) ≈ avgDelta * 4.48
            var avgDelta = deltas.Average();
            return -avgDelta / System.Math.Log(0.8);
        }

        /// <summary>
        /// Computes a numeric difference between two scores for SA acceptance probability.
        /// Uses a weighted combination of count and density.
        /// </summary>
        private static double ScoreDifference(FillScore better, FillScore worse)
        {
            // Weight count heavily (each part is worth 10 density points).
            var countDiff = better.Count - worse.Count;
            var densityDiff = better.Density - worse.Density;

            return countDiff * 10.0 + densityDiff;
        }

        private static void ReportBest(IProgress<NestProgress> progress, List<Part> parts,
            Box workArea, string description)
        {
            NestEngineBase.ReportProgress(progress, NestPhase.Nfp, 0, parts, workArea,
                description, isOverallBest: true);
        }
    }
}
