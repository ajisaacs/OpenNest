using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using OpenNest.Engine.BestFit;
using OpenNest.Engine.Strategies;
using OpenNest.Geometry;
using OpenNest.Math;
using System.Diagnostics;

namespace OpenNest.Engine.Fill;

public class StripeFiller
{
    private const int MaxPairCandidates = 5;
    private const int MaxConvergenceIterations = 20;
    private const int AngleSamples = 36;

    private readonly FillContext _context;
    private readonly NestDirection _primaryAxis;

    /// <summary>
    /// When true, only complete stripes are placed — no partial rows/columns.
    /// </summary>
    public bool CompleteStripesOnly { get; set; }

    /// <summary>
    /// Factory to create the engine used for filling the remnant strip.
    /// Defaults to NestEngineRegistry.Create (uses the user's selected engine).
    /// </summary>
    public Func<Plate, NestEngineBase> CreateRemnantEngine { get; set; }
        = NestEngineRegistry.Create;

    public StripeFiller(FillContext context, NestDirection primaryAxis)
    {
        _context = context;
        _primaryAxis = primaryAxis;
    }

    public List<Part> Fill()
    {
        var bestFits = GetPairCandidates();
        if (bestFits.Count == 0)
            return new List<Part>();

        var workArea = _context.WorkArea;
        var spacing = _context.Plate.PartSpacing;
        var drawing = _context.Item.Drawing;
        var strategyName = _primaryAxis == NestDirection.Horizontal ? "Row" : "Column";

        List<Part> bestParts = null;
        var bestScore = default(FillScore);

        for (var i = 0; i < bestFits.Count; i++)
        {
            _context.Token.ThrowIfCancellationRequested();

            var candidate = bestFits[i];
            var pairParts = candidate.BuildParts(drawing);

            // Try both directions for each candidate to find the shortest
            // perpendicular dimension (more complete stripes → larger remnant)
            foreach (var axis in new[] { NestDirection.Horizontal, NestDirection.Vertical })
            {
                var perpAxis = axis == NestDirection.Horizontal
                    ? NestDirection.Vertical : NestDirection.Horizontal;
                var sheetSpan = GetDimension(workArea, axis);
                var dirLabel = axis == NestDirection.Horizontal ? "Row" : "Col";

                var expandResult = ConvergeStripeAngle(
                    pairParts, sheetSpan, spacing, axis, _context.Token);
                var shrinkResult = ConvergeStripeAngleShrink(
                    pairParts, sheetSpan, spacing, axis, _context.Token);

                foreach (var (angle, waste, count) in new[] { expandResult, shrinkResult })
                {
                    if (count <= 0)
                        continue;

                    var result = BuildGrid(pairParts, angle, workArea, spacing, axis, perpAxis, drawing);

                    if (result == null || result.Count == 0)
                        continue;

                    var score = FillScore.Compute(result, workArea);
                    Debug.WriteLine($"[StripeFiller] {strategyName} candidate {i} {dirLabel}: " +
                        $"angle={Angle.ToDegrees(angle):F1}°, N={count}, waste={waste:F2}, " +
                        $"grid={result.Count} parts");

                    if (bestParts == null || score > bestScore)
                    {
                        bestParts = result;
                        bestScore = score;
                    }
                }
            }

            NestEngineBase.ReportProgress(_context.Progress, NestPhase.Custom,
                _context.PlateNumber, bestParts, workArea,
                $"{strategyName}: {i + 1}/{bestFits.Count} pairs, best = {bestScore.Count} parts");
        }

        return bestParts ?? new List<Part>();
    }

