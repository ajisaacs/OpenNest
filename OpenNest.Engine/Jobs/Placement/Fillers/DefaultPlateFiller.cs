using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using OpenNest.Engine;
using OpenNest.Engine.BestFit;
using OpenNest.Engine.Fill;
using OpenNest.Engine.RectanglePacking;
using OpenNest.Engine.Strategies;
using OpenNest.Geometry;
using OpenNest.Math;

namespace OpenNest.Engine.Jobs.Placement.Fillers;

internal class DefaultPlateFiller : PlateFillerBase
{
    private readonly AngleCandidateBuilder angleBuilder = new();

    internal DefaultPlateFiller(Plate plate)
        : base(plate) { }

    protected override IFillComparer CreateComparer() => new DefaultFillComparer();

    internal bool ForceFullAngleSweep
    {
        get => angleBuilder.ForceFullSweep;
        set => angleBuilder.ForceFullSweep = value;
    }

    public override List<double> BuildAngles(
        NestItem item,
        ClassificationResult classification,
        Box workArea
    ) => angleBuilder.Build(item, classification, workArea);

    protected override void RecordProductiveAngles(List<AngleResult> angleResults)
    {
        angleBuilder.RecordProductive(angleResults);
    }

    public override List<Part> Fill(
        NestItem item,
        Box workArea,
        IProgress<NestProgress> progress,
        CancellationToken token
    )
    {
        PhaseResults.Clear();
        AngleResults.Clear();

        // Replace the item's Drawing with a canonical copy for the duration of this fill.
        // All internal methods see canonical geometry; this wrapper un-canonicalizes the final result.
        var originalDrawing = item.Drawing;
        var canonicalItem = new NestItem
        {
            Drawing = CanonicalFrame.AsCanonicalCopy(item.Drawing),
            Quantity = item.Quantity,
            Priority = item.Priority,
            RotationStart = item.RotationStart,
            RotationEnd = item.RotationEnd,
            StepAngle = item.StepAngle,
        };

        // Fast path for qty 1-2.
        if (canonicalItem.Quantity > 0 && canonicalItem.Quantity <= 2)
        {
            var fast = TryFillSmallQuantity(canonicalItem, workArea);
            if (fast != null && fast.Count >= canonicalItem.Quantity)
            {
                Debug.WriteLine(
                    $"[Fill] Fast path: placed {fast.Count} parts for qty={canonicalItem.Quantity}"
                );
                WinnerPhase = NestPhase.Pairs;
                fast = RebindAndUnCanonicalize(fast, originalDrawing);
                NestProgressReporter.Report(
                    progress,
                    new ProgressReport
                    {
                        Phase = WinnerPhase,
                        PlateNumber = PlateNumber,
                        Parts = fast,
                        WorkArea = workArea,
                        Description = $"Fast path: {fast.Count} parts",
                        IsOverallBest = true,
                    }
                );
                return fast;
            }
        }

        var effectiveWorkArea = workArea;
        if (canonicalItem.Quantity > 0)
        {
            effectiveWorkArea = ShrinkWorkArea(canonicalItem, workArea, Plate.PartSpacing);
            if (effectiveWorkArea != workArea)
                Debug.WriteLine(
                    $"[Fill] Low-qty shrink: {canonicalItem.Quantity} requested, "
                        + $"from {workArea.Width:F1}x{workArea.Length:F1} "
                        + $"to {effectiveWorkArea.Width:F1}x{effectiveWorkArea.Length:F1}"
                );
        }

        var best = RunFillPipeline(canonicalItem, originalDrawing, effectiveWorkArea, progress, token);

        if (
            canonicalItem.Quantity > 0
            && best.Count < canonicalItem.Quantity
            && effectiveWorkArea != workArea
        )
        {
            Debug.WriteLine(
                $"[Fill] Low-qty fallback: got {best.Count}, need {canonicalItem.Quantity}, retrying full area"
            );
            PhaseResults.Clear();
            AngleResults.Clear();
            best = RunFillPipeline(canonicalItem, originalDrawing, workArea, progress, token);
        }

        if (canonicalItem.Quantity > 0 && best.Count > canonicalItem.Quantity)
            best = ShrinkFiller.TrimToCount(best, canonicalItem.Quantity, TrimAxis);

        best = RebindAndUnCanonicalize(best, originalDrawing);

        NestProgressReporter.Report(
            progress,
            new ProgressReport
            {
                Phase = WinnerPhase,
                PlateNumber = PlateNumber,
                Parts = best,
                WorkArea = workArea,
                Description = BuildProgressSummary(),
                IsOverallBest = true,
            }
        );

        return best;
    }

    /// <summary>
    /// Single exit point for canonical -> source frame conversion. Rebinds every Part to the
    /// original Drawing (so consumers see the user's drawing identity, not the transient canonical copy)
    /// and composes the canonical angle onto each Part's rotation via CanonicalFrame.RebindToOriginal.
    /// </summary>
    private static List<Part> RebindAndUnCanonicalize(List<Part> parts, Drawing original) =>
        CanonicalFrame.RebindToOriginal(parts, original);

