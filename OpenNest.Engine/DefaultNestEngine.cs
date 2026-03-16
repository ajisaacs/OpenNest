using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OpenNest.Engine.BestFit;
using OpenNest.Engine.ML;
using OpenNest.Geometry;
using OpenNest.Math;
using OpenNest.RectanglePacking;

namespace OpenNest
{
    public class DefaultNestEngine : NestEngineBase
    {
        public DefaultNestEngine(Plate plate) : base(plate) { }

        public override string Name => "Default";

        public override string Description => "Multi-phase nesting (Linear, Pairs, RectBestFit)";

        public bool ForceFullAngleSweep { get; set; }

        // Angles that have produced results across multiple Fill calls.
        // Populated after each Fill; used to prune subsequent fills.
        private readonly HashSet<double> knownGoodAngles = new();

        // --- Public Fill API ---

        public override List<Part> Fill(NestItem item, Box workArea,
            IProgress<NestProgress> progress, CancellationToken token)
        {
            PhaseResults.Clear();
            AngleResults.Clear();
            var best = FindBestFill(item, workArea, progress, token);

            if (best == null || best.Count == 0)
                return new List<Part>();

            if (item.Quantity > 0 && best.Count > item.Quantity)
                best = best.Take(item.Quantity).ToList();

            return best;
        }

        /// <summary>
        /// Fast fill count using linear fill with two angles plus the top cached
        /// pair candidates. Used by binary search to estimate capacity at a given
        /// box size without running the full Fill pipeline.
        /// </summary>
        private int QuickFillCount(Drawing drawing, Box testBox, double bestRotation)
        {
            var engine = new FillLinear(testBox, Plate.PartSpacing);
            var bestCount = 0;

            // Single-part linear fills.
            var angles = new[] { bestRotation, bestRotation + Angle.HalfPI };

            foreach (var angle in angles)
            {
                var h = engine.Fill(drawing, angle, NestDirection.Horizontal);
                if (h != null && h.Count > bestCount)
                    bestCount = h.Count;

                var v = engine.Fill(drawing, angle, NestDirection.Vertical);
                if (v != null && v.Count > bestCount)
                    bestCount = v.Count;
            }

            // Top pair candidates — check if pairs tile better in this box.
            var bestFits = BestFitCache.GetOrCompute(
                drawing, Plate.Size.Width, Plate.Size.Length, Plate.PartSpacing);
            var topPairs = bestFits.Where(r => r.Keep).Take(3);

            foreach (var pair in topPairs)
            {
                var pairParts = pair.BuildParts(drawing);
                var pairAngles = pair.HullAngles ?? new List<double> { 0 };
                var pairEngine = new FillLinear(testBox, Plate.PartSpacing);

                foreach (var angle in pairAngles)
                {
                    var pattern = BuildRotatedPattern(pairParts, angle);
                    if (pattern.Parts.Count == 0)
                        continue;

                    var h = pairEngine.Fill(pattern, NestDirection.Horizontal);
                    if (h != null && h.Count > bestCount)
                        bestCount = h.Count;

                    var v = pairEngine.Fill(pattern, NestDirection.Vertical);
                    if (v != null && v.Count > bestCount)
                        bestCount = v.Count;
                }
            }

            return bestCount;
        }

        public override List<Part> Fill(List<Part> groupParts, Box workArea,
            IProgress<NestProgress> progress, CancellationToken token)
        {
            if (groupParts == null || groupParts.Count == 0)
                return new List<Part>();

            PhaseResults.Clear();
            var engine = new FillLinear(workArea, Plate.PartSpacing);
            var angles = RotationAnalysis.FindHullEdgeAngles(groupParts);
            var best = FillPattern(engine, groupParts, angles, workArea);
            PhaseResults.Add(new PhaseResult(NestPhase.Linear, best?.Count ?? 0, 0));

            Debug.WriteLine($"[Fill(groupParts,Box)] Linear: {best?.Count ?? 0} parts | WorkArea: {workArea.Width:F1}x{workArea.Length:F1}");

            ReportProgress(progress, NestPhase.Linear, PlateNumber, best, workArea, BuildProgressSummary());

            if (groupParts.Count == 1)
            {
                try
                {
                    token.ThrowIfCancellationRequested();

                    var nestItem = new NestItem { Drawing = groupParts[0].BaseDrawing };
                    var rectResult = FillRectangleBestFit(nestItem, workArea);
                    PhaseResults.Add(new PhaseResult(NestPhase.RectBestFit, rectResult?.Count ?? 0, 0));

                    Debug.WriteLine($"[Fill(groupParts,Box)] RectBestFit: {rectResult?.Count ?? 0} parts");

                    if (IsBetterFill(rectResult, best, workArea))
                    {
                        best = rectResult;
                        ReportProgress(progress, NestPhase.RectBestFit, PlateNumber, best, workArea, BuildProgressSummary());
                    }

                    token.ThrowIfCancellationRequested();

                    var pairResult = FillWithPairs(nestItem, workArea, token, progress);
                    PhaseResults.Add(new PhaseResult(NestPhase.Pairs, pairResult.Count, 0));

                    Debug.WriteLine($"[Fill(groupParts,Box)] Pair: {pairResult.Count} parts | Winner: {(IsBetterFill(pairResult, best, workArea) ? "Pair" : "Linear")}");

                    if (IsBetterFill(pairResult, best, workArea))
                    {
                        best = pairResult;
                        ReportProgress(progress, NestPhase.Pairs, PlateNumber, best, workArea, BuildProgressSummary());
                    }
                }
                catch (OperationCanceledException)
                {
                    Debug.WriteLine("[Fill(groupParts,Box)] Cancelled, returning current best");
                }
            }

            return best ?? new List<Part>();
        }

