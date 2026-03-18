using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OpenNest.Engine.BestFit;
using OpenNest.Geometry;
using OpenNest.Math;
using OpenNest.RectanglePacking;

namespace OpenNest
{
    public class DefaultNestEngine : NestEngineBase
    {
        public DefaultNestEngine(Plate plate) : base(plate) { }

        public override string Name => "Default";

        public override string Description => "Multi-phase nesting (Linear, Pairs, RectBestFit, Extents)";

        private readonly AngleCandidateBuilder angleBuilder = new();

        public bool ForceFullAngleSweep
        {
            get => angleBuilder.ForceFullSweep;
            set => angleBuilder.ForceFullSweep = value;
        }

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
                drawing, Plate.Size.Length, Plate.Size.Width, Plate.PartSpacing);
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

                    var pairFiller = new PairFiller(Plate.Size, Plate.PartSpacing);
                    var pairResult = pairFiller.Fill(nestItem, workArea, PlateNumber, token, progress);
                    PhaseResults.Add(new PhaseResult(NestPhase.Pairs, pairResult.Count, 0));

                    Debug.WriteLine($"[Fill(groupParts,Box)] Pair: {pairResult.Count} parts | Winner: {(IsBetterFill(pairResult, best, workArea) ? "Pair" : "Linear")}");

                    if (IsBetterFill(pairResult, best, workArea))
                    {
                        best = pairResult;
                        ReportProgress(progress, NestPhase.Pairs, PlateNumber, best, workArea, BuildProgressSummary());
                    }

                    token.ThrowIfCancellationRequested();

                    var extentsFiller = new FillExtents(workArea, Plate.PartSpacing);
                    var bestFits2 = BestFitCache.GetOrCompute(
                        groupParts[0].BaseDrawing, Plate.Size.Length, Plate.Size.Width, Plate.PartSpacing);
                    var extentsAngles2 = new[] { groupParts[0].Rotation, groupParts[0].Rotation + Angle.HalfPI };
                    List<Part> bestExtents2 = null;

                    foreach (var angle in extentsAngles2)
                    {
                        token.ThrowIfCancellationRequested();
                        var result = extentsFiller.Fill(groupParts[0].BaseDrawing, angle, PlateNumber, token, progress, bestFits2);
                        if (result != null && result.Count > (bestExtents2?.Count ?? 0))
                            bestExtents2 = result;
                    }

                    PhaseResults.Add(new PhaseResult(NestPhase.Extents, bestExtents2?.Count ?? 0, 0));
                    Debug.WriteLine($"[Fill(groupParts,Box)] Extents: {bestExtents2?.Count ?? 0} parts");

                    if (IsBetterFill(bestExtents2, best, workArea))
                    {
                        best = bestExtents2;
                        ReportProgress(progress, NestPhase.Extents, PlateNumber, best, workArea, BuildProgressSummary());
                    }
                }
                catch (OperationCanceledException)
                {
                    Debug.WriteLine("[Fill(groupParts,Box)] Cancelled, returning current best");
                }
            }

            // Always report the final winner so the UI's temporary parts
            // match the returned result.
            var winPhase = PhaseResults.Count > 0
                ? PhaseResults.OrderByDescending(r => r.PartCount).First().Phase
                : NestPhase.Linear;
            ReportProgress(progress, winPhase, PlateNumber, best, workArea, BuildProgressSummary());

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
                var angles = angleBuilder.Build(item, bestRotation, workArea);

                // Pairs phase
                var pairSw = Stopwatch.StartNew();
                var pairFiller = new PairFiller(Plate.Size, Plate.PartSpacing);
                var pairResult = pairFiller.Fill(item, workArea, PlateNumber, token, progress);
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

                angleBuilder.RecordProductive(AngleResults);

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

                // Extents phase — reuse the BestFit cache from the Pairs phase.
                token.ThrowIfCancellationRequested();
                var extentsSw = Stopwatch.StartNew();
                var extentsFiller = new FillExtents(workArea, Plate.PartSpacing);
                var bestFits = BestFitCache.GetOrCompute(
                    item.Drawing, Plate.Size.Length, Plate.Size.Width, Plate.PartSpacing);
                List<Part> bestExtents = null;
                var extentsAngles = new[] { bestRotation, bestRotation + Angle.HalfPI };

                foreach (var angle in extentsAngles)
                {
                    token.ThrowIfCancellationRequested();
                    var extentsResult = extentsFiller.Fill(item.Drawing, angle, PlateNumber, token, progress, bestFits);
                    if (bestExtents == null || (extentsResult != null && extentsResult.Count > (bestExtents?.Count ?? 0)))
                        bestExtents = extentsResult;
                }

                extentsSw.Stop();
                var extentsScore = bestExtents != null ? FillScore.Compute(bestExtents, workArea) : default;
                Debug.WriteLine($"[FindBestFill] Extents: {extentsScore.Count} parts");
                PhaseResults.Add(new PhaseResult(NestPhase.Extents, bestExtents?.Count ?? 0, extentsSw.ElapsedMilliseconds));

                var bestScore2 = FillScore.Compute(best, workArea);
                if (extentsScore > bestScore2)
                {
                    best = bestExtents;
                    WinnerPhase = NestPhase.Extents;
                    ReportProgress(progress, NestPhase.Extents, PlateNumber, best, workArea, BuildProgressSummary());
                }
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine("[FindBestFill] Cancelled, returning current best");
            }

            // Always report the final winner so the UI's temporary parts
            // match the returned result (sub-phases may have reported their
            // own intermediate results via progress).
            ReportProgress(progress, WinnerPhase, PlateNumber, best, workArea, BuildProgressSummary());

            return best ?? new List<Part>();
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

        // --- Pattern helpers ---

        internal static Pattern BuildRotatedPattern(List<Part> groupParts, double angle)
            => FillHelpers.BuildRotatedPattern(groupParts, angle);

        internal static List<Part> FillPattern(FillLinear engine, List<Part> groupParts, List<double> angles, Box workArea)
            => FillHelpers.FillPattern(engine, groupParts, angles, workArea);

    }
}