    /// <summary>
    /// Fast path for qty 1-2: place a single part or a best-fit pair
    /// without running the full strategy pipeline.
    /// </summary>
    private List<Part> TryFillSmallQuantity(NestItem item, Box workArea)
    {
        if (item.Quantity == 1)
            return TryPlaceSingle(item.Drawing, workArea);

        if (item.Quantity == 2)
            return TryPlaceBestFitPair(item.Drawing, workArea);

        return null;
    }

    private static List<Part> TryPlaceSingle(Drawing drawing, Box workArea)
    {
        var part = Part.CreateAtOrigin(drawing);
        if (
            part.BoundingBox.Width > workArea.Width + Tolerance.Epsilon
            || part.BoundingBox.Length > workArea.Length + Tolerance.Epsilon
        )
            return null;

        part.Offset(workArea.Location - part.BoundingBox.Location);
        return new List<Part> { part };
    }

    private List<Part> TryPlaceBestFitPair(Drawing drawing, Box workArea)
    {
        var bestFits = BestFitCache.GetOrCompute(
            drawing,
            Plate.Size.Length,
            Plate.Size.Width,
            Plate.PartSpacing
        );

        // Build pair candidates with a canonical drawing so their geometry matches
        // the coordinate frame of the cached fit results.
        var canonicalDrawing = CanonicalFrame.AsCanonicalCopy(drawing);

        List<Part> bestPlacement = null;

        foreach (var fit in bestFits)
        {
            if (!fit.Keep)
                continue;

            // Skip pairs that can't possibly fit the work area in either orientation.
            if (
                fit.ShortestSide
                > System.Math.Min(workArea.Width, workArea.Length) + Tolerance.Epsilon
            )
                continue;
            if (
                fit.LongestSide
                > System.Math.Max(workArea.Width, workArea.Length) + Tolerance.Epsilon
            )
                continue;

            var landscape = fit.BuildParts(canonicalDrawing);
            var portrait = RotatePair90(landscape);

            var lFits = TryOffsetToWorkArea(landscape, workArea);
            var pFits = TryOffsetToWorkArea(portrait, workArea);

            // Pick the better orientation for this pair.
            List<Part> candidate = null;
            if (lFits && pFits)
                candidate = IsBetterFill(portrait, landscape, workArea) ? portrait : landscape;
            else if (lFits)
                candidate = landscape;
            else if (pFits)
                candidate = portrait;

            if (candidate == null)
                continue;

            if (bestPlacement == null || IsBetterFill(candidate, bestPlacement, workArea))
                bestPlacement = candidate;
        }

        // Parts are returned in canonical frame, bound to the canonical drawing.
        // The outer Fill wrapper rebinds to `drawing` and composes sourceAngle onto rotation.
        return bestPlacement;
    }

    private static List<Part> RotatePair90(List<Part> parts)
    {
        var rotated = new List<Part>(parts.Count);
        foreach (var part in parts)
            rotated.Add((Part)part.Clone());

        var bbox = ((IEnumerable<IBoundable>)rotated).GetBoundingBox();
        var center = bbox.Center;

        foreach (var part in rotated)
            part.Rotate(-Angle.HalfPI, center);

        var newBbox = ((IEnumerable<IBoundable>)rotated).GetBoundingBox();
        var offset = new Vector(-newBbox.Left, -newBbox.Bottom);
        foreach (var part in rotated)
        {
            part.Offset(offset);
            part.UpdateBounds();
        }

        return rotated;
    }

    private static bool TryOffsetToWorkArea(List<Part> parts, Box workArea)
    {
        var bbox = ((IEnumerable<IBoundable>)parts).GetBoundingBox();
        if (
            bbox.Width > workArea.Width + Tolerance.Epsilon
            || bbox.Length > workArea.Length + Tolerance.Epsilon
        )
            return false;

        var offset = workArea.Location - bbox.Location;
        foreach (var part in parts)
        {
            part.Offset(offset);
            part.UpdateBounds();
        }
        return true;
    }

    /// <summary>
    /// Shrinks the work area in both dimensions proportionally when the
    /// requested quantity is much less than the plate capacity.
    /// </summary>
    private static Box ShrinkWorkArea(NestItem item, Box workArea, double spacing)
    {
        var bbox = item.Drawing.Program.BoundingBox();
        if (bbox.Width <= 0 || bbox.Length <= 0)
            return workArea;

        var bin = new Bin { Size = new Size(workArea.Width, workArea.Length) };
        var packItem = new Item
        {
            Size = new Size(bbox.Width + spacing, bbox.Length + spacing),
        };
        var packer = new FillBestFit(bin);
        packer.Fill(packItem);
        var fullCount = bin.Items.Count;

        if (fullCount <= 0 || fullCount <= item.Quantity)
            return workArea;

        // Scale both dimensions by sqrt(ratio) so the area shrinks
        // proportionally. 2x margin gives strategies room to optimize.
        var ratio = (double)item.Quantity / fullCount;
        var scale = System.Math.Sqrt(ratio) * 2.0;

        var newWidth = workArea.Width * scale;
        var newLength = workArea.Length * scale;

        // Ensure at least one part fits.
        var minWidth = bbox.Width + spacing * 2;
        var minLength = bbox.Length + spacing * 2;
        newWidth = System.Math.Max(newWidth, minWidth);
        newLength = System.Math.Max(newLength, minLength);

        // Clamp to original dimensions.
        newWidth = System.Math.Min(newWidth, workArea.Width);
        newLength = System.Math.Min(newLength, workArea.Length);

        if (newWidth >= workArea.Width && newLength >= workArea.Length)
            return workArea;

        return new Box(workArea.X, workArea.Y, newLength, newWidth);
    }

