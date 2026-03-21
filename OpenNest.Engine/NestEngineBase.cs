using OpenNest.Engine.Fill;
using OpenNest.Geometry;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;

namespace OpenNest
{
    public abstract class NestEngineBase
    {
        protected NestEngineBase(Plate plate)
        {
            Plate = plate;
        }

        public Plate Plate { get; set; }

        public int PlateNumber { get; set; }

        public NestDirection NestDirection { get; set; }

        public NestPhase WinnerPhase { get; protected set; }

        public List<PhaseResult> PhaseResults { get; } = new();

        public List<AngleResult> AngleResults { get; } = new();

        public abstract string Name { get; }

        public abstract string Description { get; }

        // --- Virtual methods (side-effect-free, return parts) ---

        public virtual List<Part> Fill(NestItem item, Box workArea,
            IProgress<NestProgress> progress, CancellationToken token)
        {
            return new List<Part>();
        }

        public virtual List<Part> Fill(List<Part> groupParts, Box workArea,
            IProgress<NestProgress> progress, CancellationToken token)
        {
            return new List<Part>();
        }

        public virtual List<Part> PackArea(Box box, List<NestItem> items,
            IProgress<NestProgress> progress, CancellationToken token)
        {
            return new List<Part>();
        }

        // --- Nest: multi-item strategy (virtual, side-effect-free) ---

        public virtual List<Part> Nest(List<NestItem> items,
            IProgress<NestProgress> progress, CancellationToken token)
        {
            if (items == null || items.Count == 0)
                return new List<Part>();

            var workArea = Plate.WorkArea();
            var allParts = new List<Part>();

            var fillItems = items
                .Where(i => i.Quantity != 1)
                .OrderBy(i => i.Priority)
                .ThenByDescending(i => i.Drawing.Area)
                .ToList();

            var packItems = items
                .Where(i => i.Quantity == 1)
                .ToList();

            // Phase 1: Fill multi-quantity drawings using RemnantFiller.
            if (fillItems.Count > 0)
            {
                var remnantFiller = new RemnantFiller(workArea, Plate.PartSpacing);

                Func<NestItem, Box, List<Part>> fillFunc = (ni, b) =>
                    FillExact(ni, b, progress, token);

                var fillParts = remnantFiller.FillItems(fillItems, fillFunc, token, progress);

                if (fillParts.Count > 0)
                {
                    allParts.AddRange(fillParts);

                    // Deduct placed quantities
                    foreach (var item in fillItems)
                    {
                        var placed = fillParts.Count(p =>
                            p.BaseDrawing.Name == item.Drawing.Name);
                        item.Quantity = System.Math.Max(0, item.Quantity - placed);
                    }

                    // Update workArea for pack phase
                    var placedObstacles = fillParts.Select(p => p.BoundingBox.Offset(Plate.PartSpacing)).ToList();
                    var finder = new RemnantFinder(workArea, placedObstacles);
                    var remnants = finder.FindRemnants();
                    if (remnants.Count > 0)
                        workArea = remnants[0];
                    else
                        workArea = new Box(0, 0, 0, 0);
                }
            }

            // Phase 2: Pack single-quantity items into remaining space.
            packItems = packItems.Where(i => i.Quantity > 0).ToList();

            if (packItems.Count > 0 && workArea.Width > 0 && workArea.Length > 0
                && !token.IsCancellationRequested)
            {
                var packParts = PackArea(workArea, packItems, progress, token);

                if (packParts.Count > 0)
                {
                    allParts.AddRange(packParts);

                    foreach (var item in packItems)
                    {
                        var placed = packParts.Count(p =>
                            p.BaseDrawing.Name == item.Drawing.Name);
                        item.Quantity = System.Math.Max(0, item.Quantity - placed);
                    }
                }
            }

            // Compact placed parts toward the origin to close gaps.
            Compactor.Settle(allParts, Plate.WorkArea(), Plate.PartSpacing);

            return allParts;
        }

        // --- FillExact (non-virtual, delegates to virtual Fill) ---

        public List<Part> FillExact(NestItem item, Box workArea,
            IProgress<NestProgress> progress, CancellationToken token)
        {
            return Fill(item, workArea, progress, token);
        }

        // --- Convenience overloads (mutate plate, return bool) ---

        public bool Fill(NestItem item)
        {
            return Fill(item, Plate.WorkArea());
        }

