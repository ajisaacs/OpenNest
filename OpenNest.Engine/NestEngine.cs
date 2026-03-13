using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using OpenNest.Engine.BestFit;
using OpenNest.Geometry;
using OpenNest.Math;
using OpenNest.RectanglePacking;

namespace OpenNest
{
    public class NestEngine
    {
        public NestEngine(Plate plate)
        {
            Plate = plate;
        }

        public Plate Plate { get; set; }

        public NestDirection NestDirection { get; set; }

        public int PlateNumber { get; set; }

        public bool Fill(NestItem item)
        {
            return Fill(item, Plate.WorkArea());
        }

        public bool Fill(List<Part> groupParts)
        {
            return Fill(groupParts, Plate.WorkArea());
        }

        public bool Fill(NestItem item, Box workArea)
        {
            var parts = Fill(item, workArea, null, CancellationToken.None);

            if (parts == null || parts.Count == 0)
                return false;

            Plate.Parts.AddRange(parts);
            return true;
        }

        public List<Part> Fill(NestItem item, Box workArea,
            IProgress<NestProgress> progress, CancellationToken token)
        {
            var best = FindBestFill(item, workArea, progress, token);

            if (token.IsCancellationRequested)
                return best ?? new List<Part>();

            // Try improving by filling the remainder strip separately.
            var improved = TryRemainderImprovement(item, workArea, best);

            if (IsBetterFill(improved, best, workArea))
            {
                Debug.WriteLine($"[Fill] Remainder improvement: {improved.Count} parts (was {best?.Count ?? 0})");
                best = improved;
                ReportProgress(progress, NestPhase.Remainder, PlateNumber, best, workArea);
            }

            if (best == null || best.Count == 0)
                return new List<Part>();

            if (item.Quantity > 0 && best.Count > item.Quantity)
                best = best.Take(item.Quantity).ToList();

            return best;
        }

        private List<Part> FindBestFill(NestItem item, Box workArea)
        {
            var bestRotation = RotationAnalysis.FindBestRotation(item);

            var engine = new FillLinear(workArea, Plate.PartSpacing);

            // Build candidate rotation angles — always try the best rotation and +90°.
            var angles = new List<double> { bestRotation, bestRotation + Angle.HalfPI };

            // When the work area is narrow relative to the part, sweep rotation
            // angles so we can find one that fits the part into the tight strip.
            var testPart = new Part(item.Drawing);

            if (!bestRotation.IsEqualTo(0))
                testPart.Rotate(bestRotation);

            testPart.UpdateBounds();

            var partLongestSide = System.Math.Max(testPart.BoundingBox.Width, testPart.BoundingBox.Length);
            var workAreaShortSide = System.Math.Min(workArea.Width, workArea.Length);

            if (workAreaShortSide < partLongestSide)
            {
                // Try every 5° from 0 to 175° to find rotations that fit.
                var step = Angle.ToRadians(5);

                for (var a = 0.0; a < System.Math.PI; a += step)
                {
                    if (!angles.Any(existing => existing.IsEqualTo(a)))
                        angles.Add(a);
                }
            }

            var linearBag = new System.Collections.Concurrent.ConcurrentBag<(FillScore score, List<Part> parts)>();

            System.Threading.Tasks.Parallel.ForEach(angles, angle =>
            {
                var localEngine = new FillLinear(workArea, Plate.PartSpacing);
                var h = localEngine.Fill(item.Drawing, angle, NestDirection.Horizontal);
                var v = localEngine.Fill(item.Drawing, angle, NestDirection.Vertical);

                if (h != null && h.Count > 0)
                    linearBag.Add((FillScore.Compute(h, workArea), h));

                if (v != null && v.Count > 0)
                    linearBag.Add((FillScore.Compute(v, workArea), v));
            });

            List<Part> best = null;
            var bestScore = default(FillScore);

            foreach (var (score, parts) in linearBag)
            {
                if (best == null || score > bestScore)
                {
                    best = parts;
                    bestScore = score;
                }
            }

            var bestLinearScore = best != null ? FillScore.Compute(best, workArea) : default;
            Debug.WriteLine($"[FindBestFill] Linear: {bestLinearScore.Count} parts, density={bestLinearScore.Density:P1} | WorkArea: {workArea.Width:F1}x{workArea.Length:F1} | Angles: {angles.Count}");

            // Try rectangle best-fit (mixes orientations to fill remnant strips).
            var rectResult = FillRectangleBestFit(item, workArea);

            Debug.WriteLine($"[FindBestFill] RectBestFit: {rectResult?.Count ?? 0} parts");

            if (IsBetterFill(rectResult, best, workArea))
                best = rectResult;

            // Try pair-based approach.
            var pairResult = FillWithPairs(item, workArea);

            Debug.WriteLine($"[FindBestFill] Pair: {pairResult.Count} parts | Winner: {(IsBetterFill(pairResult, best, workArea) ? "Pair" : "Linear")}");

            if (IsBetterFill(pairResult, best, workArea))
                best = pairResult;

            return best;
        }

