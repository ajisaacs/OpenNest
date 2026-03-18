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

            var context = new FillContext
            {
                Item = item,
                WorkArea = workArea,
                Plate = Plate,
                PlateNumber = PlateNumber,
                Token = token,
                Progress = progress,
            };

            RunPipeline(context);

            // PhaseResults already synced during RunPipeline.
            AngleResults.AddRange(context.AngleResults);
            WinnerPhase = context.WinnerPhase;

            var best = context.CurrentBest ?? new List<Part>();

            if (item.Quantity > 0 && best.Count > item.Quantity)
                best = best.Take(item.Quantity).ToList();

            ReportProgress(progress, WinnerPhase, PlateNumber, best, workArea, BuildProgressSummary());

            return best;
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
                    var binItem = BinConverter.ToItem(nestItem, Plate.PartSpacing);
                    var bin = BinConverter.CreateBin(workArea, Plate.PartSpacing);
                    var rectEngine = new FillBestFit(bin);
                    rectEngine.Fill(binItem);
                    var rectResult = BinConverter.ToParts(bin, new List<NestItem> { nestItem });
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

        // --- RunPipeline: strategy-based orchestration ---

        private void RunPipeline(FillContext context)
        {
            var bestRotation = RotationAnalysis.FindBestRotation(context.Item);
            context.SharedState["BestRotation"] = bestRotation;

            var angles = angleBuilder.Build(context.Item, bestRotation, context.WorkArea);
            context.SharedState["AngleCandidates"] = angles;

            try
            {
                foreach (var strategy in FillStrategyRegistry.Strategies)
                {
                    context.Token.ThrowIfCancellationRequested();

                    var sw = Stopwatch.StartNew();
                    var result = strategy.Fill(context);
                    sw.Stop();

                    var phaseResult = new PhaseResult(
                        strategy.Phase, result?.Count ?? 0, sw.ElapsedMilliseconds);
                    context.PhaseResults.Add(phaseResult);

                    // Keep engine's PhaseResults in sync so BuildProgressSummary() works
                    // during progress reporting.
                    PhaseResults.Add(phaseResult);

                    if (IsBetterFill(result, context.CurrentBest, context.WorkArea))
                    {
                        context.CurrentBest = result;
                        context.CurrentBestScore = FillScore.Compute(result, context.WorkArea);
                        context.WinnerPhase = strategy.Phase;
                        ReportProgress(context.Progress, strategy.Phase, PlateNumber,
                            result, context.WorkArea, BuildProgressSummary());
                    }
                }
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine("[RunPipeline] Cancelled, returning current best");
            }

            angleBuilder.RecordProductive(context.AngleResults);
        }

        // --- Pattern helpers ---

        internal static Pattern BuildRotatedPattern(List<Part> groupParts, double angle)
            => FillHelpers.BuildRotatedPattern(groupParts, angle);

        internal static List<Part> FillPattern(FillLinear engine, List<Part> groupParts, List<double> angles, Box workArea)
            => FillHelpers.FillPattern(engine, groupParts, angles, workArea);

    }
}