        // --- Pack API ---

        public override List<Part> PackArea(Box box, List<NestItem> items,
            IProgress<NestProgress> progress, CancellationToken token)
        {
            var binItems = BinConverter.ToItems(items, Plate.PartSpacing, Plate.Area());
            var bin = BinConverter.CreateBin(box, Plate.PartSpacing);

            var engine = new PackBottomLeft(bin);
            engine.Pack(binItems);

            return BinConverter.ToParts(bin, items);
        }

        // --- FindBestFill: core orchestration ---

        private List<Part> FindBestFill(NestItem item, Box workArea,
            IProgress<NestProgress> progress = null, CancellationToken token = default)
        {
            List<Part> best = null;

            try
            {
                var bestRotation = RotationAnalysis.FindBestRotation(item);
                var angles = BuildCandidateAngles(item, bestRotation, workArea);

                // Pairs phase
                var pairSw = Stopwatch.StartNew();
                var pairResult = FillWithPairs(item, workArea, token, progress);
                pairSw.Stop();
                best = pairResult;
                var bestScore = FillScore.Compute(best, workArea);
                WinnerPhase = NestPhase.Pairs;
                PhaseResults.Add(new PhaseResult(NestPhase.Pairs, pairResult.Count, pairSw.ElapsedMilliseconds));

                Debug.WriteLine($"[FindBestFill] Pair: {bestScore.Count} parts");
                ReportProgress(progress, NestPhase.Pairs, PlateNumber, best, workArea, BuildProgressSummary());
                token.ThrowIfCancellationRequested();

                // Linear phase
                var linearSw = Stopwatch.StartNew();
                var bestLinearCount = 0;

                for (var ai = 0; ai < angles.Count; ai++)
                {
                    token.ThrowIfCancellationRequested();

                    var angle = angles[ai];
                    var localEngine = new FillLinear(workArea, Plate.PartSpacing);
                    var h = localEngine.Fill(item.Drawing, angle, NestDirection.Horizontal);
                    var v = localEngine.Fill(item.Drawing, angle, NestDirection.Vertical);

                    var angleDeg = Angle.ToDegrees(angle);
                    if (h != null && h.Count > 0)
                    {
                        var scoreH = FillScore.Compute(h, workArea);
                        AngleResults.Add(new AngleResult { AngleDeg = angleDeg, Direction = NestDirection.Horizontal, PartCount = h.Count });
                        if (h.Count > bestLinearCount) bestLinearCount = h.Count;
                        if (scoreH > bestScore)
                        {
                            best = h;
                            bestScore = scoreH;
                            WinnerPhase = NestPhase.Linear;
                        }
                    }
                    if (v != null && v.Count > 0)
                    {
                        var scoreV = FillScore.Compute(v, workArea);
                        AngleResults.Add(new AngleResult { AngleDeg = angleDeg, Direction = NestDirection.Vertical, PartCount = v.Count });
                        if (v.Count > bestLinearCount) bestLinearCount = v.Count;
                        if (scoreV > bestScore)
                        {
                            best = v;
                            bestScore = scoreV;
                            WinnerPhase = NestPhase.Linear;
                        }
                    }

                    ReportProgress(progress, NestPhase.Linear, PlateNumber, best, workArea,
                        $"Linear: {ai + 1}/{angles.Count} angles, {angleDeg:F0}° best = {bestScore.Count} parts");
                }

                linearSw.Stop();
                PhaseResults.Add(new PhaseResult(NestPhase.Linear, bestLinearCount, linearSw.ElapsedMilliseconds));

                // Record productive angles for future fills.
                foreach (var ar in AngleResults)
                {
                    if (ar.PartCount > 0)
                        knownGoodAngles.Add(Angle.ToRadians(ar.AngleDeg));
                }

                Debug.WriteLine($"[FindBestFill] Linear: {bestScore.Count} parts, density={bestScore.Density:P1} | WorkArea: {workArea.Width:F1}x{workArea.Length:F1} | Angles: {angles.Count}");

                ReportProgress(progress, NestPhase.Linear, PlateNumber, best, workArea, BuildProgressSummary());
                token.ThrowIfCancellationRequested();

                // RectBestFit phase
                var rectSw = Stopwatch.StartNew();
                var rectResult = FillRectangleBestFit(item, workArea);
                rectSw.Stop();
                var rectScore = rectResult != null ? FillScore.Compute(rectResult, workArea) : default;
                Debug.WriteLine($"[FindBestFill] RectBestFit: {rectScore.Count} parts");
                PhaseResults.Add(new PhaseResult(NestPhase.RectBestFit, rectResult?.Count ?? 0, rectSw.ElapsedMilliseconds));

                if (rectScore > bestScore)
                {
                    best = rectResult;
                    WinnerPhase = NestPhase.RectBestFit;
                    ReportProgress(progress, NestPhase.RectBestFit, PlateNumber, best, workArea, BuildProgressSummary());
                }
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine("[FindBestFill] Cancelled, returning current best");
            }

            return best ?? new List<Part>();
        }