        private List<Part> FindBestFill(NestItem item, Box workArea,
            IProgress<NestProgress> progress, CancellationToken token)
        {
            List<Part> best = null;

            try
            {
                var bestRotation = RotationAnalysis.FindBestRotation(item);
                var engine = new FillLinear(workArea, Plate.PartSpacing);
                var angles = new List<double> { bestRotation, bestRotation + Angle.HalfPI };

                var testPart = new Part(item.Drawing);
                if (!bestRotation.IsEqualTo(0))
                    testPart.Rotate(bestRotation);
                testPart.UpdateBounds();

                var partLongestSide = System.Math.Max(testPart.BoundingBox.Width, testPart.BoundingBox.Length);
                var workAreaShortSide = System.Math.Min(workArea.Width, workArea.Length);

                if (workAreaShortSide < partLongestSide)
                {
                    var step = Angle.ToRadians(5);
                    for (var a = 0.0; a < System.Math.PI; a += step)
                    {
                        if (!angles.Any(existing => existing.IsEqualTo(a)))
                            angles.Add(a);
                    }
                }

                // Linear phase
                var linearBag = new System.Collections.Concurrent.ConcurrentBag<(FillScore score, List<Part> parts)>();

                System.Threading.Tasks.Parallel.ForEach(angles,
                    new System.Threading.Tasks.ParallelOptions { CancellationToken = token },
                    angle =>
                    {
                        var localEngine = new FillLinear(workArea, Plate.PartSpacing);
                        var h = localEngine.Fill(item.Drawing, angle, NestDirection.Horizontal);
                        var v = localEngine.Fill(item.Drawing, angle, NestDirection.Vertical);

                        if (h != null && h.Count > 0)
                            linearBag.Add((FillScore.Compute(h, workArea), h));
                        if (v != null && v.Count > 0)
                            linearBag.Add((FillScore.Compute(v, workArea), v));
                    });

                var bestScore = default(FillScore);

                foreach (var (score, parts) in linearBag)
                {
                    if (best == null || score > bestScore)
                    {
                        best = parts;
                        bestScore = score;
                    }
                }

                var bestLinearScore = best != null ? FillScore.Compute(best, workArea) : default;
                Debug.WriteLine($"[FindBestFill] Linear: {bestLinearScore.Count} parts, density={bestLinearScore.Density:P1} | WorkArea: {workArea.Width:F1}x{workArea.Length:F1} | Angles: {angles.Count}");

                ReportProgress(progress, NestPhase.Linear, PlateNumber, best, workArea);
                token.ThrowIfCancellationRequested();

                // RectBestFit phase
                var rectResult = FillRectangleBestFit(item, workArea);
                Debug.WriteLine($"[FindBestFill] RectBestFit: {rectResult?.Count ?? 0} parts");

                if (IsBetterFill(rectResult, best, workArea))
                {
                    best = rectResult;
                    ReportProgress(progress, NestPhase.RectBestFit, PlateNumber, best, workArea);
                }

                token.ThrowIfCancellationRequested();

                // Pairs phase
                var pairResult = FillWithPairs(item, workArea, token);
                Debug.WriteLine($"[FindBestFill] Pair: {pairResult.Count} parts | Winner: {(IsBetterFill(pairResult, best, workArea) ? "Pair" : "Linear")}");

                if (IsBetterFill(pairResult, best, workArea))
                {
                    best = pairResult;
                    ReportProgress(progress, NestPhase.Pairs, PlateNumber, best, workArea);
                }
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine("[FindBestFill] Cancelled, returning current best");
            }

            return best ?? new List<Part>();
        }

        public bool Fill(List<Part> groupParts, Box workArea)
        {
            var parts = Fill(groupParts, workArea, null, CancellationToken.None);

            if (parts == null || parts.Count == 0)
                return false;

            Plate.Parts.AddRange(parts);
            return true;
        }