    private List<Part> BuildGrid(List<Part> pairParts, double angle,
        Box workArea, double spacing, NestDirection primaryAxis,
        NestDirection perpAxis, Drawing drawing)
    {
        var rotatedPattern = FillHelpers.BuildRotatedPattern(pairParts, angle);
        var perpDim = GetDimension(rotatedPattern.BoundingBox, perpAxis);
        var stripeBox = MakeStripeBox(workArea, perpDim, primaryAxis);
        var stripeEngine = new FillLinear(stripeBox, spacing);
        var stripeParts = stripeEngine.Fill(rotatedPattern, primaryAxis);

        if (stripeParts == null || stripeParts.Count == 0)
            return null;

        var partsPerStripe = stripeParts.Count;

        Debug.WriteLine($"[StripeFiller] Stripe: {partsPerStripe} parts, " +
            $"box={stripeBox.Width:F2}x{stripeBox.Length:F2}");

        var stripePattern = new Pattern();
        stripePattern.Parts.AddRange(stripeParts);
        stripePattern.UpdateBounds();

        var gridEngine = new FillLinear(workArea, spacing);
        var gridParts = gridEngine.Fill(stripePattern, perpAxis);

        if (gridParts == null || gridParts.Count == 0)
            return null;

        if (CompleteStripesOnly)
        {
            // Keep only complete stripes — discard partial copies
            var completeCount = gridParts.Count / partsPerStripe * partsPerStripe;
            if (completeCount < gridParts.Count)
            {
                Debug.WriteLine($"[StripeFiller] CompleteOnly: {gridParts.Count} → {completeCount} " +
                    $"(dropped {gridParts.Count - completeCount} partial)");
                gridParts = gridParts.GetRange(0, completeCount);
            }
        }

        Debug.WriteLine($"[StripeFiller] Grid: {gridParts.Count} parts");

        if (gridParts.Count == 0)
            return null;

        var allParts = new List<Part>(gridParts);

        var remnantParts = FillRemnant(gridParts, drawing, angle, primaryAxis, workArea, spacing);
        if (remnantParts != null)
        {
            Debug.WriteLine($"[StripeFiller] Remnant: {remnantParts.Count} parts");
            allParts.AddRange(remnantParts);
        }

        return allParts;
    }

    private List<BestFitResult> GetPairCandidates()
    {
        List<BestFitResult> bestFits;

        if (_context.SharedState.TryGetValue("BestFits", out var cached))
            bestFits = (List<BestFitResult>)cached;
        else
            bestFits = BestFitCache.GetOrCompute(
                _context.Item.Drawing,
                _context.Plate.Size.Length,
                _context.Plate.Size.Width,
                _context.Plate.PartSpacing);

        return bestFits
            .Where(r => r.Keep)
            .Take(MaxPairCandidates)
            .ToList();
    }

    private static Box MakeStripeBox(Box workArea, double perpDim, NestDirection primaryAxis)
    {
        return primaryAxis == NestDirection.Horizontal
            ? new Box(workArea.X, workArea.Y, workArea.Width, perpDim)
            : new Box(workArea.X, workArea.Y, perpDim, workArea.Length);
    }

