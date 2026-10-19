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
    /// and orders etch features before cut features.
    /// </summary>
    public static List<(List<ICode> codes, bool isEtch)> SplitAndClassify(Part part)
    {
        part.Program.Mode = Mode.Absolute;
        var codes = new List<ICode>(part.Program.Codes);
        InsertLeadingRapid(codes);

        return ClassifyAndOrder(SplitByRapids(codes));
    }

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