        public List<Part> Fill(List<Part> groupParts, Box workArea,
            IProgress<NestProgress> progress, CancellationToken token)
        {
            if (groupParts == null || groupParts.Count == 0)
                return new List<Part>();

            var engine = new FillLinear(workArea, Plate.PartSpacing);
            var angles = RotationAnalysis.FindHullEdgeAngles(groupParts);
            var best = FillPattern(engine, groupParts, angles, workArea);

            Debug.WriteLine($"[Fill(groupParts,Box)] Linear: {best?.Count ?? 0} parts | WorkArea: {workArea.Width:F1}x{workArea.Length:F1}");

            ReportProgress(progress, NestPhase.Linear, PlateNumber, best, workArea);

            if (groupParts.Count == 1)
            {
                try
                {
                    token.ThrowIfCancellationRequested();

                    var nestItem = new NestItem { Drawing = groupParts[0].BaseDrawing };
                    var rectResult = FillRectangleBestFit(nestItem, workArea);

                    Debug.WriteLine($"[Fill(groupParts,Box)] RectBestFit: {rectResult?.Count ?? 0} parts");

                    if (IsBetterFill(rectResult, best, workArea))
                    {
                        best = rectResult;
                        ReportProgress(progress, NestPhase.RectBestFit, PlateNumber, best, workArea);
                    }

                    token.ThrowIfCancellationRequested();

                    var pairResult = FillWithPairs(nestItem, workArea, token);

                    Debug.WriteLine($"[Fill(groupParts,Box)] Pair: {pairResult.Count} parts | Winner: {(IsBetterFill(pairResult, best, workArea) ? "Pair" : "Linear")}");

                    if (IsBetterFill(pairResult, best, workArea))
                    {
                        best = pairResult;
                        ReportProgress(progress, NestPhase.Pairs, PlateNumber, best, workArea);
                    }

                    // Try improving by filling the remainder strip separately.
                    var improved = TryRemainderImprovement(nestItem, workArea, best);

                    if (IsBetterFill(improved, best, workArea))
                    {
                        Debug.WriteLine($"[Fill(groupParts,Box)] Remainder improvement: {improved.Count} parts (was {best?.Count ?? 0})");
                        best = improved;
                        ReportProgress(progress, NestPhase.Remainder, PlateNumber, best, workArea);
                    }
                }
                catch (OperationCanceledException)
                {
                    Debug.WriteLine("[Fill(groupParts,Box)] Cancelled, returning current best");
                }
            }

            return best ?? new List<Part>();
        }

        public bool Pack(List<NestItem> items)
        {
            var workArea = Plate.WorkArea();
            return PackArea(workArea, items);
        }

        public bool PackArea(Box box, List<NestItem> items)
        {
            var binItems = BinConverter.ToItems(items, Plate.PartSpacing, Plate.Area());
            var bin = BinConverter.CreateBin(box, Plate.PartSpacing);

            var engine = new PackBottomLeft(bin);
            engine.Pack(binItems);

            var parts = BinConverter.ToParts(bin, items);
            Plate.Parts.AddRange(parts);

            return parts.Count > 0;
        }

        private List<Part> FillRectangleBestFit(NestItem item, Box workArea)
        {
            var binItem = BinConverter.ToItem(item, Plate.PartSpacing);
            var bin = BinConverter.CreateBin(workArea, Plate.PartSpacing);

            var engine = new FillBestFit(bin);
            engine.Fill(binItem);

            return BinConverter.ToParts(bin, new List<NestItem> { item });
        }

        private List<Part> FillWithPairs(NestItem item, Box workArea)
        {
            var bestFits = BestFitCache.GetOrCompute(
                item.Drawing, Plate.Size.Width, Plate.Size.Length,
                Plate.PartSpacing);

            var candidates = SelectPairCandidates(bestFits, workArea);
            Debug.WriteLine($"[FillWithPairs] Total: {bestFits.Count}, Kept: {bestFits.Count(r => r.Keep)}, Trying: {candidates.Count}");

            var resultBag = new System.Collections.Concurrent.ConcurrentBag<(FillScore score, List<Part> parts)>();

            System.Threading.Tasks.Parallel.For(0, candidates.Count, i =>
            {
                var result = candidates[i];
                var pairParts = result.BuildParts(item.Drawing);
                var angles = RotationAnalysis.FindHullEdgeAngles(pairParts);
                var engine = new FillLinear(workArea, Plate.PartSpacing);
                var filled = FillPattern(engine, pairParts, angles, workArea);

                if (filled != null && filled.Count > 0)
                    resultBag.Add((FillScore.Compute(filled, workArea), filled));
            });

            List<Part> best = null;
            var bestScore = default(FillScore);

            foreach (var (score, parts) in resultBag)
            {
                if (best == null || score > bestScore)
                {
                    best = parts;
                    bestScore = score;
                }
            }

            Debug.WriteLine($"[FillWithPairs] Best pair result: {bestScore.Count} parts, remnant={bestScore.UsableRemnantArea:F1}, density={bestScore.Density:P1}");
            return best ?? new List<Part>();
        }