        public bool Fill(NestItem item, Box workArea)
        {
            var parts = Fill(item, workArea, null, CancellationToken.None);

            if (parts == null || parts.Count == 0)
                return false;

            Plate.Parts.AddRange(parts);
            return true;
        }

        public bool Fill(List<Part> groupParts)
        {
            return Fill(groupParts, Plate.WorkArea());
        }

        public bool Fill(List<Part> groupParts, Box workArea)
        {
            var parts = Fill(groupParts, workArea, null, CancellationToken.None);

            if (parts == null || parts.Count == 0)
                return false;

            Plate.Parts.AddRange(parts);
            return true;
        }

        public bool Pack(List<NestItem> items)
        {
            var workArea = Plate.WorkArea();
            var parts = PackArea(workArea, items, null, CancellationToken.None);

            if (parts == null || parts.Count == 0)
                return false;

            Plate.Parts.AddRange(parts);
            return true;
        }

        // --- Protected utilities ---

        internal static void ReportProgress(
            IProgress<NestProgress> progress,
            NestPhase phase,
            int plateNumber,
            List<Part> best,
            Box workArea,
            string description,
            bool isOverallBest = false)
        {
            if (progress == null || best == null || best.Count == 0)
                return;

            var score = FillScore.Compute(best, workArea);
            var clonedParts = new List<Part>(best.Count);
            var totalPartArea = 0.0;

            foreach (var part in best)
            {
                clonedParts.Add((Part)part.Clone());
                totalPartArea += part.BaseDrawing.Area;
            }

            var bounds = best.GetBoundingBox();

            var msg = $"[Progress] Phase={phase}, Plate={plateNumber}, Parts={score.Count}, " +
                      $"Density={score.Density:P1}, Nested={bounds.Width:F1}x{bounds.Length:F1}, " +
                      $"PartArea={totalPartArea:F0}, Remnant={workArea.Area() - totalPartArea:F0}, " +
                      $"WorkArea={workArea.Width:F1}x{workArea.Length:F1} | {description}";
            Debug.WriteLine(msg);
            try
            {
                System.IO.File.AppendAllText(
                System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "nest-debug.log"),
                $"{DateTime.Now:HH:mm:ss.fff} {msg}\n");
            }
            catch { }

            progress.Report(new NestProgress
            {
                Phase = phase,
                PlateNumber = plateNumber,
                BestPartCount = score.Count,
                BestDensity = score.Density,
                NestedWidth = bounds.Width,
                NestedLength = bounds.Length,
                NestedArea = totalPartArea,
                BestParts = clonedParts,
                Description = description,
                ActiveWorkArea = workArea,
                IsOverallBest = isOverallBest,
            });
        }

        protected string BuildProgressSummary()
        {
            if (PhaseResults.Count == 0)
                return null;

            var parts = new List<string>(PhaseResults.Count);

            foreach (var r in PhaseResults)
                parts.Add($"{FormatPhaseName(r.Phase)}: {r.PartCount}");

            return string.Join(" | ", parts);
        }

        protected bool IsBetterFill(List<Part> candidate, List<Part> current, Box workArea)
        {
            if (candidate == null || candidate.Count == 0)
                return false;

            if (current == null || current.Count == 0)
                return true;

            return FillScore.Compute(candidate, workArea) > FillScore.Compute(current, workArea);
        }

        protected bool IsBetterValidFill(List<Part> candidate, List<Part> current, Box workArea)
        {
            if (candidate != null && candidate.Count > 0 && HasOverlaps(candidate, Plate.PartSpacing))
            {
                Debug.WriteLine($"[IsBetterValidFill] REJECTED {candidate.Count} parts due to overlaps (current best: {current?.Count ?? 0})");
                return false;
            }

            return IsBetterFill(candidate, current, workArea);
        }

        protected static bool HasOverlaps(List<Part> parts, double spacing)
        {
            if (parts == null || parts.Count <= 1)
                return false;

            for (var i = 0; i < parts.Count; i++)
            {
                var box1 = parts[i].BoundingBox;

                for (var j = i + 1; j < parts.Count; j++)
                {
                    var box2 = parts[j].BoundingBox;

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

        protected static string FormatPhaseName(NestPhase phase)
        {
            switch (phase)
            {
                case NestPhase.Pairs: return "Pairs";
                case NestPhase.Linear: return "Linear";
                case NestPhase.RectBestFit: return "BestFit";
                case NestPhase.Extents: return "Extents";
                case NestPhase.Custom: return "Custom";
                default: return phase.ToString();
            }
        }
    }
}
