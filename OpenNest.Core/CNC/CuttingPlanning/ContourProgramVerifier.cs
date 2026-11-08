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
        // Per tabbed ring: what each run cut, as (span start, from, to) along its span.
        var piecesCut = new Dictionary<int, List<(int Span, double From, double To)>>();
        foreach (var run in Runs(actual).Where(r => !r.Rapid && r.Kind == LayerType.Cut))
        {
            token.ThrowIfCancellationRequested();
            if (run.Curves.Count == 0) return false;
            var first = run.Curves[0];
            var ringIndex = -1;
            var entityIndex = -1;
            // The admitted slack is far below any extent margin, so only boundaries near the run's
            // start can contain it; they come in ring order, as the full scan met them.
            foreach (var (r, e) in material.PositionsNear(new(first.Start.X, first.Start.Y, first.Start.X, first.Start.Y)))
            {
                Query(token, ref budget);
                var admitted = AdmittedClosureSlack(material.Rings[r]);
                var curve = material.Rings[r][e];
                if (curve.SameDirection(first, admitted) && curve.Contains(first.Start, admitted)
                    && curve.DistanceAlong(first.Start) < curve.Length - Epsilon)
                {
                    ringIndex = r;
                    entityIndex = e;
                    break;
                }
            }
            if (ringIndex < 0) return false;
            // A display perimeter is never cut: any run along it is not this plan.
            if (ringIndex == 0 && material.HasDisplayPerimeter) return false;
            var tabs = material.Tabs[ringIndex];
            if (tabs.Any(tab => tab))
            {
                // A tabbed ring: each run follows one span between tabs and never crosses a tab. A
                // span is usually one run; a common-line member that starts on its common line cuts
                // the span holding that start in two, each from where the other began or ended.
                if (CutTabbedSpan(run.Curves, material.Rings[ringIndex], tabs, entityIndex, token, ref budget)
                    is not { } piece)
                    return false;
                (piecesCut.TryGetValue(ringIndex, out var cut) ? cut : piecesCut[ringIndex] = new()).Add(piece);
                continue;
            }
            if (!visited.Add(ringIndex)) return false;
            var ring = material.Rings[ringIndex];
            // Capture admits a nominal ring that closes within NominalContourClosure.Tolerance, and
            // preparation agrees at that inclusive boundary. A rotated tool path bridges that one
            // admitted joint, so this ring's positional and length agreement carries exactly that
            // admitted slack, whether bridging it shortens or lengthens the cut.
            var tolerance = AdmittedClosureSlack(ring);
            var nominalLength = ring.Sum(c => c.Length);
            var cutLength = run.Curves.Sum(c => c.Length);
            var missing = nominalLength - cutLength;
            // Bridging the admitted joint may add up to its gap; a second circuit/retrace never passes.
            if (missing < -tolerance) return false;
            if (missing > tolerance)
            {
                // Fixed legacy payloads have no certified selected tab/entry metadata.
                // A merely open contour or a caller's current tab switch proves nothing.
                if (selected == null || ringIndex != 0 || selected.TabSize <= 0
                    || System.Math.Abs(first.Start.DistanceTo(run.Curves[^1].End) - selected.TabSize) > tolerance)
                    return false;
                // SameEmission has already bound this exact terminal gap to the owned
                // settings' independently emitted, rounded/clamped selected geometry.
            }
            if (!Follows(run.Curves, ring, entityIndex, ring[entityIndex].DistanceAlong(first.Start),
                true, tolerance, token, ref budget)) return false;
        }
        // Every span of a tabbed ring is cut exactly once: its pieces tile it with no gap or overlap.
        foreach (var (ringIndex, pieces) in piecesCut)
        {
            var ring = material.Rings[ringIndex];
            var tabs = material.Tabs[ringIndex];
            var tolerance = AdmittedClosureSlack(ring);
            foreach (var start in SpanStarts(tabs))
            {
                var length = Span(ring, tabs, start).Sum(c => c.Length);
                var at = 0.0;
                foreach (var piece in pieces.Where(p => p.Span == start).OrderBy(p => p.From))
                {
                    if (System.Math.Abs(piece.From - at) > tolerance)
                        return false;
                    at = piece.To;
                }
                if (System.Math.Abs(length - at) > tolerance)
                    return false;
            }
            visited.Add(ringIndex);
        }
        return visited.Count == material.Rings.Count - (material.HasDisplayPerimeter ? 1 : 0);
    }

    // Indexes of the curves that start a span between tabs: uncut-to-cut transitions.
    private static List<int> SpanStarts(bool[] tabs) => Enumerable.Range(0, tabs.Length)
        .Where(i => !tabs[i] && tabs[(i + tabs.Length - 1) % tabs.Length]).ToList();

    // What a run along a tabbed ring cuts: its span (by the span's first curve) and where along the
    // span it starts and ends. Null unless the run follows the ring from its start without
    // reaching past the span's next tab.
    private static (int Span, double From, double To)? CutTabbedSpan(IReadOnlyList<Curve> run,
        IReadOnlyList<Curve> ring, bool[] tabs, int entityIndex, CancellationToken token, ref int budget)
    {
        if (tabs[entityIndex])
            return null;
        var tolerance = AdmittedClosureSlack(ring);
        var count = tabs.Length;
        var start = entityIndex;
        var from = ring[entityIndex].DistanceAlong(run[0].Start);
        for (var guard = 0; guard < count && !tabs[(start + count - 1) % count]; guard++)
        {
            start = (start + count - 1) % count;
            from += ring[start].Length;
        }
        var rest = new List<Curve>();
        for (var e = entityIndex; !tabs[e % count] && rest.Count < count; e++)
            rest.Add(ring[e % count]);
        var length = run.Sum(c => c.Length);
        var offset = ring[entityIndex].DistanceAlong(run[0].Start);
        if (length > rest.Sum(c => c.Length) - offset + tolerance
            || !Follows(run, rest, 0, offset, false, tolerance, token, ref budget, partial: true))
            return null;
        return (start, from, from + length);
    }

    // The curves of the span between tabs that starts at curve start.
    private static List<Curve> Span(IReadOnlyList<Curve> ring, bool[] tabs, int start)
    {
        var span = new List<Curve>();
        for (var e = start; !tabs[e % tabs.Length] && span.Count < tabs.Length; e++)
            span.Add(ring[e % tabs.Length]);
        return span;
    }

    // Zero for an exactly closed ring: a ring may lawfully close within the nominal closure
    // tolerance, and only that admitted endpoint gap relaxes replay's positional agreement.
    private static double AdmittedClosureSlack(IReadOnlyList<Curve> ring) => ring.Count == 0 ? Epsilon
        : Epsilon + System.Math.Max(0.0, ring[^1].End.DistanceTo(ring[0].Start));

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
                || !Follows(a.Curves, b.Curves, 0, 0, false, Epsilon, token, ref budget)) return false;
        }
        return true;
    }

    // Consume directed native arc-length from both streams. Subdivision, merged
    // collinear moves and a cyclic ring's reindexing do not change coverage.
    // `tolerance` is the admitted positional slack for this pair of streams (the exact epsilon
    // unless the nominal ring closes only within the nominal closure tolerance).
    // `partial` accepts an actual stream that ends before the expected one does.
    private static bool Follows(IReadOnlyList<Curve> actual, IReadOnlyList<Curve> expected,
        int index, double offset, bool cyclic, double tolerance, CancellationToken token, ref int budget,
        bool partial = false)
    {
        foreach (var curve in actual)
        {
            var consumed = 0.0;
            // Like the nominal side below, a remainder within the slack is consumed: a path that
            // bridges the admitted joint leaves exactly that much on the edge it extends.
            while (consumed < curve.Length - tolerance)
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
                if (!nominal.SameDirection(curve, tolerance)
                    || nominal.PointAtLength(offset).DistanceTo(curve.PointAtLength(consumed)) > tolerance
                    || nominal.PointAtLength(offset + length).DistanceTo(curve.PointAtLength(consumed + length)) > tolerance)
                    return false;
                consumed += length;
                offset += length;
                if (offset >= nominal.Length - tolerance)
                {
                    index++;
                    offset = 0;
                }
            }
        }
        return cyclic || partial || index == expected.Count || index == expected.Count - 1
            && expected[index].Length - offset <= tolerance;
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
