using System;
using System.Collections.Generic;
using OpenNest.Geometry;
using OpenNest.Math;

namespace OpenNest.CNC.CuttingPlanning;

/// <summary>
/// A seam overcut baked into a clean contour: the last cut runs on past the contour's own start
/// along its first cut, then a straight jog returns to the start (converted nests carry a source
/// CAM's 0.03 overcut this way). The retraced tail encloses no area, so trimming it leaves the
/// material unchanged; left in, the contour touches itself at its seam and nothing can certify it.
/// Only that exact shape is recognized. Any other self-contact is still refused by its consumer.
/// </summary>
internal static class SeamOvercut
{
    /// <summary>
    /// The same program when it has no seam overcut, otherwise an owned copy without them.
    /// The source program is never edited.
    /// </summary>
    internal static Program Trimmed(Program program)
    {
        var edits = Find(program);
        if (edits.Count == 0)
            return program;
        var copy = (Program)program.Clone();
        Apply(copy, edits);
        return copy;
    }

    /// <summary>Trims seam overcuts from an owned program in place; returns how many were removed.</summary>
    internal static int Trim(Program program)
    {
        var edits = Find(program);
        Apply(program, edits);
        return edits.Count;
    }

    private static void Apply(Program program, List<Edit> edits)
    {
        // Last first, so removing a return jog does not shift an earlier edit's indices.
        for (var i = edits.Count - 1; i >= 0; i--)
        {
            var edit = edits[i];
            var last = (Motion)program.Codes[edit.LastCut];
            last.EndPoint = edit.EndPoint;
            if (last is ArcMove arc)
                arc.CenterPoint = edit.CenterPoint;
            program.Codes.RemoveAt(edit.ReturnJog);
        }
    }

    private static List<Edit> Find(Program program)
    {
        var edits = new List<Edit>();
        if (program?.Codes == null)
            return edits;
        var incremental = program.Mode == Mode.Incremental;
        var run = new List<Segment>();
        // Null while the position is unknown: after a subprogram call in an absolute program.
        Vector? position = new Vector();
        for (var index = 0; index < program.Codes.Count; index++)
        {
            var code = program.Codes[index];
            if (code is not Motion motion)
            {
                Finish();
                if (code is SubProgramCall && !incremental)
                    position = null;
                continue;
            }
            // Incremental coordinates only need a consistent origin within one contour.
            var reference = incremental ? position ?? new Vector() : new Vector();
            var end = reference + motion.EndPoint;
            var cut = !motion.Suppressed && motion is LinearMove { Layer: LayerType.Cut or LayerType.Display or LayerType.DisplayPerimeter }
                or ArcMove { Layer: LayerType.Cut or LayerType.Display or LayerType.DisplayPerimeter };
            if (!cut || position == null)
            {
                Finish();
                position = end;
                continue;
            }
            run.Add(new(index, motion, position.Value, end,
                motion is ArcMove arc ? reference + arc.CenterPoint : null, reference));
            position = end;
        }
        Finish();
        return edits;

        void Finish()
        {
            if (TryTrim(run, out var edit))
                edits.Add(edit);
            run.Clear();
        }
    }

