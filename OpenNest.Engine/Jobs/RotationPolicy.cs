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
    private RotationPolicy(RotationPolicyKind kind, double start, double end, double step)
    {
        if (!double.IsFinite(start) || !double.IsFinite(end) || !double.IsFinite(step))
            throw new ArgumentException("Angles must be finite.");
        Kind = kind;
        Start = start;
        End = end;
        Step = step;
    }

    public RotationPolicyKind Kind { get; }
    public double Start { get; }
    public double End { get; }
    public double Step { get; }
    public static RotationPolicy Automatic { get; } = new(RotationPolicyKind.Automatic, 0, 0, 0);

    public static RotationPolicy Fixed(double angle) =>
        new(RotationPolicyKind.Fixed, angle, angle, 0);

    public static RotationPolicy BoundedSweep(double start, double end, double step)
    {
        if (step <= 0 || end < start)
            throw new ArgumentException("Sweep needs a positive step and ordered bounds.");
        return new RotationPolicy(RotationPolicyKind.BoundedSweep, start, end, step);
    }

    /// <summary>Preserves the legacy zero-step automatic sentinel; zero never means locked rotation.</summary>
    public static RotationPolicy FromLegacy(
        double stepAngle,
        double rotationStart,
        double rotationEnd
    ) => stepAngle == 0 ? Automatic : BoundedSweep(rotationStart, rotationEnd, stepAngle);

    /// <summary>True when a placement rotation (radians) satisfies this policy: anything for
    /// Automatic, the fixed angle modulo a full turn, or an exact step inside a bounded sweep.</summary>
    public bool Allows(double rotation)
    {
        const double epsilon = 0.0000001;
        if (!double.IsFinite(rotation))
            return false;
        if (Kind == RotationPolicyKind.Automatic)
            return true;
        if (Kind == RotationPolicyKind.Fixed)
        {
            var delta = (rotation - Start) % (System.Math.PI * 2);
            return System.Math.Abs(delta) <= epsilon
                || System.Math.Abs(System.Math.Abs(delta) - System.Math.PI * 2) <= epsilon;
        }
        if (rotation < Start - epsilon || rotation > End + epsilon)
            return false;
        var steps = (rotation - Start) / Step;
        return System.Math.Abs(steps - System.Math.Round(steps)) <= epsilon;
    }
}
