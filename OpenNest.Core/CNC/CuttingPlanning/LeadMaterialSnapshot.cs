using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using OpenNest.Diagnostics;
using OpenNest.Geometry;
using OpenNest.Math;

namespace OpenNest.CNC.CuttingPlanning;

/// <summary>Owned nominal material: one simple perimeter minus disjoint simple holes.</summary>
public sealed class LeadMaterialSnapshot
{
    private LeadMaterialSnapshot(IReadOnlyList<PostVerificationGeometry.Curve[]> rings, IReadOnlyList<bool[]> tabs,
        string reason, bool displayPerimeter = false)
    {
        HasDisplayPerimeter = displayPerimeter;
        Rings = rings;
        Tabs = tabs;
        Reason = reason;
        var extent = PostVerificationGeometry.Extent.None;
        foreach (var ring in rings)
            foreach (var curve in ring)
                extent = extent.Union(curve.Extent);
        Extent = extent;
        BoundaryCount = rings.Sum(ring => ring.Length);
    }

    public bool IsComplete => Reason == null;
    public string Reason { get; }
    internal IReadOnlyList<PostVerificationGeometry.Curve[]> Rings { get; }

    /// <summary>A conservative extent of every boundary's native contact reach; material lies within it.</summary>
    internal PostVerificationGeometry.Extent Extent { get; }

    /// <summary>The boundary curves of all rings: the native queries one full scan charges.</summary>
    internal int BoundaryCount { get; }

    /// <summary>
    /// Per ring curve, whether it is a tabbed cutout's tab: material boundary that the
    /// program never cuts. Aligned with <see cref="Rings"/>.
    /// </summary>
    internal IReadOnlyList<bool[]> Tabs { get; }

    /// <summary>
    /// True when the perimeter (<see cref="Rings"/>[0]) is a display perimeter: a supplied blank's
    /// edge that bounds material but is never cut. Every other ring is a hole the program cuts.
    /// </summary>
    internal bool HasDisplayPerimeter { get; }

