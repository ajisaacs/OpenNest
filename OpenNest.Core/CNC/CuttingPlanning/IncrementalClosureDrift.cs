using System.Collections.Generic;
using OpenNest.Diagnostics;
using OpenNest.Geometry;
using OpenNest.Math;

namespace OpenNest.CNC.CuttingPlanning;

/// <summary>
/// Rounding drift at an incremental contour's seam. Each incremental move is written at the source
/// CAM's precision, so a contour's end is a sum of rounded moves and can miss its start by more than
/// <see cref="NominalContourClosure.Tolerance"/> (converted PEP perimeters of a hundred polyline
/// moves written at 1e-6 miss by 1.4e-6). An absolute program has no such sum. A contour that misses
/// by no more than its moves could accumulate at that precision, and never more than
/// <see cref="Tolerance.Epsilon"/>, ends its last cut on its start; the next motion absorbs the
/// correction, so nothing after the contour moves. Gaps inside the closure tolerance are closed too:
/// material capture and preparation sum the moves differently, so a gap at that boundary can pass
/// one and fail the other. Contours closed within the native epsilon are left alone.
/// </summary>
internal static class IncrementalClosureDrift
{
    /// <summary>Closes drifted contours of an owned program in place; returns how many were closed.</summary>
    internal static int Close(Program program)
    {
        if (program?.Codes == null || program.Mode != Mode.Incremental)
            return 0;
        var closed = 0;
        var run = new List<Motion>();
        var start = new Vector();
        var position = new Vector();
        for (var index = 0; index <= program.Codes.Count; index++)
        {
            var code = index < program.Codes.Count ? program.Codes[index] : null;
            if (code is Motion motion && IsContour(motion))
            {
                if (run.Count == 0)
                    start = position;
                run.Add(motion);
                position += motion.EndPoint;
                continue;
            }
            if (run.Count > 0 && TryClose(program, run, start, position, index))
                closed++;
            run.Clear();
            if (code is Motion other)
                position += other.EndPoint;
        }
        return closed;
    }

    // The chains material capture closes: cut, display and tab moves between rapids and other layers.
    private static bool IsContour(Motion motion) => !motion.Suppressed
        && motion is LinearMove { Layer: LayerType.Cut or LayerType.Display or LayerType.Tab or LayerType.DisplayPerimeter }
            or ArcMove { Layer: LayerType.Cut or LayerType.Display or LayerType.Tab or LayerType.DisplayPerimeter };

    private static bool TryClose(Program program, List<Motion> run, Vector start, Vector end, int next)
    {
        var gap = start.DistanceTo(end);
        // Each rounded move is off by at most half a quantum per axis.
        var drift = System.Math.Min(Tolerance.Epsilon,
            run.Count * NominalContourClosure.Tolerance * System.Math.Sqrt(0.5));
        if (run.Count < 2 || !(gap > PostVerificationGeometry.Epsilon) || !(gap <= drift))
            return false;
        var last = run[^1];
        if (last.VariableRefs is { Count: > 0 })
            return false;
        // The first motion after the contour starts where the contour used to end. A subprogram call
        // first would run from the corrected end instead, so that contour is left alone.
        Motion absorber = null;
        for (; next < program.Codes.Count && absorber == null; next++)
        {
            if (program.Codes[next] is SubProgramCall)
                return false;
            absorber = program.Codes[next] as Motion;
        }
        if (absorber != null && (absorber is ArcMove || absorber.VariableRefs is { Count: > 0 }))
            return false;

        var correction = start - end;
        var endPoint = last.EndPoint + correction;
        if (last is ArcMove arc)
        {
            // Keep the arc's center (relative to its start) equidistant from its moved end.
            var chord = endPoint.DistanceTo(new Vector());
            if (chord <= Tolerance.Epsilon)
                return false;
            var normal = new Vector(-endPoint.Y, endPoint.X) * (1 / chord);
            var middle = endPoint * 0.5;
            var offset = arc.CenterPoint - middle;
            arc.CenterPoint = middle + normal * (offset.X * normal.X + offset.Y * normal.Y);
        }
        last.EndPoint = endPoint;
        if (absorber != null)
            absorber.EndPoint -= correction;
        return true;
    }
}
