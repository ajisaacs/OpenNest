using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using OpenNest.Converters;
using OpenNest.Engine.BestFit;
using OpenNest.Engine.BestFit.Tiling;
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

        public Func<Drawing, double, IPairEvaluator> CreateEvaluator { get; set; }

        public bool Fill(NestItem item)
        {
            var sw = Stopwatch.StartNew();

            var workArea = Plate.WorkArea();
            var bestRotation = FindBestRotation(item);

            var engine = new FillLinear(workArea, Plate.PartSpacing);

            // Try 4 configurations: 2 rotations x 2 axes.
            var configs = new[]
            {
                engine.Fill(item.Drawing, bestRotation, NestDirection.Horizontal),
                engine.Fill(item.Drawing, bestRotation, NestDirection.Vertical),
                engine.Fill(item.Drawing, bestRotation + Angle.HalfPI, NestDirection.Horizontal),
                engine.Fill(item.Drawing, bestRotation + Angle.HalfPI, NestDirection.Vertical)
            };

            // Pick the best linear configuration. FillLinear already ensures
            // geometry-aware spacing, so skip the redundant overlap check that
            // can produce false positives on arcs/curves.
            List<Part> linearBest = null;

            foreach (var config in configs)
            {
                if (IsBetterFill(config, linearBest))
                    linearBest = config;
            }

            var linearMs = sw.ElapsedMilliseconds;

            // Try rectangle best-fit (mixes orientations to fill remnant strips).
            var rectResult = FillRectangleBestFit(item, workArea);

            var rectMs = sw.ElapsedMilliseconds - linearMs;

            // Try pair-based approach.
            var pairResult = FillWithPairs(item);

            var pairMs = sw.ElapsedMilliseconds - linearMs - rectMs;

            // Pick whichever is the better fill.
            Debug.WriteLine($"[NestEngine.Fill] Linear: {linearBest?.Count ?? 0} parts ({linearMs}ms) | Rect: {rectResult?.Count ?? 0} parts ({rectMs}ms) | Pair: {pairResult.Count} parts ({pairMs}ms) | WorkArea: {workArea.Width:F1}x{workArea.Height:F1}");
            var best = linearBest;

            if (IsBetterFill(rectResult, best))
                best = rectResult;

            if (IsBetterFill(pairResult, best))
                best = pairResult;

            if (best == null || best.Count == 0)
                return false;

            // Limit to requested quantity if specified.
            if (item.Quantity > 0 && best.Count > item.Quantity)
                best = best.Take(item.Quantity).ToList();

            Plate.Parts.AddRange(best);
            return true;
        }

        public bool Fill(List<Part> groupParts)
        {
            if (groupParts == null || groupParts.Count == 0)
                return false;

            var workArea = Plate.WorkArea();
            var engine = new FillLinear(workArea, Plate.PartSpacing);
            var angles = FindHullEdgeAngles(groupParts);
            var best = FillPattern(engine, groupParts, angles);

            // For single-part groups, also try rectangle best-fit and pair-based filling.
            if (groupParts.Count == 1)
            {
                var nestItem = new NestItem { Drawing = groupParts[0].BaseDrawing };
                var rectResult = FillRectangleBestFit(nestItem, workArea);

                if (IsBetterFill(rectResult, best))
                    best = rectResult;

                var pairResult = FillWithPairs(nestItem);

                if (IsBetterFill(pairResult, best))
                    best = pairResult;
            }

            if (best == null || best.Count == 0)
                return false;

            Plate.Parts.AddRange(best);
            return true;
        }

        public bool Fill(NestItem item, Box workArea)
        {
            var bestRotation = FindBestRotation(item);

            var engine = new FillLinear(workArea, Plate.PartSpacing);

            var configs = new[]
            {
                engine.Fill(item.Drawing, bestRotation, NestDirection.Horizontal),
                engine.Fill(item.Drawing, bestRotation, NestDirection.Vertical),
                engine.Fill(item.Drawing, bestRotation + Angle.HalfPI, NestDirection.Horizontal),
                engine.Fill(item.Drawing, bestRotation + Angle.HalfPI, NestDirection.Vertical)
            };

            List<Part> best = null;

            foreach (var config in configs)
            {
                if (IsBetterFill(config, best))
                    best = config;
            }

            Debug.WriteLine($"[Fill(NestItem,Box)] Linear: {best?.Count ?? 0} parts | WorkArea: {workArea.Width:F1}x{workArea.Height:F1}");

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
            var angles = FindHullEdgeAngles(groupParts);
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

        public bool Fill(NestItem item, int maxCount)
        {
            if (maxCount <= 0)
                return false;

            var savedQty = item.Quantity;
            item.Quantity = maxCount;
            var result = Fill(item);
            item.Quantity = savedQty;
            return result;
        }

        public bool FillArea(Box box, NestItem item)
        {
            var binItem = ConvertToRectangleItem(item);

            var bin = new Bin
            {
                Location = box.Location,
                Size = box.Size
            };

            bin.Width += Plate.PartSpacing;
            bin.Height += Plate.PartSpacing;

            var engine = new FillBestFit(bin);
            engine.Fill(binItem);

            var nestItems = new List<NestItem>();
            nestItems.Add(item);

            var parts = ConvertToParts(bin, nestItems);
            Plate.Parts.AddRange(parts);

            return parts.Count > 0;
        }

        public bool FillArea(Box box, NestItem item, int maxCount)
        {
            var binItem = ConvertToRectangleItem(item);

            var bin = new Bin
            {
                Location = box.Location,
                Size = box.Size
            };

            bin.Width += Plate.PartSpacing;
            bin.Height += Plate.PartSpacing;

            var engine = new FillBestFit(bin);
            engine.Fill(binItem, maxCount);

            var nestItems = new List<NestItem>();
            nestItems.Add(item);

            var parts = ConvertToParts(bin, nestItems);
            Plate.Parts.AddRange(parts);

            return parts.Count > 0;
        }

        public bool Pack(List<NestItem> items)
        {
            var workArea = Plate.WorkArea();
            return PackArea(workArea, items);
        }

        public bool PackArea(Box box, List<NestItem> items)
        {
            var binItems = ConvertToRectangleItems(items);

            var bin = new Bin
            {
                Location = box.Location,
                Size = box.Size
            };

            bin.Width += Plate.PartSpacing;
            bin.Height += Plate.PartSpacing;

            var engine = new PackBottomLeft(bin);
            engine.Pack(binItems);

            var parts = ConvertToParts(bin, items);
            Plate.Parts.AddRange(parts);

            return parts.Count > 0;
        }

        private List<Part> FillRectangleBestFit(NestItem item, Box workArea)
        {
            var binItem = ConvertToRectangleItem(item);

            var bin = new Bin
            {
                Location = workArea.Location,
                Size = workArea.Size
            };

            bin.Width += Plate.PartSpacing;
            bin.Height += Plate.PartSpacing;

            var engine = new FillBestFit(bin);
            engine.Fill(binItem);

            var nestItems = new List<NestItem> { item };
            return ConvertToParts(bin, nestItems);
        }

        private List<Part> FillWithPairs(NestItem item)
        {
            return FillWithPairs(item, Plate.WorkArea());
        }

        private List<Part> FillWithPairs(NestItem item, Box workArea)
        {
            IPairEvaluator evaluator = null;

            if (CreateEvaluator != null)
            {
                try { evaluator = CreateEvaluator(item.Drawing, Plate.PartSpacing); }
                catch { /* GPU not available, fall back to geometry */ }
            }

            var finder = new BestFitFinder(Plate.Size.Width, Plate.Size.Height, evaluator);
            var bestFits = finder.FindBestFits(item.Drawing, Plate.PartSpacing, stepSize: 0.25);

            var keptResults = bestFits.Where(r => r.Keep).Take(50).ToList();
            Debug.WriteLine($"[FillWithPairs] Total: {bestFits.Count}, Kept: {bestFits.Count(r => r.Keep)}, Trying: {keptResults.Count}");

            var resultBag = new System.Collections.Concurrent.ConcurrentBag<(int count, List<Part> parts)>();

            System.Threading.Tasks.Parallel.For(0, keptResults.Count, i =>
            {
                var result = keptResults[i];
                var pairParts = BuildPairParts(result, item.Drawing);
                var angles = FindHullEdgeAngles(pairParts);
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

            (evaluator as IDisposable)?.Dispose();

            Debug.WriteLine($"[FillWithPairs] Best pair result: {best?.Count ?? 0} parts");
            return best ?? new List<Part>();
        }

        public static List<Part> BuildPairParts(BestFitResult bestFit, Drawing drawing)
        {
            var candidate = bestFit.Candidate;

            var part1 = new Part(drawing);
            var bbox1 = part1.Program.BoundingBox();
            part1.Offset(-bbox1.Location.X, -bbox1.Location.Y);
            part1.UpdateBounds();

            var part2 = new Part(drawing);
            if (!candidate.Part2Rotation.IsEqualTo(0))
                part2.Rotate(candidate.Part2Rotation);
            var bbox2 = part2.Program.BoundingBox();
            part2.Offset(-bbox2.Location.X, -bbox2.Location.Y);
            part2.Location = candidate.Part2Offset;
            part2.UpdateBounds();

            if (!bestFit.OptimalRotation.IsEqualTo(0))
            {
                var pairBounds = ((IEnumerable<IBoundable>)new IBoundable[] { part1, part2 }).GetBoundingBox();
                var center = pairBounds.Center;
                part1.Rotate(-bestFit.OptimalRotation, center);
                part2.Rotate(-bestFit.OptimalRotation, center);
            }

            var finalBounds = ((IEnumerable<IBoundable>)new IBoundable[] { part1, part2 }).GetBoundingBox();
            var offset = new Vector(-finalBounds.Left, -finalBounds.Bottom);
            part1.Offset(offset);
            part2.Offset(offset);

            return new List<Part> { part1, part2 };
        }

        private List<Part> ConvertTileResultToParts(TileResult tileResult, Drawing drawing)
        {
            var parts = new List<Part>();
            var bestFit = tileResult.BestFit;
            var candidate = bestFit.Candidate;
            var workArea = Plate.WorkArea();

            foreach (var placement in tileResult.Placements)
            {
                // Build part1 at origin.
                var part1 = new Part(drawing);
                var bbox1 = part1.Program.BoundingBox();
                part1.Offset(-bbox1.Location.X, -bbox1.Location.Y);
                part1.UpdateBounds();

                // Build part2 with rotation, positioned at offset.
                var part2 = new Part(drawing);

                if (!candidate.Part2Rotation.IsEqualTo(0))
                    part2.Rotate(candidate.Part2Rotation);

                var bbox2 = part2.Program.BoundingBox();
                part2.Offset(-bbox2.Location.X, -bbox2.Location.Y);
                part2.Location = candidate.Part2Offset;
                part2.UpdateBounds();

                // Apply optimal rotation to align pair to minimum bounding rectangle.
                if (!bestFit.OptimalRotation.IsEqualTo(0))
                {
                    var pairBounds = ((IEnumerable<IBoundable>)new IBoundable[] { part1, part2 }).GetBoundingBox();
                    var center = pairBounds.Center;
                    part1.Rotate(-bestFit.OptimalRotation, center);
                    part2.Rotate(-bestFit.OptimalRotation, center);
                }

                // Apply 90 degree rotation if the tiler chose the rotated orientation.
                if (tileResult.PairRotated)
                {
                    var pairBounds = ((IEnumerable<IBoundable>)new IBoundable[] { part1, part2 }).GetBoundingBox();
                    var center = pairBounds.Center;
                    part1.Rotate(Angle.HalfPI, center);
                    part2.Rotate(Angle.HalfPI, center);
                }

                // Normalize pair to origin.
                var finalBounds = ((IEnumerable<IBoundable>)new IBoundable[] { part1, part2 }).GetBoundingBox();
                var normalizeOffset = new Vector(-finalBounds.Left, -finalBounds.Bottom);
                part1.Offset(normalizeOffset);
                part2.Offset(normalizeOffset);

                // Offset to grid position plus work area origin.
                var plateOffset = placement.Position + workArea.Location;
                part1.Offset(plateOffset);
                part2.Offset(plateOffset);

                parts.Add(part1);
                parts.Add(part2);
            }

            return parts;
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
            if (candidate == null || candidate.Count == 0)
                return false;

            // Reject candidate if it has overlapping parts.
            if (HasOverlaps(candidate, Plate.PartSpacing))
                return false;

            if (current == null || current.Count == 0)
                return true;

            if (candidate.Count != current.Count)
                return candidate.Count > current.Count;

            var candidateBox = ((IEnumerable<IBoundable>)candidate).GetBoundingBox();
            var currentBox = ((IEnumerable<IBoundable>)current).GetBoundingBox();

            return candidateBox.Area() < currentBox.Area();
        }

        private List<double> FindHullEdgeAngles(List<Part> parts)
        {
            var points = new List<Vector>();

            foreach (var part in parts)
            {
                var entities = ConvertProgram.ToGeometry(part.Program)
                    .Where(e => e.Layer != SpecialLayers.Rapid);

                var shapes = Helper.GetShapes(entities);

                foreach (var shape in shapes)
                {
                    var polygon = shape.ToPolygonWithTolerance(0.1);

                    foreach (var vertex in polygon.Vertices)
                        points.Add(vertex + part.Location);
                }
            }

            if (points.Count < 3)
                return new List<double> { 0 };

            var hull = ConvexHull.Compute(points);
            var vertices = hull.Vertices;
            var n = hull.IsClosed() ? vertices.Count - 1 : vertices.Count;

            var angles = new List<double> { 0 };

            for (var i = 0; i < n; i++)
            {
                var next = (i + 1) % n;
                var dx = vertices[next].X - vertices[i].X;
                var dy = vertices[next].Y - vertices[i].Y;

                if (dx * dx + dy * dy < Tolerance.Epsilon)
                    continue;

                var angle = -System.Math.Atan2(dy, dx);

                if (!angles.Any(a => a.IsEqualTo(angle)))
                    angles.Add(angle);
            }

            return angles;
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

        private double FindBestRotation(NestItem item)
        {
            var entities = ConvertProgram.ToGeometry(item.Drawing.Program)
                .Where(e => e.Layer != SpecialLayers.Rapid);

            var shapes = Helper.GetShapes(entities);

            if (shapes.Count == 0)
                return 0;

            // Find the largest shape (outer profile).
            Shape largest = shapes[0];
            double largestArea = largest.Area();

            for (int i = 1; i < shapes.Count; i++)
            {
                var area = shapes[i].Area();
                if (area > largestArea)
                {
                    largest = shapes[i];
                    largestArea = area;
                }
            }

            // Convert to polygon so arcs are properly represented as line segments.
            // Shape.FindBestRotation() uses Entity cardinal points which are incorrect
            // for arcs that don't sweep through all 4 cardinal directions.
            var polygon = largest.ToPolygonWithTolerance(0.1);

            BoundingRectangleResult result;

            if (item.RotationStart.IsEqualTo(0) && item.RotationEnd.IsEqualTo(0))
                result = polygon.FindBestRotation();
            else
                result = polygon.FindBestRotation(item.RotationStart, item.RotationEnd);

            // Negate the angle to align the minimum bounding rectangle with the axes.
            return -result.Angle;
        }

        private List<Part> ConvertToParts(Bin bin, List<NestItem> items)
        {
            var parts = new List<Part>();

            foreach (var item in bin.Items)
            {
                var nestItem = items[item.Id];
                var part = ConvertToPart(item, nestItem.Drawing);
                parts.Add(part);
            }

            return parts;
        }

        private Part ConvertToPart(Item item, Drawing dwg)
        {
            var part = new Part(dwg);

            if (item.IsRotated)
                part.Rotate(Angle.HalfPI);

            var boundingBox = part.Program.BoundingBox();
            var offset = item.Location - boundingBox.Location;

            part.Offset(offset);

            return part;
        }

        private List<Item> ConvertToRectangleItems(List<NestItem> items)
        {
            var binItems = new List<Item>();

            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];
                var binItem = ConvertToRectangleItem(item, i);

                int maxQty = (int)System.Math.Floor(Plate.Area() / binItem.Area());

                int qty = item.Quantity < maxQty
                    ? item.Quantity
                    : maxQty;

                for (int j = 0; j < qty; j++)
                    binItems.Add(binItem.Clone() as Item);
            }

            return binItems;
        }

        private Item ConvertToRectangleItem(NestItem item, int id = 0)
        {
            var box = item.Drawing.Program.BoundingBox();

            box.Width += Plate.PartSpacing;
            box.Height += Plate.PartSpacing;

            

            return new Item
            {
                Id = id,
                Location = box.Location,
                Size = box.Size
            };
        }
    }
}
