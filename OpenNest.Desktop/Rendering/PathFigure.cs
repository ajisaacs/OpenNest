using System.Collections.Generic;
using OpenNest.Geometry;
using OpenNest.Math;

namespace OpenNest.Desktop.Rendering;

/// <summary>One drawn piece of a figure, ending at <see cref="End"/>.</summary>
public abstract record PathSegment(Vector End);

/// <summary>A straight line from the previous point to <see cref="PathSegment.End"/>.</summary>
public sealed record LineTo(Vector End) : PathSegment(End);

/// <summary>
/// A circular arc about <paramref name="Center"/>, in degrees measured from +X toward +Y (the
/// y-up drawing frame); a positive sweep turns counterclockwise. A sweep of 360 degrees is a full
/// circle. Toolkits join the previous point to the arc's start with a line, as GDI+ and Skia do.
/// </summary>
public sealed record ArcTo(Vector End, Vector Center, double Radius, double StartAngle, double SweepAngle)
    : PathSegment(End)
{
    public bool IsFullCircle => System.Math.Abs(SweepAngle) >= 360.0;
}

/// <summary>
/// One continuous run of drawing moves: a move to <see cref="Start"/> followed by
/// <see cref="Segments"/>. Toolkit-neutral; each desktop app converts figures to its own path type.
/// </summary>
public sealed class PathFigure
{
    public PathFigure(Vector start, IReadOnlyList<PathSegment> segments)
    {
        Start = start;
        Segments = segments;
    }

    public Vector Start { get; }

    public IReadOnlyList<PathSegment> Segments { get; }

    public Vector End => Segments.Count == 0 ? Start : Segments[Segments.Count - 1].End;

    /// <summary>Whether the figure ends where it starts, so a toolkit may close it.</summary>
    public bool IsClosed => Start.DistanceTo(End) <= Tolerance.Epsilon;

    public override string ToString() => $"{Start} +{Segments.Count} {(IsClosed ? "closed" : "open")}";
}