    private List<Part> FillRemnant(
        List<Part> gridParts, Drawing drawing, double angle,
        NestDirection primaryAxis, Box workArea, double spacing)
    {
        var gridBox = gridParts.GetBoundingBox();
        var minDim = System.Math.Min(
            drawing.Program.BoundingBox().Width,
            drawing.Program.BoundingBox().Length);

        Box remnantBox;

        if (primaryAxis == NestDirection.Horizontal)
        {
            var remnantY = gridBox.Top + spacing;
            var remnantLength = workArea.Top - remnantY;
            if (remnantLength < minDim)
                return null;
            remnantBox = new Box(workArea.X, remnantY, workArea.Width, remnantLength);
        }
        else
        {
            var remnantX = gridBox.Right + spacing;
            var remnantWidth = workArea.Right - remnantX;
            if (remnantWidth < minDim)
                return null;
            remnantBox = new Box(remnantX, workArea.Y, remnantWidth, workArea.Length);
        }

        Debug.WriteLine($"[StripeFiller] Remnant box: {remnantBox.Width:F2}x{remnantBox.Length:F2}");

        // Check cache first
        var cached = FillResultCache.Get(drawing, remnantBox, spacing);
        if (cached != null)
        {
            Debug.WriteLine($"[StripeFiller] Remnant CACHE HIT: {cached.Count} parts");
            return cached;
        }

        // Exclude Row/Column from remnant fill to prevent recursion
        FillStrategyRegistry.SetEnabled("Pairs", "RectBestFit", "Extents", "Linear");
        try
        {
            var engine = CreateRemnantEngine(_context.Plate);
            var item = new NestItem { Drawing = drawing };
            var parts = engine.Fill(item, remnantBox, _context.Progress, _context.Token);

            Debug.WriteLine($"[StripeFiller] Remnant engine ({engine.Name}): {parts?.Count ?? 0} parts, " +
                $"winner={engine.WinnerPhase}");

            if (parts != null && parts.Count > 0)
            {
                FillResultCache.Store(drawing, remnantBox, spacing, parts);
                return parts;
            }

            return null;
        }
        finally
        {
            FillStrategyRegistry.SetEnabled(null);
        }
    }

    public static double FindAngleForTargetSpan(
        List<Part> patternParts, double targetSpan, NestDirection axis)
    {
        var bestAngle = 0.0;
        var bestDiff = double.MaxValue;
        var samples = new (double angle, double span)[AngleSamples + 1];

        for (var i = 0; i <= AngleSamples; i++)
        {
            var angle = i * Angle.HalfPI / AngleSamples;
            var span = GetRotatedSpan(patternParts, angle, axis);
            samples[i] = (angle, span);

            var diff = System.Math.Abs(span - targetSpan);
            if (diff < bestDiff)
            {
                bestDiff = diff;
                bestAngle = angle;
            }
        }

        if (bestDiff < Tolerance.Epsilon)
            return bestAngle;

        for (var i = 0; i < samples.Length - 1; i++)
        {
            var (a1, s1) = samples[i];
            var (a2, s2) = samples[i + 1];

            if ((s1 <= targetSpan && targetSpan <= s2) ||
                (s2 <= targetSpan && targetSpan <= s1))
            {
                var result = BisectForTarget(patternParts, a1, a2, targetSpan, axis);
                var resultSpan = GetRotatedSpan(patternParts, result, axis);
                var resultDiff = System.Math.Abs(resultSpan - targetSpan);

                if (resultDiff < bestDiff)
                {
                    bestDiff = resultDiff;
                    bestAngle = result;
                }
            }
        }

        return bestAngle;
    }

    /// <summary>
    /// Returns the rotation angle that orients the pair with its short side
    /// along the given axis. Returns 0 if already oriented, PI/2 if rotated.
    /// </summary>
    private static double OrientShortSideAlong(List<Part> patternParts, NestDirection axis)
    {
        var box = FillHelpers.BuildRotatedPattern(patternParts, 0).BoundingBox;
        var span0 = GetDimension(box, axis);
        var perpSpan0 = axis == NestDirection.Horizontal ? box.Length : box.Width;

        // Short side already along axis
        if (span0 <= perpSpan0)
            return 0;

        // Rotate 90° to put short side along axis
        return Angle.HalfPI;
    }