        private List<Part> FillWithPairs(NestItem item, Box workArea, CancellationToken token)
        {
            var bestFits = BestFitCache.GetOrCompute(
                item.Drawing, Plate.Size.Width, Plate.Size.Length,
                Plate.PartSpacing);

            var candidates = SelectPairCandidates(bestFits, workArea);
            Debug.WriteLine($"[FillWithPairs] Total: {bestFits.Count}, Kept: {bestFits.Count(r => r.Keep)}, Trying: {candidates.Count}");

            var resultBag = new System.Collections.Concurrent.ConcurrentBag<(FillScore score, List<Part> parts)>();

            try
            {
                System.Threading.Tasks.Parallel.For(0, candidates.Count,
                    new System.Threading.Tasks.ParallelOptions { CancellationToken = token },
                    i =>
                    {
                        var result = candidates[i];
                        var pairParts = result.BuildParts(item.Drawing);
                        var angles = RotationAnalysis.FindHullEdgeAngles(pairParts);
                        var engine = new FillLinear(workArea, Plate.PartSpacing);
                        var filled = FillPattern(engine, pairParts, angles, workArea);

                        if (filled != null && filled.Count > 0)
                            resultBag.Add((FillScore.Compute(filled, workArea), filled));
                    });
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine("[FillWithPairs] Cancelled mid-phase, using results so far");
            }

            List<Part> best = null;
            var bestScore = default(FillScore);

            foreach (var (score, parts) in resultBag)
            {
                if (best == null || score > bestScore)
                {
                    best = parts;
                    bestScore = score;
                }
            }

            Debug.WriteLine($"[FillWithPairs] Best pair result: {bestScore.Count} parts, remnant={bestScore.UsableRemnantArea:F1}, density={bestScore.Density:P1}");
            return best ?? new List<Part>();
        }

        /// <summary>
        /// Selects pair candidates to try for the given work area. Always includes
        /// the top 50 by area. For narrow work areas, also includes all pairs whose
        /// shortest side fits the strip width — these are candidates that can only
        /// be evaluated by actually tiling them into the narrow space.
        /// </summary>
        private List<BestFitResult> SelectPairCandidates(List<BestFitResult> bestFits, Box workArea)
        {
            var kept = bestFits.Where(r => r.Keep).ToList();
            var top = kept.Take(50).ToList();

            var workShortSide = System.Math.Min(workArea.Width, workArea.Length);
            var plateShortSide = System.Math.Min(Plate.Size.Width, Plate.Size.Length);

            // When the work area is significantly narrower than the plate,
            // include all pairs that fit the narrow dimension.
            if (workShortSide < plateShortSide * 0.5)
            {
                var stripCandidates = kept
                    .Where(r => r.ShortestSide <= workShortSide + Tolerance.Epsilon);

                var existing = new HashSet<BestFitResult>(top);

                foreach (var r in stripCandidates)
                {
                    if (existing.Add(r))
                        top.Add(r);
                }

                Debug.WriteLine($"[SelectPairCandidates] Strip mode: {top.Count} candidates (shortSide <= {workShortSide:F1})");
            }

            return top;
        }

