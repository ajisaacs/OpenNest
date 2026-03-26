using System.Collections.Generic;
using OpenNest.CNC;
using OpenNest.Geometry;

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
            if (code is RapidMove)
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
    public static List<(List<ICode> codes, bool isEtch)> ClassifyAndOrder(List<List<ICode>> features)
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
    public static List<(List<ICode> codes, bool isEtch)> SplitAndClassify(Part part) =>
        ClassifyAndOrder(SplitByRapids(part.Program.Codes));

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
                distance += currentPos.DistanceTo(arc.EndPoint);
                currentPos = arc.EndPoint;
            }
        }

        return distance;
    }
}
