using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using OpenNest.Diagnostics;
using OpenNest.Geometry;

namespace OpenNest.CNC.CuttingPlanning;

/// <summary>Owned nominal material: one simple perimeter minus disjoint simple holes.</summary>
public sealed class LeadMaterialSnapshot
{
    private LeadMaterialSnapshot(IReadOnlyList<PostVerificationGeometry.Curve[]> rings, string reason)
    {
        Rings = rings;
        Reason = reason;
    }

    public bool IsComplete => Reason == null;
    public string Reason { get; }
    internal IReadOnlyList<PostVerificationGeometry.Curve[]> Rings { get; }

    /// <summary>Capture a stable, clean, rotation-baked program, applying location once.
    /// Unsupported or malformed geometry produces an incomplete snapshot; cancellation throws.</summary>
    public static LeadMaterialSnapshot Capture(Program cleanProgram, Vector location,
        CancellationToken token = default)
    {
        try
        {
            var execution = ExecutionMotionReader.Read(cleanProgram, location, null, token);
            var rings = new List<PostVerificationGeometry.Curve[]>();
            var chain = new List<PostVerificationGeometry.Curve>();
            foreach (var motion in execution.Motions)
            {
                token.ThrowIfCancellationRequested();
                if (motion.Rapid || motion.Layer is LayerType.Scribe or LayerType.Leadin or LayerType.Leadout)
                {
                    Finish();
                    continue;
                }
                if (motion.Layer is not (LayerType.Cut or LayerType.Display))
                    throw new ArgumentException("Material capture contains an unsupported layer.");
                var curve = motion.Curve;
                if (curve == null || !double.IsFinite(curve.Length) || curve.Length <= PostVerificationGeometry.Epsilon)
                    throw new ArgumentException("Material contains a degenerate motion.");
                if (chain.Count > 0 && chain[^1].End.DistanceTo(curve.Start) > PostVerificationGeometry.Epsilon)
                    throw new ArgumentException("Material contour is discontinuous.");
                chain.Add(curve);
                if (PostVerificationGeometry.Closed(chain))
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
                            if (overlap || contacts.Any(p => !adjacent
                                || !(p.DistanceTo(a.End) <= PostVerificationGeometry.Epsilon
                                    && p.DistanceTo(b.Start) <= PostVerificationGeometry.Epsilon)
                                && !(p.DistanceTo(a.Start) <= PostVerificationGeometry.Epsilon
                                    && p.DistanceTo(b.End) <= PostVerificationGeometry.Epsilon)))
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
            var holes = rings.Where((_, i) => i != outer).ToArray();
            for (var i = 0; i < holes.Length; i++)
                for (var j = i + 1; j < holes.Length; j++)
                    if (Inside(holes[i][0].Start, holes[j], token) || Inside(holes[j][0].Start, holes[i], token))
                        throw new ArgumentException("Nested cutouts are not supported material.");
            return new LeadMaterialSnapshot(new[] { rings[outer] }.Concat(holes).ToArray(), null);

            void Finish()
            {
                if (chain.Count == 0)
                    return;
                if (!PostVerificationGeometry.Closed(chain))
                    throw new ArgumentException("Nominal material contour is open; tab gaps cannot be filled implicitly.");
                rings.Add(chain.ToArray());
                chain.Clear();
            }
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
        {
            return new LeadMaterialSnapshot(Array.Empty<PostVerificationGeometry.Curve[]>(), ex.Message);
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
