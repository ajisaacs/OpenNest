using OpenNest.Engine;
using OpenNest.Engine.BestFit;
using OpenNest.Engine.Fill;
using OpenNest.Engine.Strategies;
using OpenNest.Geometry;
using OpenNest.Math;
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

        // --- Engine policy ---

        private IFillComparer _comparer;

        protected IFillComparer Comparer => _comparer ??= CreateComparer();

        protected virtual IFillComparer CreateComparer() => new DefaultFillComparer();

        public virtual NestDirection? PreferredDirection => null;

        public virtual ShrinkAxis TrimAxis => ShrinkAxis.Width;

        public virtual List<double> BuildAngles(NestItem item, ClassificationResult classification, Box workArea)
        {
            return new List<double> { classification.PrimaryAngle, classification.PrimaryAngle + OpenNest.Math.Angle.HalfPI };
        }

        protected virtual void RecordProductiveAngles(List<AngleResult> angleResults) { }

        protected FillPolicy BuildPolicy() => new FillPolicy(Comparer, PreferredDirection);

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

            var plateArea = workArea.Width * workArea.Length;

            var fillItems = items
                .Where(i => ShouldFill(i, plateArea))
                .OrderBy(i => i.Priority)
                .ThenByDescending(i => i.Drawing.Area)
                .ToList();

            var packItems = items
                .Where(i => !ShouldFill(i, plateArea))
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

            // Phase 2: Pack low-quantity items into remaining space.
            // Separate qty=2 items — they'll be placed as best-fit pairs after packing.
            packItems = packItems.Where(i => i.Quantity > 0).ToList();
            var pairItems = packItems.Where(i => i.Quantity == 2).ToList();
            var regularPackItems = packItems.Where(i => i.Quantity != 2).ToList();

            if (regularPackItems.Count > 0 && workArea.Width > 0 && workArea.Length > 0
                && !token.IsCancellationRequested)
            {
                var packParts = PackArea(workArea, regularPackItems, progress, token);

                if (packParts.Count > 0)
                {
                    allParts.AddRange(packParts);

                    foreach (var item in regularPackItems)
                    {
                        var placed = packParts.Count(p =>
                            p.BaseDrawing.Name == item.Drawing.Name);
                        item.Quantity = System.Math.Max(0, item.Quantity - placed);
                    }
                }
            }

            // Phase 3: Place best-fit pairs for qty=2 items in remaining space.
            if (pairItems.Count > 0 && !token.IsCancellationRequested)
            {
                var placed = PlaceBestFitPairs(pairItems, allParts, Plate.WorkArea());
                allParts.AddRange(placed);
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
            IProgress<NestProgress> progress, ProgressReport report)
        {
            if (progress == null || report.Parts == null || report.Parts.Count == 0)
                return;

            var clonedParts = new List<Part>(report.Parts.Count);
            foreach (var part in report.Parts)
                clonedParts.Add((Part)part.Clone());

            Debug.WriteLine($"[Progress] Phase={report.Phase}, Plate={report.PlateNumber}, " +
                            $"Parts={clonedParts.Count} | {report.Description}");

            progress.Report(new NestProgress
            {
                Phase = report.Phase,
                PlateNumber = report.PlateNumber,
                BestParts = clonedParts,
                Description = report.Description,
                ActiveWorkArea = report.WorkArea,
                IsOverallBest = report.IsOverallBest,
            });
        }

        protected string BuildProgressSummary()
        {
            if (PhaseResults.Count == 0)
                return null;

            var parts = new List<string>(PhaseResults.Count);

            foreach (var r in PhaseResults)
                parts.Add($"{r.Phase.ShortName()}: {r.PartCount}");

            return string.Join(" | ", parts);
        }

        protected bool IsBetterFill(List<Part> candidate, List<Part> current, Box workArea)
            => Comparer.IsBetter(candidate, current, workArea);

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

                    var overlapX = System.Math.Min(box1.Right, box2.Right)
                                 - System.Math.Max(box1.Left, box2.Left);
                    var overlapY = System.Math.Min(box1.Top, box2.Top)
                                 - System.Math.Max(box1.Bottom, box2.Bottom);

                    if (overlapX <= Tolerance.Epsilon || overlapY <= Tolerance.Epsilon)
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

        /// <summary>
        /// Places best-fit pairs for qty=2 items into remnant spaces around
        /// already-placed parts. Returns all placed pair parts.
        /// </summary>
        private List<Part> PlaceBestFitPairs(List<NestItem> pairItems,
            List<Part> existingParts, Box fullWorkArea)
        {
            var result = new List<Part>();
            var obstacles = existingParts
                .Select(p => p.BoundingBox.Offset(Plate.PartSpacing))
                .ToList();
            var finder = new RemnantFinder(fullWorkArea, obstacles);

            foreach (var item in pairItems)
            {
                if (item.Quantity < 2) continue;

                var bestFits = BestFitCache.GetOrCompute(
                    item.Drawing, Plate.Size.Length, Plate.Size.Width, Plate.PartSpacing);

                // BestFitCache stores pair coordinates in canonical frame. Build candidates
                // from a canonical drawing copy so geometry and coords share a frame; rebind
                // + un-rotate winning pair to the original drawing's frame before returning.
                var canonicalDrawing = CanonicalFrame.AsCanonicalCopy(item.Drawing);
                var sourceAngle = item.Drawing?.Source?.Angle ?? 0.0;

                List<Part> bestPlacement = null;
                Box bestTarget = null;

                foreach (var fit in bestFits)
                {
                    if (!fit.Keep)
                        continue;

                    var parts = fit.BuildParts(canonicalDrawing);
                    var pairBbox = ((IEnumerable<IBoundable>)parts).GetBoundingBox();
                    var pairW = pairBbox.Width;
                    var pairL = pairBbox.Length;
                    var minDim = System.Math.Min(pairW, pairL);

                    var remnants = finder.FindRemnants(minDim);

                    foreach (var r in remnants)
                    {
                        if (pairW <= r.Width + Tolerance.Epsilon &&
                            pairL <= r.Length + Tolerance.Epsilon)
                        {
                            var offset = r.Location - pairBbox.Location;
                            foreach (var p in parts)
                            {
                                p.Offset(offset);
                                p.UpdateBounds();
                            }

                            if (bestPlacement == null || IsBetterFill(parts, bestPlacement, r))
                            {
                                bestPlacement = parts;
                                bestTarget = r;
                            }
                            break;
                        }
                    }
                }

                if (bestPlacement == null) continue;

                // Rebind to the original drawing and compose sourceAngle onto rotation so the
                // final placed parts sit in the user's visible frame.
                bestPlacement = RebindPairToOriginal(bestPlacement, item.Drawing, sourceAngle);

                result.AddRange(bestPlacement);
                item.Quantity = 0;

                var envelope = ((IEnumerable<IBoundable>)bestPlacement).GetBoundingBox();
                finder.AddObstacle(envelope.Offset(Plate.PartSpacing));

                Debug.WriteLine($"[Nest] Placed best-fit pair for {item.Drawing.Name} " +
                    $"at ({bestTarget.X:F1},{bestTarget.Y:F1}), " +
                    $"size {envelope.Width:F1}x{envelope.Length:F1}");
            }

            return result;
        }

        /// <summary>
        /// Rebinds each canonical-frame Part in the pair to the original Drawing at its current
        /// world pose, then composes sourceAngle onto each via CanonicalFrame.FromCanonical so
        /// the returned list is in the original drawing's visible frame. Mirrors
        /// DefaultNestEngine.RebindAndUnCanonicalize.
        /// </summary>
        private static List<Part> RebindPairToOriginal(List<Part> parts, Drawing original, double sourceAngle)
        {
            if (parts == null || parts.Count == 0)
                return parts;

            for (var i = 0; i < parts.Count; i++)
            {
                var p = parts[i];
                var rebound = Part.CreateAtOrigin(original, p.Rotation);
                var delta = p.BoundingBox.Location - rebound.BoundingBox.Location;
                rebound.Offset(delta);
                rebound.UpdateBounds();
                parts[i] = rebound;
            }

            return CanonicalFrame.FromCanonical(parts, sourceAngle);
        }

        /// <summary>
        /// Determines whether a drawing should use grid-fill (true) or bin-pack (false).
        /// Low-quantity items whose total area is a small fraction of the plate are
        /// better off being packed alongside other parts rather than filling first.
        /// </summary>
        private bool ShouldFill(NestItem item, double plateArea)
        {
            if (item.Quantity <= 1)
                return false;

            var bbox = item.Drawing.Program.BoundingBox();
            var partArea = (bbox.Width + Plate.PartSpacing) * (bbox.Length + Plate.PartSpacing);
            if (partArea <= 0)
                return false;

            var totalArea = partArea * item.Quantity;

            // If the total area of all copies is less than 10% of the plate,
            // packing produces better results than grid-filling.
            return totalArea >= plateArea * 0.1;
        }

    }
}
