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

    private int? contentHash;

    public Mode Mode { get; }
    public IReadOnlyList<PartGeometryMotion> Motions { get; }

    /// <summary>
    /// Equality by exact content: the same mode and the same motions in the same order, every
    /// value bit for bit (so 0.0 and -0.0 differ), whatever the instances. Snapshots themselves
    /// still compare by reference, as job identity does; this comparer is for caches whose
    /// results depend on the geometry alone, such as best-fit candidates.
    /// </summary>
    public static IEqualityComparer<PartGeometrySnapshot> ContentComparer { get; } = new ContentEquality();

    private int ContentHash
    {
        get
        {
            if (contentHash is int cached)
                return cached;
            var hash = new HashCode();
            hash.Add(Mode);
            foreach (var motion in Motions)
            {
                hash.Add(motion.Type);
                hash.Add(Bits(motion.X));
                hash.Add(Bits(motion.Y));
                hash.Add(Bits(motion.CenterX));
                hash.Add(Bits(motion.CenterY));
                hash.Add(motion.Rotation);
                hash.Add(motion.Layer);
                hash.Add(motion.Suppressed);
            }
            var value = hash.ToHashCode();
            contentHash = value;
            return value;
        }
    }

    private static long Bits(double value) => BitConverter.DoubleToInt64Bits(value);

    private static bool SameMotion(PartGeometryMotion a, PartGeometryMotion b) =>
        a.Type == b.Type
        && Bits(a.X) == Bits(b.X)
        && Bits(a.Y) == Bits(b.Y)
        && Bits(a.CenterX) == Bits(b.CenterX)
        && Bits(a.CenterY) == Bits(b.CenterY)
        && a.Rotation == b.Rotation
        && a.Layer == b.Layer
        && a.Suppressed == b.Suppressed;

    private sealed class ContentEquality : IEqualityComparer<PartGeometrySnapshot>
    {
        public bool Equals(PartGeometrySnapshot x, PartGeometrySnapshot y)
        {
            if (ReferenceEquals(x, y))
                return true;
            if (x is null || y is null || x.Mode != y.Mode || x.Motions.Count != y.Motions.Count)
                return false;
            for (var i = 0; i < x.Motions.Count; i++)
                if (!SameMotion(x.Motions[i], y.Motions[i]))
                    return false;
            return true;
        }

        public int GetHashCode(PartGeometrySnapshot snapshot) => snapshot.ContentHash;
    }

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