    /// <summary>Capture a stable, clean, rotation-baked program, applying location once.
    /// Unsupported or malformed geometry produces an incomplete snapshot; cancellation throws.</summary>
    public static LeadMaterialSnapshot Capture(Program cleanProgram, Vector location,
        CancellationToken token = default)
    {
        try
        {
            var execution = ExecutionMotionReader.ReadSupported(cleanProgram, location, null, token);
            var rings = new List<PostVerificationGeometry.Curve[]>();
            var ringTabs = new List<bool[]>();
            var chain = new List<PostVerificationGeometry.Curve>();
            var chainTabs = new List<bool>();
            var ringDisplay = new List<bool>();
            var chainDisplay = new List<bool>();
            foreach (var motion in execution.Motions)
            {
                token.ThrowIfCancellationRequested();
                // Break cuts run through material that stays attached at tabs; they bound nothing.
                if (motion.Rapid || motion.Layer is LayerType.Scribe or LayerType.Leadin or LayerType.Leadout or LayerType.Break)
                {
                    Finish();
                    continue;
                }
                // A tabbed cutout's tabs are never cut but still close its hole.
                // A display perimeter is never cut but still bounds the blank's material.
                if (motion.Layer is not (LayerType.Cut or LayerType.Display or LayerType.Tab or LayerType.DisplayPerimeter))
                    throw new ArgumentException("Material capture contains an unsupported layer.");
                var curve = motion.Curve;
                if (curve == null || !double.IsFinite(curve.Length) || curve.Length <= PostVerificationGeometry.Epsilon)
                    throw new ArgumentException("Material contains a degenerate motion.");
                if (chain.Count > 0 && chain[^1].End.DistanceTo(curve.Start) > PostVerificationGeometry.Epsilon)
                    throw new ArgumentException("Material contour is discontinuous.");
                chain.Add(curve);
                chainTabs.Add(motion.Layer == LayerType.Tab);
                chainDisplay.Add(motion.Layer == LayerType.DisplayPerimeter);
                if (NominalContourClosure.IsClosed(chain[0].Start, chain[^1].End))
                    Finish();
            }
            Finish();
            if (rings.Count == 0)
                throw new ArgumentException("Material has no closed contour.");
            var budget = 1000000;
            // Certify simple rings and mutually disjoint boundaries before containment.
            for (var r = 0; r < rings.Count; r++)
                for (var s = r; s < rings.Count; s++)
                    for (var i = 0; i < rings[r].Length; i++)
                        for (var j = s == r ? i + 1 : 0; j < rings[s].Length; j++)
                        {
                            token.ThrowIfCancellationRequested();
                            if (--budget < 0)
                                throw new NotSupportedException("Material validation exceeds the native query limit.");
                            var a = rings[r][i];
                            var b = rings[s][j];
                            var contacts = a.Contacts(b, out var overlap);
                            var adjacent = r == s && (j == i + 1 || (i == 0 && j == rings[r].Length - 1));
                            var arcJoint = adjacent && a.IsFiniteArc && b.IsFiniteArc
                                && (a.End.DistanceTo(b.Start) <= Tolerance.Epsilon
                                    || a.Start.DistanceTo(b.End) <= Tolerance.Epsilon);
                            // Native arc queries pad angles by 1e-5 radians, admitting
                            // intersections on an extension (distance grows with radius).
                            // At a shared arc joint require both actual finite spans;
                            // overlap and genuine second intersections still refuse.
                            if (arcJoint)
                                contacts = contacts.Where(p => !a.IsOutsideArcSpan(p) && !b.IsOutsideArcSpan(p)).ToArray();
                            // Native contacts at rounded arc/line joints can drift from the
                            // authored endpoint. Use the shared geometry tolerance only to
                            // recognize that joint; overlaps and other contacts still refuse.
                            if (overlap || contacts.Any(p => !adjacent
                                || !(p.DistanceTo(a.End) <= Tolerance.Epsilon
                                    && p.DistanceTo(b.Start) <= Tolerance.Epsilon)
                                && !(p.DistanceTo(a.Start) <= Tolerance.Epsilon
                                    && p.DistanceTo(b.End) <= Tolerance.Epsilon)))
                                throw new ArgumentException($"Material boundaries overlap, touch or self-intersect ({r}:{i}, {s}:{j}, overlap={overlap}).");
                        }
            var outer = -1;
            for (var i = 0; i < rings.Count; i++)
            {
                token.ThrowIfCancellationRequested();
                if (Enumerable.Range(0, rings.Count).All(j => i == j || Inside(rings[j][0].Start, rings[i], token)))
                {
                    if (outer >= 0)
                        throw new ArgumentException("Material perimeter is ambiguous.");
                    outer = i;
                }
            }
            if (outer < 0)
                throw new ArgumentException("Material must have exactly one enclosing perimeter.");
            if (ringTabs[outer].Any(tab => tab))
                throw new ArgumentException("A perimeter cannot have tabs.");
            if (ringDisplay.Where((_, i) => i != outer).Any(display => display))
                throw new ArgumentException("A display perimeter must enclose every other contour.");
            if (ringDisplay[outer] && rings.Count == 1)
                throw new ArgumentException("A display perimeter with no cutouts has nothing to cut.");
            var holes = rings.Where((_, i) => i != outer).ToArray();
            for (var i = 0; i < holes.Length; i++)
                for (var j = i + 1; j < holes.Length; j++)
                    if (Inside(holes[i][0].Start, holes[j], token) || Inside(holes[j][0].Start, holes[i], token))
                        throw new ArgumentException("Nested cutouts are not supported material.");
            return new LeadMaterialSnapshot(new[] { rings[outer] }.Concat(holes).ToArray(),
                new[] { ringTabs[outer] }.Concat(ringTabs.Where((_, i) => i != outer)).ToArray(), null, ringDisplay[outer]);

            void Finish()
            {
                if (chain.Count == 0)
                    return;
                if (!NominalContourClosure.IsClosed(chain[0].Start, chain[^1].End))
                {
                    var gap = chain[0].Start.DistanceTo(chain[^1].End);
                    throw new ArgumentException(FormattableString.Invariant(
                        $"Nominal material contour {rings.Count + 1} is open: endpoint gap {gap:G6} model units exceeds closure tolerance {NominalContourClosure.Tolerance:G6}.")
                        + " Review the source contour in the drawing editor; gaps cannot be filled implicitly.");
                }
                if (chainDisplay.Contains(true) && chainDisplay.Contains(false))
                    throw new ArgumentException("A display perimeter must be one whole closed contour.");
                rings.Add(chain.ToArray());
                ringTabs.Add(chainTabs.ToArray());
                ringDisplay.Add(chainDisplay.Count > 0 && chainDisplay[0]);
                chain.Clear();
                chainTabs.Clear();
                chainDisplay.Clear();
            }
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
        {
            return new LeadMaterialSnapshot(Array.Empty<PostVerificationGeometry.Curve[]>(), Array.Empty<bool[]>(), ex.Message);
        }
    }

    internal bool ContainsMaterial(Vector point, CancellationToken token) => Inside(point, Rings[0], token)
        && !Rings.Skip(1).Any(hole => Inside(point, hole, token));

    internal static bool Inside(Vector point, IReadOnlyList<PostVerificationGeometry.Curve> ring, CancellationToken token)
    {
        var inside = false;
        foreach (var curve in ring)
        {
            token.ThrowIfCancellationRequested();
            if (curve.CrossesRay(point))
                inside = !inside;
        }
        return inside;
    }
}
