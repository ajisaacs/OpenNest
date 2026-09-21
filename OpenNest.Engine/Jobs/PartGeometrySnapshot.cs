using System;
using System.Collections.Generic;
using System.Linq;
using OpenNest.CNC;

namespace OpenNest.Engine.Jobs;

/// <summary>Exact immutable CNC motion values. Rapid moves retain contour/hole boundaries; arcs are not tessellated.</summary>
public sealed record PartGeometryMotion(
    CodeType Type,
    double X,
    double Y,
    double CenterX,
    double CenterY,
    RotationType Rotation,
    LayerType Layer,
    bool Suppressed
);

/// <summary>
/// Owned geometry only: no Drawing, quantity, events, or mutable CNC references are retained.
/// This initial boundary supports flat rapid/linear/arc programs and rejects other instructions explicitly.
/// Coordinates and mode are preserved without normalization, rounding, or polygon approximation.
/// </summary>
public sealed class PartGeometrySnapshot
{
    private PartGeometrySnapshot(Mode mode, IEnumerable<PartGeometryMotion> motions)
    {
        Mode = mode;
        Motions = NestJob.Own(motions);
    }

    public Mode Mode { get; }
    public IReadOnlyList<PartGeometryMotion> Motions { get; }

    /// <summary>Copies supported motion geometry immediately; later program edits cannot affect this snapshot.</summary>
    public static PartGeometrySnapshot FromProgram(Program program)
    {
        ArgumentNullException.ThrowIfNull(program);
        var motions = program.Codes.Select(code =>
            code switch
            {
                ArcMove arc => new PartGeometryMotion(
                    arc.Type,
                    arc.EndPoint.X,
                    arc.EndPoint.Y,
                    arc.CenterPoint.X,
                    arc.CenterPoint.Y,
                    arc.Rotation,
                    arc.Layer,
                    arc.Suppressed
                ),
                LinearMove line => new PartGeometryMotion(
                    line.Type,
                    line.EndPoint.X,
                    line.EndPoint.Y,
                    0,
                    0,
                    default,
                    line.Layer,
                    line.Suppressed
                ),
                RapidMove rapid => new PartGeometryMotion(
                    rapid.Type,
                    rapid.EndPoint.X,
                    rapid.EndPoint.Y,
                    0,
                    0,
                    default,
                    default,
                    rapid.Suppressed
                ),
                _ => throw new NotSupportedException(
                    "Geometry snapshots currently support only flat rapid/linear/arc programs."
                ),
            }
        );
        return new PartGeometrySnapshot(program.Mode, motions);
    }
}
