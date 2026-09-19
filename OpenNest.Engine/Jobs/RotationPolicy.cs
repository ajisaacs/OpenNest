using System;

namespace OpenNest;

public enum RotationPolicyKind { Fixed, BoundedSweep, Automatic }

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
    public static RotationPolicy Fixed(double angle) => new(RotationPolicyKind.Fixed, angle, angle, 0);
    public static RotationPolicy BoundedSweep(double start, double end, double step)
    {
        if (step <= 0 || end < start) throw new ArgumentException("Sweep needs a positive step and ordered bounds.");
        return new RotationPolicy(RotationPolicyKind.BoundedSweep, start, end, step);
    }

    /// <summary>Preserves the legacy zero-step automatic sentinel; zero never means locked rotation.</summary>
    public static RotationPolicy FromLegacy(double stepAngle, double rotationStart, double rotationEnd) =>
        stepAngle == 0 ? Automatic : BoundedSweep(rotationStart, rotationEnd, stepAngle);
}
