using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using OpenNest.CNC.CuttingPlanning;
using OpenNest.Geometry;

namespace OpenNest.Engine.CuttingPlanning;

/// <summary>
/// Proposes the ORDER in which a part's remaining holes are cut when the perimeter entry is
/// already chosen: an open path from the tool's arrival through every hole centre and ON to
/// the selected perimeter entry — the terminal edge is real rapid travel and is optimised,
/// not scored after the fact. It reuses <see cref="CuttingPartOrder"/>'s bounded nearest-
/// neighbour + 2-opt/Or-opt machinery (no exhaustive permutations) with an optional fixed
/// endpoint, so whole-part routing without an endpoint keeps byte-identical behaviour.
/// This is a proposal only: the search still certifies every rapid and emitted lead along
/// it, exactly as it does for part orders.
/// </summary>
internal static class CuttingHoleOrder
{
    /// <summary>
    /// One representative point per hole ordinal of <paramref name="prepared"/>, in its LOCAL
    /// coordinates. The tool arriving inside a cutout from a part nested there says nothing
    /// useful about that cutout's centre: it is routed from where the tool is instead, before
    /// a later hop to it has to cross the other parts nested in it.
    /// </summary>
    internal static Vector[] Centres(PreparedContours prepared, Vector localArrival, CancellationToken token)
    {
        var centres = prepared.HoleCentres(token).Select(c => c ?? Vector.Zero).ToArray();
        for (var hole = 0; hole < prepared.PerimeterOrdinal; hole++)
            if (prepared.HoleContains(hole, localArrival))
                centres[hole] = localArrival;
        return centres;
    }

    /// <summary>
    /// Orders <paramref name="holes"/> (stable ordinals, each appearing exactly once in the
    /// result, never the perimeter) to minimize arrival -&gt; holes -&gt; <paramref
    /// name="perimeterEntry"/> distance. <paramref name="centresByOrdinal"/> supplies one
    /// representative point per hole ordinal. Empty holes give an empty order. Without a
    /// perimeter entry the path is open: it ends at whichever hole keeps it shortest.
    /// </summary>
    internal static IReadOnlyList<int> Plan(IReadOnlyList<int> holes,
        IReadOnlyList<Vector> centresByOrdinal, Vector arrival, Vector? perimeterEntry,
        CancellationToken token = default)
    {
        if (holes == null)
            throw new ArgumentException("Hole ordinals are required.", nameof(holes));
        if (centresByOrdinal == null)
            throw new ArgumentException("Hole centres are required.", nameof(centresByOrdinal));
        token.ThrowIfCancellationRequested();
        if (holes.Count == 0)
            return Array.Empty<int>();

        var none = Array.Empty<int>();
        var prerequisites = holes.Select(_ => (IReadOnlyCollection<int>)none).ToList();
        var centres = holes.Select(hole => centresByOrdinal[hole]).ToList();
        var route = CuttingPartOrder.Plan(centres, arrival, prerequisites, token, perimeterEntry);
        return route.Select(index => holes[index]).ToArray();
    }
}
