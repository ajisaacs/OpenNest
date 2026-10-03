using System;
using System.Collections.Generic;
using System.Linq;
using OpenNest.Diagnostics;
using OpenNest.Geometry;

namespace OpenNest.CNC.CuttingPlanning;

/// <summary>Owned value motions; no references to live programs, instructions or settings.</summary>
public sealed class OwnedExecution
{
    internal OwnedExecution(IEnumerable<ExecutionMotion> motions)
    {
        Motions = Array.AsReadOnly(motions.ToArray());
    }

    public IReadOnlyList<ExecutionMotion> Motions { get; }
    public Vector DeparturePoint => Motions[^1].End;
    public bool HasCuttingContour => Motions.Any(move => !move.Rapid
        && move.Layer is LayerType.Cut or LayerType.Display
        && move.Length > PostVerificationGeometry.Epsilon);

    public double RapidDistanceFrom(Vector arrival)
    {
        PostVerificationGeometry.Validate(arrival);
        var total = 0.0;
        for (var index = 0; index < Motions.Count; index++)
        {
            var move = Motions[index];
            if (move.Rapid && (index == 0 ? arrival : move.Start) is { } start)
                total += start.DistanceTo(move.End);
        }
        return total;
    }
}

/// <summary>An immutable motion value; native line/arc geometry stays private to Core.</summary>
public sealed class ExecutionMotion
{
    internal ExecutionMotion(Vector? start, Vector end, bool rapid, LayerType layer,
        PostVerificationGeometry.Curve curve)
    {
        Start = start;
        End = end;
        Rapid = rapid;
        Layer = layer;
        Curve = curve;
    }

    public Vector? Start { get; }
    public Vector End { get; }
    public bool Rapid { get; }
    public LayerType Layer { get; }
    public double Length => Curve?.Length ?? 0;
    internal PostVerificationGeometry.Curve Curve { get; }
    internal ExecutionMotion WithStart(Vector? start) => new(start, End, Rapid, Layer, Curve);
}