    private static bool TryTrim(List<Segment> run, out Edit edit)
    {
        edit = default;
        if (run.Count < 3)
            return false;
        var first = run[0];
        var last = run[^2];
        var jog = run[^1];
        var seam = first.Start;
        var overcut = last.End;
        if (jog.Motion is not LinearMove || !NominalContourClosure.IsClosed(seam, jog.End)
            || HasVariables(last.Motion) || HasVariables(jog.Motion)
            || !IsFinite(seam) || !IsFinite(overcut) || !IsFinite(last.Start)
            || overcut.DistanceTo(seam) <= Tolerance.Epsilon || last.Start.DistanceTo(seam) <= Tolerance.Epsilon)
            return false;

        if (first.Center == null && last.Center == null)
        {
            var length = first.Start.DistanceTo(first.End);
            if (length <= Tolerance.Epsilon)
                return false;
            var direction = (first.End - first.Start) * (1 / length);
            // The last cut lies on the first cut's line, passes the seam and stops inside the first cut.
            var along = Dot(overcut - seam, direction);
            var before = Dot(seam - last.Start, direction);
            if (System.Math.Abs(Cross(overcut - seam, direction)) > Tolerance.Epsilon
                || System.Math.Abs(Cross(last.Start - seam, direction)) > Tolerance.Epsilon
                || along <= Tolerance.Epsilon || along >= length - Tolerance.Epsilon || before <= Tolerance.Epsilon)
                return false;
            edit = new(last.Index, jog.Index, seam - last.Reference, new Vector());
            return true;
        }

        if (first.Center is not { } firstCenter || last.Center is not { } lastCenter
            || !IsFinite(firstCenter) || !IsFinite(lastCenter)
            || ((ArcMove)first.Motion).Rotation != ((ArcMove)last.Motion).Rotation)
            return false;
        var radius = seam.DistanceTo(firstCenter);
        if (radius <= Tolerance.Epsilon || firstCenter.DistanceTo(lastCenter) > Tolerance.Epsilon
            || System.Math.Abs(overcut.DistanceTo(lastCenter) - radius) > Tolerance.Epsilon
            || System.Math.Abs(overcut.DistanceTo(firstCenter) - radius) > Tolerance.Epsilon
            || System.Math.Abs(seam.DistanceTo(lastCenter) - radius) > Tolerance.Epsilon)
            return false;
        var clockwise = ((ArcMove)first.Motion).Rotation == RotationType.CW;
        var margin = Tolerance.Epsilon / radius;
        // The overcut end lies inside the first arc's span, and the seam inside the last arc's.
        var firstSweep = first.Start.DistanceTo(first.End) <= Tolerance.Epsilon
            ? 2 * System.Math.PI : Travel(firstCenter, seam, first.End, clockwise);
        var passed = Travel(firstCenter, seam, overcut, clockwise);
        var lastSweep = Travel(lastCenter, last.Start, overcut, clockwise);
        var toSeam = Travel(lastCenter, last.Start, seam, clockwise);
        if (passed <= margin || passed >= System.Math.PI || passed >= firstSweep - margin
            || toSeam <= margin || toSeam >= lastSweep - margin
            || System.Math.Abs(lastSweep - toSeam - passed) > margin)
            return false;

        // Keep the shortened arc's center equidistant from both of its ends.
        var chord = seam - last.Start;
        var normal = new Vector(-chord.Y, chord.X) * (1 / last.Start.DistanceTo(seam));
        var middle = (last.Start + seam) * 0.5;
        var center = middle + normal * Dot(lastCenter - middle, normal);
        edit = new(last.Index, jog.Index, seam - last.Reference, center - last.Reference);
        return true;
    }

    /// <summary>The angle travelled around <paramref name="center"/> from one point to another, in [0, 2pi).</summary>
    private static double Travel(Vector center, Vector from, Vector to, bool clockwise)
    {
        var start = System.Math.Atan2(from.Y - center.Y, from.X - center.X);
        var end = System.Math.Atan2(to.Y - center.Y, to.X - center.X);
        var travel = (clockwise ? start - end : end - start) % (2 * System.Math.PI);
        return travel < 0 ? travel + 2 * System.Math.PI : travel;
    }

    private static bool HasVariables(Motion motion) => motion.VariableRefs is { Count: > 0 };
    private static bool IsFinite(Vector point) => double.IsFinite(point.X) && double.IsFinite(point.Y);
    private static double Dot(Vector a, Vector b) => a.X * b.X + a.Y * b.Y;
    private static double Cross(Vector a, Vector b) => a.X * b.Y - a.Y * b.X;

    /// <summary>One cut of a contour in the walk's coordinates; <see cref="Reference"/> maps them back to its code.</summary>
    private readonly record struct Segment(int Index, Motion Motion, Vector Start, Vector End, Vector? Center, Vector Reference);

    /// <summary>The last cut's new code-relative end and center, and the return jog to remove.</summary>
    private readonly record struct Edit(int LastCut, int ReturnJog, Vector EndPoint, Vector CenterPoint);
}