        private bool HasOverlaps(List<Part> parts, double spacing)
        {
            if (parts == null || parts.Count <= 1)
                return false;

            for (var i = 0; i < parts.Count; i++)
            {
                var box1 = parts[i].BoundingBox;

                for (var j = i + 1; j < parts.Count; j++)
                {
                    var box2 = parts[j].BoundingBox;

                    // Fast bounding box rejection — if boxes don't overlap,
                    // the parts can't intersect. Eliminates nearly all pairs
                    // in grid layouts.
                    if (box1.Right < box2.Left || box2.Right < box1.Left ||
                        box1.Top < box2.Bottom || box2.Top < box1.Bottom)
                        continue;

                    List<Vector> pts;

                    if (parts[i].Intersects(parts[j], out pts))
                    {
                        var b1 = parts[i].BoundingBox;
                        var b2 = parts[j].BoundingBox;
                        Debug.WriteLine($"[HasOverlaps] Overlap: part[{i}] ({parts[i].BaseDrawing?.Name}) @ ({b1.Left:F2},{b1.Bottom:F2})-({b1.Right:F2},{b1.Top:F2}) rot={parts[i].Rotation:F2}" +
                            $" vs part[{j}] ({parts[j].BaseDrawing?.Name}) @ ({b2.Left:F2},{b2.Bottom:F2})-({b2.Right:F2},{b2.Top:F2}) rot={parts[j].Rotation:F2}" +
                            $" intersections={pts?.Count ?? 0}");
                        return true;
                    }
                }
            }

            return false;
        }

        private bool IsBetterFill(List<Part> candidate, List<Part> current, Box workArea)
        {
            if (candidate == null || candidate.Count == 0)
                return false;

            if (current == null || current.Count == 0)
                return true;

            return FillScore.Compute(candidate, workArea) > FillScore.Compute(current, workArea);
        }

        private bool IsBetterValidFill(List<Part> candidate, List<Part> current, Box workArea)
        {
            if (candidate != null && candidate.Count > 0 && HasOverlaps(candidate, Plate.PartSpacing))
            {
                Debug.WriteLine($"[IsBetterValidFill] REJECTED {candidate.Count} parts due to overlaps (current best: {current?.Count ?? 0})");
                return false;
            }

            return IsBetterFill(candidate, current, workArea);
        }

        /// <summary>
        /// Groups parts into positional clusters along the given axis.
        /// Parts whose center positions are separated by more than half
        /// the part dimension start a new cluster.
        /// </summary>
        private static List<List<Part>> ClusterParts(List<Part> parts, bool horizontal)
        {
            var sorted = horizontal
                ? parts.OrderBy(p => p.BoundingBox.Center.X).ToList()
                : parts.OrderBy(p => p.BoundingBox.Center.Y).ToList();

            var refDim = horizontal
                ? sorted.Max(p => p.BoundingBox.Width)
                : sorted.Max(p => p.BoundingBox.Length);
            var gapThreshold = refDim * 0.5;

            var clusters = new List<List<Part>>();
            var current = new List<Part> { sorted[0] };

            for (var i = 1; i < sorted.Count; i++)
            {
                var prevCenter = horizontal
                    ? sorted[i - 1].BoundingBox.Center.X
                    : sorted[i - 1].BoundingBox.Center.Y;
                var currCenter = horizontal
                    ? sorted[i].BoundingBox.Center.X
                    : sorted[i].BoundingBox.Center.Y;

                if (currCenter - prevCenter > gapThreshold)
                {
                    clusters.Add(current);
                    current = new List<Part>();
                }

                current.Add(sorted[i]);
            }

            clusters.Add(current);
            return clusters;
        }