    private List<Part> RunFillPipeline(
        NestItem item,
        Drawing originalDrawing,
        Box workArea,
        IProgress<NestProgress> progress,
        CancellationToken token
    )
    {
        var context = new FillContext
        {
            Item = item,
            OriginalDrawing = originalDrawing,
            WorkArea = workArea,
            Plate = Plate,
            PlateNumber = PlateNumber,
            Token = token,
            Progress = progress,
            Policy = BuildPolicy(),
            MaxQuantity = item.Quantity,
        };
        RunPipeline(context);

        AngleResults.AddRange(context.AngleResults);
        WinnerPhase = context.WinnerPhase;

        return context.CurrentBest ?? new List<Part>();
    }

    public override List<Part> Fill(
        List<Part> groupParts,
        Box workArea,
        IProgress<NestProgress> progress,
        CancellationToken token
    )
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
        var engine = new FillLinear(workArea, Plate.PartSpacing) { Label = "GroupPattern" };
        var angles = RotationAnalysis.FindHullEdgeAngles(groupParts);
        var best = FillHelpers.FillPattern(engine, groupParts, angles, workArea, Comparer);
        PhaseResults.Add(new PhaseResult(NestPhase.Linear, best?.Count ?? 0, 0));

        Debug.WriteLine(
            $"[Fill(groupParts,Box)] Linear pattern: {best?.Count ?? 0} parts | WorkArea: {workArea.Width:F1}x{workArea.Length:F1}"
        );

        NestProgressReporter.Report(
            progress,
            new ProgressReport
            {
                Phase = NestPhase.Linear,
                PlateNumber = PlateNumber,
                Parts = best,
                WorkArea = workArea,
                Description = BuildProgressSummary(),
                IsOverallBest = true,
            }
        );

        return best ?? new List<Part>();
    }

    public override List<Part> PackArea(
        Box box,
        List<NestItem> items,
        IProgress<NestProgress> progress,
        CancellationToken token
    )
    {
        var binItems = BinConverter.ToItems(items, Plate.PartSpacing, Plate.Area());
        var bin = BinConverter.CreateBin(box, Plate.PartSpacing);

        var engine = new PackBottomLeft(bin);
        engine.Pack(binItems);

        return BinConverter.ToParts(bin, items);
    }

    protected virtual void RunPipeline(FillContext context)
    {
        var classification = PartClassifier.Classify(context.Item.Drawing);
        context.PartType = classification.Type;
        context.SharedState["BestRotation"] = classification.PrimaryAngle;
        context.SharedState["Classification"] = classification;

        var angles = BuildAngles(context.Item, classification, context.WorkArea);
        context.SharedState["AngleCandidates"] = angles;

        try
        {
            foreach (var strategy in FillStrategyRegistry.Strategies)
            {
                context.Token.ThrowIfCancellationRequested();
                context.ActivePhase = strategy.Phase;

                var stopwatch = Stopwatch.StartNew();
                var result = strategy.Fill(context);
                stopwatch.Stop();

                var phaseResult = new PhaseResult(
                    strategy.Phase,
                    result?.Count ?? 0,
                    stopwatch.ElapsedMilliseconds
                );
                context.PhaseResults.Add(phaseResult);

                // Keep filler PhaseResults in sync so BuildProgressSummary() works
                // during progress reporting.
                PhaseResults.Add(phaseResult);

                // FillContext.ReportProgress updates CurrentBest during the
                // strategy's angle sweep. This catches strategies that return a
                // result without reporting it (e.g. RectBestFit).
                var improved = context.Policy.Comparer.IsBetter(
                    result,
                    context.CurrentBest,
                    context.WorkArea
                );
                if (improved)
                {
                    context.CurrentBest = result;
                    context.CurrentBestScore = FillScore.Compute(result, context.WorkArea);
                    context.WinnerPhase = strategy.Phase;
                }

                if (improved && context.CurrentBest != null && context.CurrentBest.Count > 0)
                {
                    NestProgressReporter.Report(
                        context.Progress,
                        new ProgressReport
                        {
                            Phase = context.WinnerPhase,
                            PlateNumber = PlateNumber,
                            Parts = context.ToOriginalFrame(context.CurrentBest),
                            WorkArea = context.WorkArea,
                            Description = BuildProgressSummary(),
                            IsOverallBest = true,
                        }
                    );
                }
            }
        }
        catch (OperationCanceledException)
        {
            Debug.WriteLine("[RunPipeline] Cancelled, returning current best");
        }

        RecordProductiveAngles(context.AngleResults);
    }
}
