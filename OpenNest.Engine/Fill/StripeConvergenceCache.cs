using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using OpenNest.Engine.BestFit;
using OpenNest.Geometry;

namespace OpenNest.Engine.Fill;

/// <summary>
/// One estimate row of a stripe convergence: the turned pair's span along the row and across
/// it, and the parts and remaining span of the row tiled across the sheet (zero parts and NaN
/// when none fit, or when the span leaves no room to step).
/// </summary>
internal readonly record struct StripeRow(double PairSpan, double PerpDim, int Count, double Remaining);

/// <summary>
/// The stripe angles <see cref="StripeFiller"/> converged in one fill pipeline, so each pair,
/// axis, variant and span converges once, and the turned bounds and estimate rows they were
/// converged from. The Row and Column strategies converge the same layouts (both lay rows along
/// both axes), every pair remnant strip along a full side of the work area repeats the same
/// span, and the expanding and shrinking variants revisit each other's angles. Each value is a
/// pure function of its key (the pair's parts rebuilt from the drawing, the angle, the span,
/// the spacing and the row cap), so a hit returns exactly what computing it again would. A
/// canceled convergence stores no result; the rows and bounds it stored are complete.
/// </summary>
internal sealed class StripeConvergenceCache
{
    internal const string SharedStateKey = "StripeConvergence";

    private readonly ConcurrentDictionary<Key, (double Angle, double Waste, int Count)> results =
        new();

    private readonly ConcurrentDictionary<Key, ConcurrentDictionary<double, Box>> bounds = new();

    private readonly ConcurrentDictionary<Key, ConcurrentDictionary<double, Lazy<StripeRow>>> rows =
        new();

    /// <summary>
    /// The cached convergence for the key, or the one <paramref name="converge"/> computes,
    /// stored unless it throws.
    /// </summary>
    public (double Angle, double Waste, int Count) GetOrAdd(
        BestFitResult candidate,
        Drawing drawing,
        NestDirection axis,
        int variant,
        double sheetSpan,
        double spacing,
        int maxParts,
        Func<(double Angle, double Waste, int Count)> converge
    )
    {
        var key = new Key(candidate, drawing, axis, variant, sheetSpan, spacing, maxParts);
        if (results.TryGetValue(key, out var cached))
            return cached;

        // Threads converging the same key at once compute identical results; the first stored wins.
        var result = converge();
        results.TryAdd(key, result);
        return result;
    }

    /// <summary>The number of convergences stored.</summary>
    internal int Count => results.Count;

    /// <summary>The pair's parts turned about their center: bounding boxes by angle.</summary>
    public ConcurrentDictionary<double, Box> Bounds(BestFitResult candidate, Drawing drawing) =>
        bounds.GetOrAdd(
            new Key(candidate, drawing, default, -1, 0, 0, 0),
            _ => new ConcurrentDictionary<double, Box>()
        );

    /// <summary>The pair's estimate rows across one span, by angle.</summary>
    public ConcurrentDictionary<double, Lazy<StripeRow>> Rows(
        BestFitResult candidate,
        Drawing drawing,
        NestDirection axis,
        double sheetSpan,
        double spacing,
        int maxParts
    ) =>
        rows.GetOrAdd(
            new Key(candidate, drawing, axis, -1, sheetSpan, spacing, maxParts),
            _ => new ConcurrentDictionary<double, Lazy<StripeRow>>()
        );

    /// <summary>Gets or creates the pipeline's cache in a fill context's shared state.</summary>
    public static StripeConvergenceCache GetOrCreate(Dictionary<string, object> sharedState)
    {
        if (sharedState.TryGetValue(SharedStateKey, out var existing))
            return (StripeConvergenceCache)existing;

        var cache = new StripeConvergenceCache();
        sharedState[SharedStateKey] = cache;
        return cache;
    }

    private readonly struct Key : IEquatable<Key>
    {
        private readonly BestFitResult candidate;
        private readonly Drawing drawing;
        private readonly NestDirection axis;
        private readonly int variant;
        private readonly long sheetSpanBits;
        private readonly long spacingBits;
        private readonly int maxParts;

        public Key(
            BestFitResult candidate,
            Drawing drawing,
            NestDirection axis,
            int variant,
            double sheetSpan,
            double spacing,
            int maxParts
        )
        {
            this.candidate = candidate;
            this.drawing = drawing;
            this.axis = axis;
            this.variant = variant;
            sheetSpanBits = BitConverter.DoubleToInt64Bits(sheetSpan);
            spacingBits = BitConverter.DoubleToInt64Bits(spacing);
            this.maxParts = maxParts;
        }

        public bool Equals(Key other) =>
            ReferenceEquals(candidate, other.candidate)
            && ReferenceEquals(drawing, other.drawing)
            && axis == other.axis
            && variant == other.variant
            && sheetSpanBits == other.sheetSpanBits
            && spacingBits == other.spacingBits
            && maxParts == other.maxParts;

        public override bool Equals(object obj) => obj is Key other && Equals(other);

        public override int GetHashCode() =>
            HashCode.Combine(
                System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(candidate),
                System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(drawing),
                axis,
                variant,
                sheetSpanBits,
                spacingBits,
                maxParts
            );
    }
}