        // --- Angle building ---

        private List<double> BuildCandidateAngles(NestItem item, double bestRotation, Box workArea)
        {
            var angles = new List<double> { bestRotation, bestRotation + Angle.HalfPI };

            // When the work area is narrow relative to the part, sweep rotation
            // angles so we can find one that fits the part into the tight strip.
            var testPart = new Part(item.Drawing);
            if (!bestRotation.IsEqualTo(0))
                testPart.Rotate(bestRotation);
            testPart.UpdateBounds();

            var partLongestSide = System.Math.Max(testPart.BoundingBox.Width, testPart.BoundingBox.Length);
            var workAreaShortSide = System.Math.Min(workArea.Width, workArea.Length);
            var needsSweep = workAreaShortSide < partLongestSide || ForceFullAngleSweep;

            if (needsSweep)
            {
                var step = Angle.ToRadians(5);
                for (var a = 0.0; a < System.Math.PI; a += step)
                {
                    if (!angles.Any(existing => existing.IsEqualTo(a)))
                        angles.Add(a);
                }
            }

            // When the work area triggers a full sweep (and we're not forcing it for training),
            // try ML angle prediction to reduce the sweep.
            if (!ForceFullAngleSweep && angles.Count > 2)
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

                        Debug.WriteLine($"[BuildCandidateAngles] ML: {angles.Count} angles -> {mlAngles.Count} predicted");
                        angles = mlAngles;
                    }
                }
            }

            // If we have known-good angles from previous fills, use only those
            // plus the defaults (bestRotation + 90°). This prunes the expensive
            // angle sweep after the first fill.
            if (knownGoodAngles.Count > 0 && !ForceFullAngleSweep)
            {
                var pruned = new List<double> { bestRotation, bestRotation + Angle.HalfPI };

                foreach (var a in knownGoodAngles)
                {
                    if (!pruned.Any(existing => existing.IsEqualTo(a)))
                        pruned.Add(a);
                }

                Debug.WriteLine($"[BuildCandidateAngles] Pruned: {angles.Count} -> {pruned.Count} angles (known-good)");
                return pruned;
            }

            return angles;
        }

        // --- Fill strategies ---

        private List<Part> FillRectangleBestFit(NestItem item, Box workArea)
        {
            var binItem = BinConverter.ToItem(item, Plate.PartSpacing);
            var bin = BinConverter.CreateBin(workArea, Plate.PartSpacing);

            var engine = new FillBestFit(bin);
            engine.Fill(binItem);

            return BinConverter.ToParts(bin, new List<NestItem> { item });
        }

        private List<Part> FillWithPairs(NestItem item, Box workArea,
            CancellationToken token = default, IProgress<NestProgress> progress = null)
        {
            var bestFits = BestFitCache.GetOrCompute(
                item.Drawing, Plate.Size.Width, Plate.Size.Length,
                Plate.PartSpacing);

            var candidates = SelectPairCandidates(bestFits, workArea);
            var diagMsg = $"[FillWithPairs] Total: {bestFits.Count}, Kept: {bestFits.Count(r => r.Keep)}, Trying: {candidates.Count}\n" +
                          $"[FillWithPairs] Plate: {Plate.Size.Width:F2}x{Plate.Size.Length:F2}, WorkArea: {workArea.Width:F2}x{workArea.Length:F2}";
            Debug.WriteLine(diagMsg);
            try { System.IO.File.AppendAllText(
                System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "nest-debug.log"),
                $"{DateTime.Now:HH:mm:ss} {diagMsg}\n"); } catch { }

            List<Part> best = null;
            var bestScore = default(FillScore);
            var sinceImproved = 0;

            try
            {
                for (var i = 0; i < candidates.Count; i++)
                {
                    token.ThrowIfCancellationRequested();

                    var result = candidates[i];
                    var pairParts = result.BuildParts(item.Drawing);
                    var angles = result.HullAngles;
                    var engine = new FillLinear(workArea, Plate.PartSpacing);
                    var filled = FillPattern(engine, pairParts, angles, workArea);

                    if (filled != null && filled.Count > 0)
                    {
                        var score = FillScore.Compute(filled, workArea);
                        if (best == null || score > bestScore)
                        {
                            best = filled;
                            bestScore = score;
                            sinceImproved = 0;
                        }
                        else
                        {
                            sinceImproved++;
                        }
                    }
                    else
                    {
                        sinceImproved++;
                    }

                    ReportProgress(progress, NestPhase.Pairs, PlateNumber, best, workArea,
                        $"Pairs: {i + 1}/{candidates.Count} candidates, best = {bestScore.Count} parts");

                    // Early exit: stop if we've tried enough candidates without improvement.
                    if (i >= 9 && sinceImproved >= 10)
                    {
                        Debug.WriteLine($"[FillWithPairs] Early exit at {i + 1}/{candidates.Count} — no improvement in last {sinceImproved} candidates");
                        break;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine("[FillWithPairs] Cancelled mid-phase, using results so far");
            }

            Debug.WriteLine($"[FillWithPairs] Best pair result: {bestScore.Count} parts, density={bestScore.Density:P1}");
            try { System.IO.File.AppendAllText(
                System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "nest-debug.log"),
                $"{DateTime.Now:HH:mm:ss} [FillWithPairs] Best: {bestScore.Count} parts, density={bestScore.Density:P1}\n"); } catch { }
            return best ?? new List<Part>();
        }

        /// <summary>
        /// Selects pair candidates to try for the given work area. Always includes
        /// the top 50 by area. For narrow work areas, also includes all pairs whose
        /// shortest side fits the strip width.
        /// </summary>
        private List<BestFitResult> SelectPairCandidates(List<BestFitResult> bestFits, Box workArea)
        {
            var kept = bestFits.Where(r => r.Keep).ToList();
            var top = kept.Take(50).ToList();

            var workShortSide = System.Math.Min(workArea.Width, workArea.Length);
            var plateShortSide = System.Math.Min(Plate.Size.Width, Plate.Size.Length);

            // When the work area is significantly narrower than the plate,
            // search ALL candidates (not just kept) for pairs that fit the
            // narrow dimension. Pairs rejected by aspect ratio for the full
            // plate may be exactly what's needed for a narrow remainder strip.
            if (workShortSide < plateShortSide * 0.5)
            {
                var stripCandidates = bestFits
                    .Where(r => r.ShortestSide <= workShortSide + Tolerance.Epsilon
                             && r.Utilization >= 0.3)
                    .OrderByDescending(r => r.Utilization);

                var existing = new HashSet<BestFitResult>(top);

                foreach (var r in stripCandidates)
                {
                    if (top.Count >= 100)
                        break;

                    if (existing.Add(r))
                        top.Add(r);
                }

                Debug.WriteLine($"[SelectPairCandidates] Strip mode: {top.Count} candidates (shortSide <= {workShortSide:F1})");
            }

            return top;
        }

        // --- Pattern helpers ---

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
            var results = new System.Collections.Concurrent.ConcurrentBag<(List<Part> Parts, FillScore Score)>();

            Parallel.ForEach(angles, angle =>
            {
                var pattern = BuildRotatedPattern(groupParts, angle);

                if (pattern.Parts.Count == 0)
                    return;

                var h = engine.Fill(pattern, NestDirection.Horizontal);
                if (h != null && h.Count > 0)
                    results.Add((h, FillScore.Compute(h, workArea)));

                var v = engine.Fill(pattern, NestDirection.Vertical);
                if (v != null && v.Count > 0)
                    results.Add((v, FillScore.Compute(v, workArea)));
            });

            List<Part> best = null;
            var bestScore = default(FillScore);

            foreach (var res in results)
            {
                if (best == null || res.Score > bestScore)
                {
                    best = res.Parts;
                    bestScore = res.Score;
                }
            }

            return best;
        }

    }
}
