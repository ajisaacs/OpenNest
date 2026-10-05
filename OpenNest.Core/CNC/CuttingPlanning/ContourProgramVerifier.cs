using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using OpenNest.Diagnostics;
using OpenNest.Geometry;
using Curve = OpenNest.Diagnostics.PostVerificationGeometry.Curve;

namespace OpenNest.CNC.CuttingPlanning;

/// <summary>Independently constructed expected geometry, frozen before final replay.
/// It is not a search verdict or the payload returned to the caller.</summary>
internal sealed class SelectedContourProgram
{
    private readonly PreparedContours owner;
    private readonly ContourChoice[] choices;
    internal OwnedExecution Expected { get; }
    internal double TabSize { get; }

    internal SelectedContourProgram(PreparedContours owner, IReadOnlyList<ContourChoice> choices,
        OwnedExecution expected, double tabSize)
    {
        this.owner = owner;
        this.choices = choices.ToArray();
        Expected = expected;
        TabSize = tabSize;
    }

    internal bool Matches(PreparedContours prepared, IReadOnlyList<ContourChoice> selected)
    {
        if (!ReferenceEquals(owner, prepared) || !choices.SequenceEqual(selected)) return false;
        prepared.ValidateCompleteChoices(selected);
        return true;
    }
}

/// <summary>Directed native boundary accounting and selected-emission correspondence.
/// No tessellation, topology inference, live source reads or final emission.</summary>
internal static class ContourProgramVerifier
{
    private const double Epsilon = PostVerificationGeometry.Epsilon;

    internal static bool Verify(OwnedExecution actual, LeadMaterialSnapshot material,
        SelectedContourProgram selected, CancellationToken token)
    {
        var budget = 1000000;
        if (material == null || !material.IsComplete) return false;
        if (selected != null && !SameEmission(actual, selected.Expected, token, ref budget)) return false;
        var visited = new HashSet<int>();
        foreach (var run in Runs(actual).Where(r => !r.Rapid && r.Kind == LayerType.Cut))
        {
            token.ThrowIfCancellationRequested();
            if (run.Curves.Count == 0) return false;
            var first = run.Curves[0];
            var ringIndex = -1;
            var entityIndex = -1;
            for (var r = 0; r < material.Rings.Count && ringIndex < 0; r++)
                for (var e = 0; e < material.Rings[r].Length; e++)
                {
                    Query(token, ref budget);
                    var curve = material.Rings[r][e];
                    if (curve.SameDirection(first) && curve.Contains(first.Start)
                        && curve.DistanceAlong(first.Start) < curve.Length - Epsilon)
                    {
                        ringIndex = r;
                        entityIndex = e;
                        break;
                    }
                }
            if (ringIndex < 0 || !visited.Add(ringIndex)) return false;
            var ring = material.Rings[ringIndex];
            var nominalLength = ring.Sum(c => c.Length);
            var cutLength = run.Curves.Sum(c => c.Length);
            var missing = nominalLength - cutLength;
            if (missing < -Epsilon) return false; // Never a second circuit/retrace.
            if (missing > Epsilon)
            {
                // Fixed legacy payloads have no certified selected tab/entry metadata.
                // A merely open contour or a caller's current tab switch proves nothing.
                if (selected == null || ringIndex != 0 || selected.TabSize <= 0
                    || System.Math.Abs(first.Start.DistanceTo(run.Curves[^1].End) - selected.TabSize) > Epsilon)
                    return false;
                // SameEmission has already bound this exact terminal gap to the owned
                // settings' independently emitted, rounded/clamped selected geometry.
            }
            if (!Follows(run.Curves, ring, entityIndex, ring[entityIndex].DistanceAlong(first.Start),
                true, token, ref budget)) return false;
        }
        return visited.Count == material.Rings.Count;
    }

    private static bool SameEmission(OwnedExecution actual, OwnedExecution expected,
        CancellationToken token, ref int budget)
    {
        var left = Runs(actual);
        var right = Runs(expected);
        if (left.Count != right.Count) return false;
        for (var i = 0; i < left.Count; i++)
        {
            Query(token, ref budget);
            var a = left[i];
            var b = right[i];
            if (a.Kind != b.Kind || a.Rapid != b.Rapid) return false;
            if (a.Rapid)
            {
                // Arrival is supplied anew by replay, not captured expected geometry.
                if (a.End.DistanceTo(b.End) > Epsilon) return false;
            }
            else if (System.Math.Abs(a.Curves.Sum(c => c.Length) - b.Curves.Sum(c => c.Length)) > Epsilon
                || !Follows(a.Curves, b.Curves, 0, 0, false, token, ref budget)) return false;
        }
        return true;
    }

    // Consume directed native arc-length from both streams. Subdivision, merged
    // collinear moves and a cyclic ring's reindexing do not change coverage.
    private static bool Follows(IReadOnlyList<Curve> actual, IReadOnlyList<Curve> expected,
        int index, double offset, bool cyclic, CancellationToken token, ref int budget)
    {
        foreach (var curve in actual)
        {
            var consumed = 0.0;
            while (consumed < curve.Length - Epsilon)
            {
                Query(token, ref budget);
                if (index >= expected.Count)
                {
                    if (!cyclic) return false;
                    index = 0;
                }
                var nominal = expected[index];
                var remaining = nominal.Length - offset;
                if (remaining <= 0)
                {
                    index++;
                    offset = 0;
                    continue;
                }
                var length = System.Math.Min(remaining, curve.Length - consumed);
                if (!nominal.SameDirection(curve)
                    || nominal.PointAtLength(offset).DistanceTo(curve.PointAtLength(consumed)) > Epsilon
                    || nominal.PointAtLength(offset + length).DistanceTo(curve.PointAtLength(consumed + length)) > Epsilon)
                    return false;
                consumed += length;
                offset += length;
                if (offset >= nominal.Length - Epsilon)
                {
                    index++;
                    offset = 0;
                }
            }
        }
        return cyclic || index == expected.Count || index == expected.Count - 1
            && expected[index].Length - offset <= Epsilon;
    }

    private static List<Run> Runs(OwnedExecution execution)
    {
        var runs = new List<Run>();
        foreach (var motion in execution.Motions)
        {
            var kind = motion.Layer is LayerType.Cut or LayerType.Display ? LayerType.Cut : motion.Layer;
            if (motion.Rapid)
            {
                runs.Add(new Run(true, kind, motion.End));
                continue;
            }
            if (motion.Length <= Epsilon) continue; // Zero travel contributes no coverage.
            if (runs.Count == 0 || runs[^1].Rapid || runs[^1].Kind != kind)
                runs.Add(new Run(false, kind, motion.End));
            runs[^1].Curves.Add(motion.Curve);
        }
        return runs;
    }

    private static void Query(CancellationToken token, ref int budget)
    {
        token.ThrowIfCancellationRequested();
        if (--budget < 0) throw new NotSupportedException("Replay contour accounting exceeds the native query limit.");
    }

    private sealed record Run(bool Rapid, LayerType Kind, Vector End)
    {
        internal List<Curve> Curves { get; } = new();
    }
}
