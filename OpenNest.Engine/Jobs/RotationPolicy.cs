using System;

namespace OpenNest.Engine.Jobs;

public enum RotationPolicyKind
{
    Fixed,
    BoundedSweep,
    Automatic,
}

/// <summary>Immutable rotation constraints, in radians about the geometry origin.</summary>
public sealed class RotationPolicy
{
    private RotationPolicy(
        RotationPolicyKind kind,
        double start,
        double end,
        double step,
        bool allow180Equivalent
    )
    {
        if (!double.IsFinite(start) || !double.IsFinite(end) || !double.IsFinite(step))
            throw new ArgumentException("Angles must be finite.");
        Kind = kind;
        Start = start;
        End = end;
        Step = step;
        Allow180Equivalent = allow180Equivalent;
    }

    public RotationPolicyKind Kind { get; }
    public double Start { get; }
    public double End { get; }
    public double Step { get; }

    /// <summary>Whether an orientation 180° from an allowed angle is also legal.</summary>
    public bool Allow180Equivalent { get; }
    public static RotationPolicy Automatic { get; } =
        new(RotationPolicyKind.Automatic, 0, 0, 0, false);

    public static RotationPolicy Fixed(double angle, bool allow180Equivalent = false) =>
        new(RotationPolicyKind.Fixed, angle, angle, 0, allow180Equivalent);

    public static RotationPolicy BoundedSweep(
        double start,
        double end,
        double step,
        bool allow180Equivalent = false
    )
    {
        if (step <= 0 || end < start)
            throw new ArgumentException("Sweep needs a positive step and ordered bounds.");
        return new RotationPolicy(
            RotationPolicyKind.BoundedSweep,
            start,
            end,
            step,
            allow180Equivalent
        );
    }

    /// <summary>Preserves the legacy zero-step automatic sentinel; zero never means locked rotation.</summary>
    public static RotationPolicy FromLegacy(
        double stepAngle,
        double rotationStart,
        double rotationEnd,
        bool allow180Equivalent = false
    ) =>
        stepAngle == 0
            ? Automatic
            : BoundedSweep(rotationStart, rotationEnd, stepAngle, allow180Equivalent);

    /// <summary>True when a placement rotation satisfies this policy. Fixed and bounded
    /// policies compare orientations modulo full turns; an allowed 180° equivalent is included.</summary>
    public bool Allows(double rotation)
    {
        const double epsilon = 0.0000001;
        if (!double.IsFinite(rotation))
            return false;
        if (Kind == RotationPolicyKind.Automatic)
            return true;
        if (Kind == RotationPolicyKind.Fixed)
            return AnglesEqual(rotation, Start, epsilon)
                || (Allow180Equivalent && AnglesEqual(rotation, Start + System.Math.PI, epsilon));
        return SweepAllows(rotation, epsilon)
            || (Allow180Equivalent && SweepAllows(rotation - System.Math.PI, epsilon));
    }

    private bool SweepAllows(double rotation, double epsilon)
    {
        var fullTurn = System.Math.PI * 2;
        var firstTurn = System.Math.Ceiling((Start - epsilon - rotation) / fullTurn);
        var lastTurn = System.Math.Floor((End + epsilon - rotation) / fullTurn);
        for (var turns = firstTurn; turns <= lastTurn; turns++)
        {
            var equivalent = rotation + turns * fullTurn;
            if (equivalent < Start - epsilon || equivalent > End + epsilon)
                continue;
            var steps = (equivalent - Start) / Step;
            if (System.Math.Abs(steps - System.Math.Round(steps)) <= epsilon)
                return true;
        }
        return false;
    }

    private static bool AnglesEqual(double left, double right, double epsilon)
    {
        var delta = (left - right) % (System.Math.PI * 2);
        return System.Math.Abs(delta) <= epsilon
            || System.Math.Abs(System.Math.Abs(delta) - System.Math.PI * 2) <= epsilon;
    }
}
