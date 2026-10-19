using System.Collections.Generic;
using OpenNest.CNC;
using OpenNest.Geometry;
using OpenNest.Math;

namespace OpenNest.Posts.Cincinnati;

/// <summary>
/// Shared utilities for splitting CNC programs into features and classifying them.
/// </summary>
public static class FeatureUtils
{
    /// <summary>
    /// Splits a flat list of codes into feature groups, breaking on rapid moves.
    /// Each feature starts with a rapid move followed by cutting/etching moves.
    /// </summary>
    public static List<List<ICode>> SplitByRapids(List<ICode> codes)
    {
        var features = new List<List<ICode>>();
        List<ICode> current = null;

        foreach (var code in codes)
        {
            if (code is SubProgramCall)
            {
                // Flush any pending feature
                if (current != null)
                    features.Add(current);
                // SubProgramCall is its own feature
                features.Add(new List<ICode> { code });
                current = null;
            }
            else if (code is RapidMove)
            {
                if (current != null)
                    features.Add(current);
                current = new List<ICode> { code };
            }
            else
            {
                current ??= new List<ICode>();
                current.Add(code);
            }
        }

        if (current != null && current.Count > 0)
            features.Add(current);

        return features;
    }

    /// <summary>
    /// Classifies features as etch or cut and orders etch features before cut features.
    /// </summary>
    public static List<(List<ICode> codes, bool isEtch)> ClassifyAndOrder(
        List<List<ICode>> features
    )
    {
        var result = new List<(List<ICode>, bool)>();
        var etch = new List<List<ICode>>();
        var cut = new List<List<ICode>>();

        foreach (var f in features)
        {
            if (IsEtch(f))
                etch.Add(f);
            else
                cut.Add(f);
        }

        foreach (var f in etch)
            result.Add((f, true));
        foreach (var f in cut)
            result.Add((f, false));

        return result;
    }

    /// <summary>
    /// Splits a part's program into features by rapids, classifies each as etch or cut,
    /// and orders etch features before cut features. The part's program is not modified.
    /// </summary>
    public static List<(List<ICode> codes, bool isEtch)> SplitAndClassify(Part part)
    {
        var codes = RemoveSuppressed(AbsoluteCodes(part.Program));
        InsertLeadingRapid(codes);

        return ClassifyAndOrder(SplitByRapids(codes));
    }

    /// <summary>
    /// Returns <paramref name="program"/>'s codes in absolute mode without changing the
    /// program. Hole calls are the program's own instances, so lookups by hole program
    /// reference still match; every other code of an incremental program is a copy.
    /// </summary>
    public static List<ICode> AbsoluteCodes(Program program)
    {
        if (program.Mode == Mode.Absolute)
            return new List<ICode>(program.Codes);

        var copy = ToAbsoluteCopy(program);
        var codes = new List<ICode>(copy.Codes.Count);
        for (var i = 0; i < copy.Codes.Count; i++)
            codes.Add(copy.Codes[i] is SubProgramCall ? program.Codes[i] : copy.Codes[i]);
        return codes;
    }

    /// <summary>
    /// Leaves suppressed moves uncut, matching the other posts and the cut simulation.
    /// Absolute codes only. A cut that resumes after a suppressed run starts with a
    /// rapid to the run's end, so the gap is crossed with the beam off rather than cut
    /// as a chord. Rapids left with nothing to cut, and hole calls whose moves are all
    /// suppressed, are dropped so they cannot post an empty pierce.
    /// </summary>
    public static List<ICode> RemoveSuppressed(List<ICode> codes)
    {
        var kept = new List<ICode>(codes.Count);
        Vector? resumeAt = null;
        foreach (var code in codes)
        {
            if (code is Motion { Suppressed: true } suppressed)
            {
                resumeAt = suppressed.EndPoint;
                continue;
            }

            if (code is SubProgramCall call && !HasCut(call.Program))
                continue;

            if (resumeAt.HasValue && code is Motion and not RapidMove)
                kept.Add(new RapidMove(resumeAt.Value));
            resumeAt = null;
            kept.Add(code);
        }

        var result = new List<ICode>(kept.Count);
        for (var i = 0; i < kept.Count; i++)
        {
            if (kept[i] is RapidMove && !CutFollows(kept, i + 1))
                continue;
            result.Add(kept[i]);
        }
        return result;
    }

