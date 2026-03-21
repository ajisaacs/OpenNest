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
        var perpAxis = _primaryAxis == NestDirection.Horizontal
            ? NestDirection.Vertical
            : NestDirection.Horizontal;
        var sheetSpan = GetDimension(workArea, _primaryAxis);
        var strategyName = _primaryAxis == NestDirection.Horizontal ? "Row" : "Column";

        List<Part> bestParts = null;
        var bestScore = default(FillScore);

        for (var i = 0; i < bestFits.Count; i++)
        {
            _context.Token.ThrowIfCancellationRequested();

            var candidate = bestFits[i];
            var pairParts = candidate.BuildParts(drawing);

            // Try both expand (N wider pairs) and shrink (N+1 narrower pairs)
            var expandResult = ConvergeStripeAngle(
                pairParts, sheetSpan, spacing, _primaryAxis, _context.Token);
            var shrinkResult = ConvergeStripeAngleShrink(
                pairParts, sheetSpan, spacing, _primaryAxis, _context.Token);

            // Evaluate both convergence results
            foreach (var (angle, waste, count) in new[] { expandResult, shrinkResult })
            {
                if (count <= 0)
                    continue;

                var result = BuildGrid(pairParts, angle, workArea, spacing, perpAxis, drawing);
                if (result == null || result.Count == 0)
                    continue;

                var score = FillScore.Compute(result, workArea);
                Debug.WriteLine($"[StripeFiller] {strategyName} candidate {i}: angle={Angle.ToDegrees(angle):F1}°, " +
                    $"pairsAcross={count}, waste={waste:F2}, grid={result.Count} parts");

                if (bestParts == null || score > bestScore)
                {
                    bestParts = result;
                    bestScore = score;
                }
            }

            NestEngineBase.ReportProgress(_context.Progress, NestPhase.Custom,
                _context.PlateNumber, bestParts, workArea,
                $"{strategyName}: {i + 1}/{bestFits.Count} pairs, best = {bestScore.Count} parts");
        }

        return bestParts ?? new List<Part>();
    }

    private List<Part> BuildGrid(List<Part> pairParts, double angle,
        Box workArea, double spacing, NestDirection perpAxis, Drawing drawing)
    {
        var rotatedPattern = FillHelpers.BuildRotatedPattern(pairParts, angle);
        var perpDim = GetDimension(rotatedPattern.BoundingBox, perpAxis);
        var stripeBox = MakeStripeBox(workArea, perpDim, _primaryAxis);
        var stripeEngine = new FillLinear(stripeBox, spacing);
        var stripeParts = stripeEngine.Fill(rotatedPattern, _primaryAxis);

        if (stripeParts == null || stripeParts.Count == 0)
            return null;

        Debug.WriteLine($"[StripeFiller] Stripe: {stripeParts.Count} parts, " +
            $"box={stripeBox.Width:F2}x{stripeBox.Length:F2}");

        var stripePattern = new Pattern();
        stripePattern.Parts.AddRange(stripeParts);
        stripePattern.UpdateBounds();

        var gridEngine = new FillLinear(workArea, spacing);
        var gridParts = gridEngine.Fill(stripePattern, perpAxis);

        if (gridParts == null || gridParts.Count == 0)
            return null;

        Debug.WriteLine($"[StripeFiller] Grid: {gridParts.Count} parts");

        var allParts = new List<Part>(gridParts);
        var remnantParts = FillRemnant(gridParts, drawing, angle, workArea, spacing);
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
        Box workArea, double spacing)
    {
        var gridBox = gridParts.GetBoundingBox();
        var minDim = System.Math.Min(
            drawing.Program.BoundingBox().Width,
            drawing.Program.BoundingBox().Length);

        Box remnantBox;

        if (_primaryAxis == NestDirection.Horizontal)
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

        var engine = new FillLinear(remnantBox, spacing);
        var parts = engine.Fill(drawing, angle, _primaryAxis);
        return parts != null && parts.Count > 0 ? parts : null;
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

        for (var iteration = 0; iteration < MaxConvergenceIterations; iteration++)
        {
            token.ThrowIfCancellationRequested();

            var rotated = FillHelpers.BuildRotatedPattern(patternParts, currentAngle);
            var pairSpan = GetDimension(rotated.BoundingBox, axis);

            if (pairSpan + spacing <= 0)
                break;

            var n = (int)System.Math.Floor((sheetSpan + spacing) / (pairSpan + spacing));
            if (n <= 0)
                break;

            var usedSpan = n * (pairSpan + spacing) - spacing;
            var remaining = sheetSpan - usedSpan;

            if (remaining < bestWaste)
            {
                bestWaste = remaining;
                bestAngle = currentAngle;
                bestCount = n;
            }

            if (remaining <= tolerance)
                break;

            var delta = remaining / n;
            var targetSpan = pairSpan + delta;

            currentAngle = FindAngleForTargetSpan(patternParts, targetSpan, axis);
        }

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

        // Now converge from this starting point
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

            var n = (int)System.Math.Floor((sheetSpan + spacing) / (pairSpan + spacing));
            if (n <= 0)
                break;

            usedSpan = n * (pairSpan + spacing) - spacing;
            var remaining = sheetSpan - usedSpan;

            if (remaining < bestWaste)
            {
                bestWaste = remaining;
                bestAngle = currentAngle;
                bestCount = n;
            }

            if (remaining <= tolerance)
                break;

            var delta = remaining / n;
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
