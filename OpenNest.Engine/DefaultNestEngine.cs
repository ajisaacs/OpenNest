using OpenNest.Engine.Fill;
using OpenNest.Engine.Strategies;
using OpenNest.Geometry;
using OpenNest.RectanglePacking;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;

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

            // Single part: delegate to the strategy pipeline.
            if (groupParts.Count == 1)
            {
                var nestItem = new NestItem { Drawing = groupParts[0].BaseDrawing };
                return Fill(nestItem, workArea, progress, token);
            }

            // Multi-part group: linear pattern fill only.
            PhaseResults.Clear();
            var engine = new FillLinear(workArea, Plate.PartSpacing);
            var angles = RotationAnalysis.FindHullEdgeAngles(groupParts);
            var best = FillHelpers.FillPattern(engine, groupParts, angles, workArea);
            PhaseResults.Add(new PhaseResult(NestPhase.Linear, best?.Count ?? 0, 0));

            Debug.WriteLine($"[Fill(groupParts,Box)] Linear pattern: {best?.Count ?? 0} parts | WorkArea: {workArea.Width:F1}x{workArea.Length:F1}");

            ReportProgress(progress, NestPhase.Linear, PlateNumber, best, workArea, BuildProgressSummary());

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

    }
}