    /// <summary>
    /// Iteratively finds the rotation angle where N copies of the pattern
    /// span the given dimension with minimal waste by expanding pair width.
    /// Returns (angle, waste, pairCount).
    /// </summary>
    public static (double Angle, double Waste, int Count) ConvergeStripeAngle(
        List<Part> patternParts, double sheetSpan, double spacing,
        NestDirection axis, CancellationToken token = default)
    {
        var currentAngle = OrientShortSideAlong(patternParts, axis);
        var bestWaste = double.MaxValue;
        var bestAngle = currentAngle;
        var bestCount = 0;
        var tolerance = sheetSpan * 0.001;

        Debug.WriteLine($"[Converge] Start: orient={Angle.ToDegrees(currentAngle):F1}°, sheetSpan={sheetSpan:F2}, spacing={spacing}");

        for (var iteration = 0; iteration < MaxConvergenceIterations; iteration++)
        {
            token.ThrowIfCancellationRequested();

            var rotated = FillHelpers.BuildRotatedPattern(patternParts, currentAngle);
            var pairSpan = GetDimension(rotated.BoundingBox, axis);
            var perpDim = axis == NestDirection.Horizontal
                ? rotated.BoundingBox.Length : rotated.BoundingBox.Width;

            if (pairSpan + spacing <= 0)
                break;

            // Use FillLinear to get the ACTUAL part count with geometry-aware spacing
            var stripeBox = axis == NestDirection.Horizontal
                ? new Box(0, 0, sheetSpan, perpDim)
                : new Box(0, 0, perpDim, sheetSpan);
            var engine = new FillLinear(stripeBox, spacing);
            var filled = engine.Fill(rotated, axis);
            var n = filled?.Count ?? 0;

            if (n <= 0)
                break;

            // Measure actual waste from the placed parts
            var filledBox = ((IEnumerable<IBoundable>)filled).GetBoundingBox();
            var usedSpan = GetDimension(filledBox, axis);
            var remaining = sheetSpan - usedSpan;

            Debug.WriteLine($"[Converge] iter={iteration}: angle={Angle.ToDegrees(currentAngle):F2}°, " +
                $"pairSpan={pairSpan:F4}, perpDim={perpDim:F4}, N={n}, waste={remaining:F3}");

            if (remaining < bestWaste)
            {
                bestWaste = remaining;
                bestAngle = currentAngle;
                bestCount = n;
            }

            if (remaining <= tolerance)
                break;

            // Estimate pairs from bounding box to compute delta
            var bboxN = (int)System.Math.Floor((sheetSpan + spacing) / (pairSpan + spacing));
            if (bboxN <= 0) bboxN = 1;
            var delta = remaining / bboxN;
            var targetSpan = pairSpan + delta;

            Debug.WriteLine($"[Converge]   delta={delta:F4}, targetSpan={targetSpan:F4}");

            var prevAngle = currentAngle;
            currentAngle = FindAngleForTargetSpan(patternParts, targetSpan, axis);

            Debug.WriteLine($"[Converge]   newAngle={Angle.ToDegrees(currentAngle):F2}° (was {Angle.ToDegrees(prevAngle):F2}°)");

            if (System.Math.Abs(currentAngle - prevAngle) < Tolerance.Epsilon)
            {
                Debug.WriteLine("[Converge]   STUCK — angle unchanged, breaking");
                break;
            }
        }

        Debug.WriteLine($"[Converge] Result: angle={Angle.ToDegrees(bestAngle):F2}°, N={bestCount}, waste={bestWaste:F3}");
        return (bestAngle, bestWaste, bestCount);
    }

