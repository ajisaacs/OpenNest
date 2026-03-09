using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
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

            var partLongestSide = System.Math.Max(testPart.BoundingBox.Width, testPart.BoundingBox.Height);
            var workAreaShortSide = System.Math.Min(workArea.Width, workArea.Height);

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

            List<Part> best = null;

            foreach (var angle in angles)
            {
                var h = engine.Fill(item.Drawing, angle, NestDirection.Horizontal);
                var v = engine.Fill(item.Drawing, angle, NestDirection.Vertical);

                if (IsBetterFill(h, best))
                    best = h;

                if (IsBetterFill(v, best))
                    best = v;
            }

            Debug.WriteLine($"[Fill(NestItem,Box)] Linear: {best?.Count ?? 0} parts | WorkArea: {workArea.Width:F1}x{workArea.Height:F1} | Angles: {angles.Count}");

            // Try rectangle best-fit (mixes orientations to fill remnant strips).
            var rectResult = FillRectangleBestFit(item, workArea);

            Debug.WriteLine($"[Fill(NestItem,Box)] RectBestFit: {rectResult?.Count ?? 0} parts");

            if (IsBetterFill(rectResult, best))
                best = rectResult;

            // Try pair-based approach.
            var pairResult = FillWithPairs(item, workArea);

            Debug.WriteLine($"[Fill(NestItem,Box)] Pair: {pairResult.Count} parts | Winner: {(IsBetterFill(pairResult, best) ? "Pair" : "Linear")}");

            if (IsBetterFill(pairResult, best))
                best = pairResult;

            if (best == null || best.Count == 0)
                return false;

            if (item.Quantity > 0 && best.Count > item.Quantity)
                best = best.Take(item.Quantity).ToList();

            Plate.Parts.AddRange(best);
            return true;
        }

        public bool Fill(List<Part> groupParts, Box workArea)
        {
            if (groupParts == null || groupParts.Count == 0)
                return false;

            var engine = new FillLinear(workArea, Plate.PartSpacing);
            var angles = RotationAnalysis.FindHullEdgeAngles(groupParts);
            var best = FillPattern(engine, groupParts, angles);

            Debug.WriteLine($"[Fill(groupParts,Box)] Linear: {best?.Count ?? 0} parts | WorkArea: {workArea.Width:F1}x{workArea.Height:F1}");

            if (groupParts.Count == 1)
            {
                var nestItem = new NestItem { Drawing = groupParts[0].BaseDrawing };
                var rectResult = FillRectangleBestFit(nestItem, workArea);

                Debug.WriteLine($"[Fill(groupParts,Box)] RectBestFit: {rectResult?.Count ?? 0} parts");

                if (IsBetterFill(rectResult, best))
                    best = rectResult;

                var pairResult = FillWithPairs(nestItem, workArea);

                Debug.WriteLine($"[Fill(groupParts,Box)] Pair: {pairResult.Count} parts | Winner: {(IsBetterFill(pairResult, best) ? "Pair" : "Linear")}");

                if (IsBetterFill(pairResult, best))
                    best = pairResult;
            }

            if (best == null || best.Count == 0)
                return false;

            Plate.Parts.AddRange(best);
            return true;
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
                item.Drawing, Plate.Size.Width, Plate.Size.Height,
                Plate.PartSpacing);

            var candidates = SelectPairCandidates(bestFits, workArea);
            Debug.WriteLine($"[FillWithPairs] Total: {bestFits.Count}, Kept: {bestFits.Count(r => r.Keep)}, Trying: {candidates.Count}");

            var resultBag = new System.Collections.Concurrent.ConcurrentBag<(int count, List<Part> parts)>();

            System.Threading.Tasks.Parallel.For(0, candidates.Count, i =>
            {
                var result = candidates[i];
                var pairParts = result.BuildParts(item.Drawing);
                var angles = RotationAnalysis.FindHullEdgeAngles(pairParts);
                var engine = new FillLinear(workArea, Plate.PartSpacing);
                var filled = FillPattern(engine, pairParts, angles);

                if (filled != null && filled.Count > 0)
                    resultBag.Add((filled.Count, filled));
            });

            List<Part> best = null;

            foreach (var (count, parts) in resultBag)
            {
                if (best == null || count > best.Count)
                    best = parts;
            }

            Debug.WriteLine($"[FillWithPairs] Best pair result: {best?.Count ?? 0} parts");
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

            var workShortSide = System.Math.Min(workArea.Width, workArea.Height);
            var plateShortSide = System.Math.Min(Plate.Size.Width, Plate.Size.Height);

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
                for (var j = i + 1; j < parts.Count; j++)
                {
                    List<Vector> pts;

                    if (parts[i].Intersects(parts[j], out pts))
                        return true;
                }
            }

            return false;
        }

        private bool IsBetterFill(List<Part> candidate, List<Part> current)
        {
            if (candidate == null || candidate.Count == 0)
                return false;

            if (current == null || current.Count == 0)
                return true;

            if (candidate.Count != current.Count)
                return candidate.Count > current.Count;

            // Same count: prefer smaller bounding box (more compact).
            var candidateBox = ((IEnumerable<IBoundable>)candidate).GetBoundingBox();
            var currentBox = ((IEnumerable<IBoundable>)current).GetBoundingBox();

            return candidateBox.Area() < currentBox.Area();
        }

        private bool IsBetterValidFill(List<Part> candidate, List<Part> current)
        {
            if (candidate != null && candidate.Count > 0 && HasOverlaps(candidate, Plate.PartSpacing))
                return false;

            return IsBetterFill(candidate, current);
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

        private List<Part> FillPattern(FillLinear engine, List<Part> groupParts, List<double> angles)
        {
            List<Part> best = null;

            foreach (var angle in angles)
            {
                var pattern = BuildRotatedPattern(groupParts, angle);

                if (pattern.Parts.Count == 0)
                    continue;

                var h = engine.Fill(pattern, NestDirection.Horizontal);
                var v = engine.Fill(pattern, NestDirection.Vertical);

                if (IsBetterValidFill(h, best))
                    best = h;

                if (IsBetterValidFill(v, best))
                    best = v;
            }

            return best;
        }

    }
}
