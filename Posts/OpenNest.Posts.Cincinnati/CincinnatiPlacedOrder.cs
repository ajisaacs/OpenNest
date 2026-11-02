using System;
using System.Collections.Generic;
using System.Linq;
using OpenNest.CNC;
using OpenNest.Geometry;

namespace OpenNest.Posts.Cincinnati;

/// <summary>
/// Decides whether the post moves through a nest in the placed order the pre-post check
/// verified (see docs/post-verification.md). The writer keeps plate order, but writes each
/// part's (and hole's) etch features before its cut features, adds a rapid ahead of a contour
/// that has none, drops rapids with nothing to cut and, with part sub-programs, rapids to and
/// from each part's corner. The order is declared preserved only when, for this nest and
/// these settings, none of that changes a single motion.
/// </summary>
internal static class CincinnatiPlacedOrder
{
    /// <summary>Why the posted motion sequence differs from the placed one, or null when it equals it.</summary>
    internal static string Change(Nest nest, CincinnatiPostConfig config)
    {
        if (config.UsePartSubprograms)
            return "calls part sub-programs, which add rapids to and from each part's corner";
        if (config.UseSmartRapids)
            return "uses smart rapids (G121), whose blended rapids leave the straight line";
        if (nest?.Plates == null)
            return null;

        for (var plateIndex = 0; plateIndex < nest.Plates.Count; plateIndex++)
        {
            var parts = nest.Plates[plateIndex]?.Parts;
            if (parts == null)
                continue;
            for (var partIndex = 0; partIndex < parts.Count; partIndex++)
            {
                var part = parts[partIndex];
                if (part?.Program == null)
                    continue;
                var where = $"part {partIndex + 1} on plate {plateIndex + 1}";
                List<ICode> placed;
                List<(List<ICode> codes, bool isEtch)> posted;
                try
                {
                    placed = FeatureUtils.AbsoluteCodes(FeatureUtils.PostedProgram(part));
                    posted = FeatureUtils.SplitAndClassify(part);
                }
                catch (InvalidOperationException)
                {
                    return $"cannot post {where} as placed";
                }

                var change = ChangeTo(placed, posted, where);
                if (change != null)
                    return change;

                foreach (var call in placed.OfType<SubProgramCall>())
                {
                    if (call.Program == null || !FeatureUtils.HasCut(call.Program))
                        continue;
                    var hole = CincinnatiPartSubprogramWriter.PostedHole(call.Program);
                    change = ChangeTo(
                        FeatureUtils.AbsoluteCodes(call.Program),
                        FeatureUtils.ClassifyAndOrder(FeatureUtils.SplitByRapids(hole.Codes)),
                        $"a hole of {where}"
                    );
                    if (change != null)
                        return change;
                }
            }
        }
        return null;
    }

    private static string ChangeTo(List<ICode> placed, List<(List<ICode> codes, bool isEtch)> posted, string where)
    {
        if (SameMotions(placed, posted.SelectMany(feature => feature.codes)))
            return null;
        var cutSeen = false;
        foreach (var feature in FeatureUtils.SplitByRapids(placed))
        {
            var isEtch = FeatureUtils.IsEtch(feature);
            if (isEtch && cutSeen)
                return $"writes the etch features of {where} before the cuts placed ahead of them";
            cutSeen |= !isEtch;
        }
        return $"adds or drops rapids in {where}";
    }

    // Motions and hole calls in the same order with exactly the same coordinates.
    private static bool SameMotions(IEnumerable<ICode> placed, IEnumerable<ICode> posted)
    {
        var a = placed.Where(code => code is Motion or SubProgramCall).ToList();
        var b = posted.Where(code => code is Motion or SubProgramCall).ToList();
        if (a.Count != b.Count)
            return false;
        for (var i = 0; i < a.Count; i++)
        {
            if (!Same(a[i], b[i]))
                return false;
        }
        return true;
    }

    private static bool Same(ICode a, ICode b) =>
        (a, b) switch
        {
            (SubProgramCall x, SubProgramCall y) => ReferenceEquals(x.Program, y.Program) && Exact(x.Offset, y.Offset),
            (ArcMove x, ArcMove y) => Exact(x.EndPoint, y.EndPoint)
                && Exact(x.CenterPoint, y.CenterPoint)
                && x.Rotation == y.Rotation,
            (Motion x, Motion y) => x.GetType() == y.GetType() && Exact(x.EndPoint, y.EndPoint),
            _ => false,
        };

    private static bool Exact(Vector a, Vector b) => a.X == b.X && a.Y == b.Y;
}