    /// <summary>
    /// Tries fitting N+1 narrower pairs by shrinking the pair width.
    /// Complements ConvergeStripeAngle which only expands.
    /// </summary>
    public static (double Angle, double Waste, int Count) ConvergeStripeAngleShrink(
        List<Part> patternParts, double sheetSpan, double spacing,
        NestDirection axis, CancellationToken token = default)
    {
        // Orient short side along axis before computing N
        var baseAngle = OrientShortSideAlong(patternParts, axis);
        var naturalPattern = FillHelpers.BuildRotatedPattern(patternParts, baseAngle);
        var naturalSpan = GetDimension(naturalPattern.BoundingBox, axis);

        if (naturalSpan + spacing <= 0)
            return (0, double.MaxValue, 0);

        var naturalN = (int)System.Math.Floor((sheetSpan + spacing) / (naturalSpan + spacing));
        var targetN = naturalN + 1;

        // Target span for N+1 pairs
        var targetSpan = (sheetSpan + spacing) / targetN - spacing;

        if (targetSpan <= 0)
            return (0, double.MaxValue, 0);

        // Find the angle that shrinks the pair to targetSpan
        var angle = FindAngleForTargetSpan(patternParts, targetSpan, axis);

        // Verify the actual span at this angle
        var rotated = FillHelpers.BuildRotatedPattern(patternParts, angle);
        var actualSpan = GetDimension(rotated.BoundingBox, axis);
        var actualN = (int)System.Math.Floor((sheetSpan + spacing) / (actualSpan + spacing));

        if (actualN <= 0)
            return (0, double.MaxValue, 0);

        var usedSpan = actualN * (actualSpan + spacing) - spacing;
        var waste = sheetSpan - usedSpan;

        // Now converge from this starting point using geometry-aware fill
        var bestWaste = waste;
        var bestAngle = angle;
        var bestCount = actualN;
        var tolerance = sheetSpan * 0.001;
        var currentAngle = angle;

        for (var iteration = 0; iteration < MaxConvergenceIterations; iteration++)
        {
            token.ThrowIfCancellationRequested();

            if (bestWaste <= tolerance)
                break;

            rotated = FillHelpers.BuildRotatedPattern(patternParts, currentAngle);
            var pairSpan = GetDimension(rotated.BoundingBox, axis);
            var perpDim = axis == NestDirection.Horizontal
                ? rotated.BoundingBox.Length : rotated.BoundingBox.Width;

            var stripeBox = axis == NestDirection.Horizontal
                ? new Box(0, 0, sheetSpan, perpDim)
                : new Box(0, 0, perpDim, sheetSpan);
            var engine = new FillLinear(stripeBox, spacing);
            var filled = engine.Fill(rotated, axis);
            var n = filled?.Count ?? 0;

            if (n <= 0)
                break;

            var filledBox = ((IEnumerable<IBoundable>)filled).GetBoundingBox();
            var remaining = sheetSpan - GetDimension(filledBox, axis);

            if (remaining < bestWaste)
            {
                bestWaste = remaining;
                bestAngle = currentAngle;
                bestCount = n;
            }

            if (remaining <= tolerance)
                break;

            var bboxN = (int)System.Math.Floor((sheetSpan + spacing) / (pairSpan + spacing));
            if (bboxN <= 0) bboxN = 1;
            var delta = remaining / bboxN;
            var newTarget = pairSpan + delta;

            currentAngle = FindAngleForTargetSpan(patternParts, newTarget, axis);
        }

        return (bestAngle, bestWaste, bestCount);
    }

    private static double BisectForTarget(
        List<Part> patternParts, double lo, double hi,
        double targetSpan, NestDirection axis)
    {
        var bestAngle = lo;
        var bestDiff = double.MaxValue;

        for (var i = 0; i < 30; i++)
        {
            var mid = (lo + hi) / 2;
            var span = GetRotatedSpan(patternParts, mid, axis);
            var diff = System.Math.Abs(span - targetSpan);

            if (diff < bestDiff)
            {
                bestDiff = diff;
                bestAngle = mid;
            }

            if (diff < Tolerance.Epsilon)
                break;

            var loSpan = GetRotatedSpan(patternParts, lo, axis);
            if ((loSpan < targetSpan && span < targetSpan) ||
                (loSpan > targetSpan && span > targetSpan))
                lo = mid;
            else
                hi = mid;
        }

        return bestAngle;
    }

    private static double GetRotatedSpan(
        List<Part> patternParts, double angle, NestDirection axis)
    {
        var rotated = FillHelpers.BuildRotatedPattern(patternParts, angle);
        return axis == NestDirection.Horizontal
            ? rotated.BoundingBox.Width
            : rotated.BoundingBox.Length;
    }

    private static double GetDimension(Box box, NestDirection axis)
    {
        return axis == NestDirection.Horizontal ? box.Width : box.Length;
    }
}
