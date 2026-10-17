using System.Collections.Generic;
using System.Linq;
using System.Threading;
using OpenNest.Geometry;
using OpenNest.Math;

namespace OpenNest.CNC.CuttingPlanning;

/// <summary>
/// Whether one part's clean material lies strictly inside a cutout of another's. The cutting
/// planner's prerequisites and the pre-post review share this proof, so a pair the planner orders
/// inner-before-host is the pair posting checks. Bounds only select candidates; containment is
/// proven on native material, and any boundary contact or incomplete material is ambiguous.
/// </summary>
internal static class NestedPartContainment
{
    private const int ContactBudget = 1000000;

    /// <summary>
    /// True when <paramref name="inner"/>'s perimeter lies strictly inside one cutout of
    /// <paramref name="host"/>, false when it lies in none; otherwise null, with
    /// <paramref name="ambiguity"/> saying why containment cannot be established.
    /// </summary>
    internal static bool? InsideCutout(LeadMaterialSnapshot inner, LeadMaterialSnapshot host,
        CancellationToken token, out string ambiguity)
    {
        ambiguity = null;
        if (!inner.IsComplete || !host.IsComplete)
        {
            ambiguity = inner.Reason ?? host.Reason;
            return null;
        }
        var budget = ContactBudget;
        foreach (var curve in inner.Rings[0])
            foreach (var ring in host.Rings)
                foreach (var boundary in ring)
                {
                    token.ThrowIfCancellationRequested();
                    if (--budget < 0)
                    {
                        ambiguity = "the containment check exceeds the native query limit";
                        return null;
                    }
                    if (curve.Contacts(boundary, out var overlap).Count != 0 || overlap)
                    {
                        ambiguity = "their boundaries touch or cross";
                        return null;
                    }
                }
        var probe = inner.Rings[0][0].Start;
        return host.Rings.Skip(1).Any(hole => LeadMaterialSnapshot.Inside(probe, hole, token));
    }

    /// <summary>Material extent of a clean program at a location: rapids and scribe marks excluded.</summary>
    internal static Box CleanBounds(Program clean, Vector location, CancellationToken token) =>
        ExecutionMotionReader.ReadSupported(clean, location, null, token).Motions
            .Where(m => !m.Rapid && m.Layer is LayerType.Cut or LayerType.Display && m.Curve != null)
            .Select(m => m.Curve.ToEntity().BoundingBox).ToList().GetBoundingBox();

    /// <summary>Whether a host's bounds admit an inner part's bounds as a containment candidate.</summary>
    internal static bool Within(Box inner, Box outer) =>
        inner.Left >= outer.Left - Tolerance.Epsilon && inner.Right <= outer.Right + Tolerance.Epsilon
        && inner.Bottom >= outer.Bottom - Tolerance.Epsilon && inner.Top <= outer.Top + Tolerance.Epsilon;

    /// <summary>The union of two boxes.</summary>
    internal static Box Union(Box a, Box b)
    {
        var left = System.Math.Min(a.Left, b.Left);
        var bottom = System.Math.Min(a.Bottom, b.Bottom);
        return new(left, bottom, System.Math.Max(a.Right, b.Right) - left, System.Math.Max(a.Top, b.Top) - bottom);
    }
}
