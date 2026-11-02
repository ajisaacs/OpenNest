#nullable enable
using System;
using System.Collections.Generic;

namespace OpenNest.Engine.Jobs.Cutouts;

/// <summary>What cutout filling maximizes: the job's material seated in cutouts, read as insert
/// outline area, since every square inch in a hole is one the sheet no longer has to give.
/// Copy count plays no part, so one large part beats a handful of tiny ones of less area.</summary>
internal static class CutoutSeatedArea
{
    private const double Tie = 1e-6;

    /// <summary>The area a part takes off a sheet: its outline, holes included.</summary>
    internal static double Outline(JobPartGeometry geometry) => System.Math.Abs(geometry.Perimeter.Area());

    internal static double Of(IReadOnlyList<NestJobPlacement> poses, Func<string, JobPartGeometry> geometry)
    {
        var area = 0.0;
        foreach (var pose in poses)
            area += Outline(geometry(pose.PartId));
        return area;
    }

    /// <summary>True when <paramref name="candidate"/> seats more area, beyond rounding: areas
    /// summed in another order count as the same.</summary>
    internal static bool More(double candidate, double incumbent) =>
        candidate - incumbent > Tie * System.Math.Max(candidate, incumbent);

    /// <summary>True when <paramref name="candidate"/> should replace
    /// <paramref name="incumbent"/>: it seats more area, or the same area more compactly.</summary>
    internal static bool Better(IReadOnlyList<NestJobPlacement> candidate,
        IReadOnlyList<NestJobPlacement> incumbent, Func<string, JobPartGeometry> geometry)
    {
        var area = Of(candidate, geometry);
        var other = Of(incumbent, geometry);
        return More(area, other)
            || (!More(other, area) && CutoutPackerProposal.Tighter(candidate, incumbent, geometry));
    }
}