    // True when a cutting move comes before the next rapid or hole call.
    private static bool CutFollows(List<ICode> codes, int start)
    {
        for (var i = start; i < codes.Count; i++)
        {
            if (codes[i] is RapidMove or SubProgramCall)
                return false;
            if (codes[i] is Motion)
                return true;
        }
        return false;
    }

    /// <summary>True when <paramref name="program"/> has an unsuppressed cutting move.</summary>
    public static bool HasCut(Program program)
    {
        if (program == null)
            return false;
        foreach (var code in program.Codes)
        {
            if (code is Motion { Suppressed: false } and not RapidMove)
                return true;
            if (code is SubProgramCall call && HasCut(call.Program))
                return true;
        }
        return false;
    }

    /// <summary>
    /// The pierce point of a feature in its program's frame: its first rapid, or for a
    /// hole call the hole's first pierce shifted to the call offset.
    /// </summary>
    public static Vector StartPoint(List<ICode> feature)
    {
        if (feature.Count == 1 && feature[0] is SubProgramCall call && call.Program != null)
            return call.Offset + StartPoint(AbsoluteCodes(call.Program));

        foreach (var code in feature)
        {
            if (code is RapidMove rapid)
                return rapid.EndPoint;
        }
        foreach (var code in feature)
        {
            if (code is Motion motion)
                return motion.EndPoint;
        }
        return Vector.Zero;
    }

    /// <summary>Where the tool stops at the end of a feature, in its program's frame.</summary>
    public static Vector EndPoint(List<ICode> feature)
    {
        if (feature.Count == 1 && feature[0] is SubProgramCall call && call.Program != null)
            return call.Offset + EndPoint(AbsoluteCodes(call.Program));

        for (var i = feature.Count - 1; i >= 0; i--)
        {
            if (feature[i] is Motion motion)
                return motion.EndPoint;
        }
        return Vector.Zero;
    }

    /// <summary>
    /// Marks each part's exterior features: cut features whose cut moves (lead moves
    /// excluded) span the extents of all the part's cut moves. Etch features and hole
    /// calls are never exterior. A part whose outline is cut in pieces has none.
    /// </summary>
    public static bool[] FindExterior(IReadOnlyList<(List<ICode> codes, bool isEtch)> features)
    {
        var bounds = new Box[features.Count];
        Box all = null;
        for (var i = 0; i < features.Count; i++)
        {
            var (codes, isEtch) = features[i];
            if (isEtch || (codes.Count == 1 && codes[0] is SubProgramCall))
                continue;
            bounds[i] = CutBounds(codes);
            if (bounds[i] != null)
                all = all == null ? bounds[i] : Union(all, bounds[i]);
        }

        var result = new bool[features.Count];
        for (var i = 0; i < features.Count; i++)
            result[i] = bounds[i] != null && SameExtents(bounds[i], all);
        return result;
    }

    private static Box CutBounds(List<ICode> codes)
    {
        var cut = new Program(Mode.Absolute);
        var position = Vector.Zero;
        var inCut = false;
        foreach (var code in codes)
        {
            if (code is not Motion motion)
                continue;

            var layer = motion switch
            {
                LinearMove linear => linear.Layer,
                ArcMove arc => arc.Layer,
                _ => LayerType.Leadin,
            };
            var isCut = motion is not RapidMove && layer is not (LayerType.Leadin or LayerType.Leadout);
            if (isCut)
            {
                if (!inCut)
                    cut.Codes.Add(new RapidMove(position));
                cut.Codes.Add(motion);
            }
            inCut = isCut;
            position = motion.EndPoint;
        }

        return cut.Codes.Count == 0 ? null : cut.BoundingBox();
    }

    private static Box Union(Box a, Box b)
    {
        var left = System.Math.Min(a.Left, b.Left);
        var bottom = System.Math.Min(a.Bottom, b.Bottom);
        var right = System.Math.Max(a.Right, b.Right);
        var top = System.Math.Max(a.Top, b.Top);
        return new Box(left, bottom, right - left, top - bottom);
    }