        private List<Part> TryStripRefill(NestItem item, Box workArea, List<Part> parts, bool horizontal)
        {
            if (parts == null || parts.Count < 3)
                return null;

            var clusters = ClusterParts(parts, horizontal);

            if (clusters.Count < 2)
                return null;

            // Determine the mode (most common) cluster count, excluding the last cluster.
            var mainClusters = clusters.Take(clusters.Count - 1).ToList();
            var modeCount = mainClusters
                .GroupBy(c => c.Count)
                .OrderByDescending(g => g.Count())
                .First()
                .Key;

            var lastCluster = clusters[clusters.Count - 1];

            // Only attempt refill if the last cluster is smaller than the mode.
            if (lastCluster.Count >= modeCount)
                return null;

            Debug.WriteLine($"[TryStripRefill] {(horizontal ? "H" : "V")} clusters: {clusters.Count}, mode: {modeCount}, last: {lastCluster.Count}");

            // Build the main parts list (everything except the last cluster).
            var mainParts = clusters.Take(clusters.Count - 1).SelectMany(c => c).ToList();
            var mainBox = ((IEnumerable<IBoundable>)mainParts).GetBoundingBox();

            // Compute the strip box from the main grid edge to the work area edge.
            Box stripBox;

            if (horizontal)
            {
                var stripLeft = mainBox.Right + Plate.PartSpacing;
                var stripWidth = workArea.Right - stripLeft;

                if (stripWidth <= 0)
                    return null;

                stripBox = new Box(stripLeft, workArea.Y, stripWidth, workArea.Length);
            }
            else
            {
                var stripBottom = mainBox.Top + Plate.PartSpacing;
                var stripHeight = workArea.Top - stripBottom;

                if (stripHeight <= 0)
                    return null;

                stripBox = new Box(workArea.X, stripBottom, workArea.Width, stripHeight);
            }

            Debug.WriteLine($"[TryStripRefill] Strip: {stripBox.Width:F1}x{stripBox.Length:F1} at ({stripBox.X:F1},{stripBox.Y:F1})");

            var stripParts = FindBestFill(item, stripBox);

            if (stripParts == null || stripParts.Count <= lastCluster.Count)
            {
                Debug.WriteLine($"[TryStripRefill] No improvement: strip={stripParts?.Count ?? 0} vs oddball={lastCluster.Count}");
                return null;
            }

            Debug.WriteLine($"[TryStripRefill] Improvement: strip={stripParts.Count} vs oddball={lastCluster.Count}");

            var combined = new List<Part>(mainParts.Count + stripParts.Count);
            combined.AddRange(mainParts);
            combined.AddRange(stripParts);
            return combined;
        }

        private List<Part> TryRemainderImprovement(NestItem item, Box workArea, List<Part> currentBest)
        {
            if (currentBest == null || currentBest.Count < 3)
                return null;

            List<Part> best = null;

            var hResult = TryStripRefill(item, workArea, currentBest, horizontal: true);

            if (IsBetterFill(hResult, best, workArea))
                best = hResult;

            var vResult = TryStripRefill(item, workArea, currentBest, horizontal: false);

            if (IsBetterFill(vResult, best, workArea))
                best = vResult;

            return best;
        }

        private Pattern BuildRotatedPattern(List<Part> groupParts, double angle)
        {
            var pattern = new Pattern();
            var center = ((IEnumerable<IBoundable>)groupParts).GetBoundingBox().Center;

            foreach (var part in groupParts)
            {
                var clone = (Part)part.Clone();
                clone.UpdateBounds();

                if (!angle.IsEqualTo(0))
                    clone.Rotate(angle, center);

                pattern.Parts.Add(clone);
            }

            pattern.UpdateBounds();
            return pattern;
        }

        private List<Part> FillPattern(FillLinear engine, List<Part> groupParts, List<double> angles, Box workArea)
        {
            var bag = new System.Collections.Concurrent.ConcurrentBag<(FillScore score, List<Part> parts)>();

            System.Threading.Tasks.Parallel.ForEach(angles, angle =>
            {
                var pattern = BuildRotatedPattern(groupParts, angle);

                if (pattern.Parts.Count == 0)
                    return;

                var localEngine = new FillLinear(workArea, engine.PartSpacing);
                var h = localEngine.Fill(pattern, NestDirection.Horizontal);
                var v = localEngine.Fill(pattern, NestDirection.Vertical);

                if (h != null && h.Count > 0 && !HasOverlaps(h, engine.PartSpacing))
                    bag.Add((FillScore.Compute(h, workArea), h));

                if (v != null && v.Count > 0 && !HasOverlaps(v, engine.PartSpacing))
                    bag.Add((FillScore.Compute(v, workArea), v));
            });

            List<Part> best = null;
            var bestScore = default(FillScore);

            foreach (var (score, parts) in bag)
            {
                if (best == null || score > bestScore)
                {
                    best = parts;
                    bestScore = score;
                }
            }

            return best;
        }

        private static void ReportProgress(
            IProgress<NestProgress> progress,
            NestPhase phase,
            int plateNumber,
            List<Part> best,
            Box workArea)
        {
            if (progress == null || best == null || best.Count == 0)
                return;

            var score = FillScore.Compute(best, workArea);
            var clonedParts = new List<Part>(best.Count);

            foreach (var part in best)
                clonedParts.Add((Part)part.Clone());

            progress.Report(new NestProgress
            {
                Phase = phase,
                PlateNumber = plateNumber,
                BestPartCount = score.Count,
                BestDensity = score.Density,
                UsableRemnantArea = score.UsableRemnantArea,
                BestParts = clonedParts
            });
        }
    }
}
