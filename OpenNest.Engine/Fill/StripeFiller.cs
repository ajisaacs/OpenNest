using System.Collections.Generic;
using System.Linq;
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
        // Placeholder — implemented in Task 3
        return new List<Part>();
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