    private static bool SameExtents(Box a, Box b) =>
        a.Left.IsEqualTo(b.Left)
        && a.Right.IsEqualTo(b.Right)
        && a.Bottom.IsEqualTo(b.Bottom)
        && a.Top.IsEqualTo(b.Top);

    /// <summary>
    /// If the first contour motion has no rapid before it, the feature writer would
    /// pierce at that motion's endpoint and lose the first segment. Inserts a synthetic
    /// rapid at the contour's return point (the last motion endpoint) just before it.
    /// Hole calls ahead of it are self-contained features and stay in place: a program
    /// that opens with a hole call followed by a rapid needs no synthetic rapid, which
    /// would otherwise post an empty pierce.
    /// </summary>
    public static void InsertLeadingRapid(List<ICode> codes)
    {
        var firstMotion = codes.FindIndex(code => code is Motion);
        if (firstMotion < 0 || codes[firstMotion] is RapidMove)
            return;

        var lastMotion = (Motion)codes.FindLast(code => code is Motion);
        codes.Insert(firstMotion, new RapidMove(lastMotion.EndPoint));
    }

    /// <summary>
    /// Returns an absolute-mode copy of <paramref name="program"/>, leaving the
    /// caller's program (and its mode) untouched.
    /// </summary>
    public static Program ToAbsoluteCopy(Program program)
    {
        var copy = (Program)program.Clone();
        copy.Mode = Mode.Absolute;
        return copy;
    }

    /// <summary>
    /// Returns true if any non-rapid move in the feature has LayerType.Scribe.
    /// </summary>
    public static bool IsEtch(List<ICode> codes)
    {
        foreach (var code in codes)
        {
            if (code is LinearMove linear && linear.Layer == LayerType.Scribe)
                return true;
            if (code is ArcMove arc && arc.Layer == LayerType.Scribe)
                return true;
        }
        return false;
    }

    /// <summary>
    /// Computes the total cut distance of a feature by summing segment lengths.
    /// </summary>
    public static double ComputeCutDistance(List<ICode> codes)
    {
        var distance = 0.0;
        var currentPos = Vector.Zero;

        foreach (var code in codes)
        {
            if (code is RapidMove rapid)
                currentPos = rapid.EndPoint;
            else if (code is LinearMove linear)
            {
                distance += currentPos.DistanceTo(linear.EndPoint);
                currentPos = linear.EndPoint;
            }
            else if (code is ArcMove arc)
            {
                distance += ComputeArcLength(currentPos, arc);
                currentPos = arc.EndPoint;
            }
        }

        return distance;
    }

    /// <summary>
    /// Computes the arc length from the current position through an arc move.
    /// Uses radius * sweep angle instead of chord length.
    /// </summary>
    public static double ComputeArcLength(Vector startPos, ArcMove arc)
    {
        var radius = startPos.DistanceTo(arc.CenterPoint);
        if (radius < Tolerance.Epsilon)
            return 0.0;

        // Full circle: start ≈ end
        if (
            Tolerance.IsEqualTo(startPos.X, arc.EndPoint.X)
            && Tolerance.IsEqualTo(startPos.Y, arc.EndPoint.Y)
        )
            return 2.0 * System.Math.PI * radius;

        var startAngle = System.Math.Atan2(
            startPos.Y - arc.CenterPoint.Y,
            startPos.X - arc.CenterPoint.X
        );
        var endAngle = System.Math.Atan2(
            arc.EndPoint.Y - arc.CenterPoint.Y,
            arc.EndPoint.X - arc.CenterPoint.X
        );

        double sweep;
        if (arc.Rotation == RotationType.CW)
        {
            sweep = startAngle - endAngle;
            if (sweep <= 0)
                sweep += 2.0 * System.Math.PI;
        }
        else
        {
            sweep = endAngle - startAngle;
            if (sweep <= 0)
                sweep += 2.0 * System.Math.PI;
        }

        return radius * sweep;
    }
}
