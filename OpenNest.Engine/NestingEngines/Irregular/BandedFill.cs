#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Clipper2Lib;
using OpenNest.Engine.Jobs;
using OpenNest.Geometry;

namespace OpenNest.Engine.NestingEngines.Irregular;

/// <summary>
/// Fills one sheet in full-height strips along its length, two ways.
///
/// <see cref="Fill"/> lays each like-part group out as a band: the group's Lattice pattern (the one
/// interactive Fill gives that part alone, so a round part stacks, a hexagon tiles and an L
/// interlocks with its pair) in the narrowest strip that holds all its copies. Bands stand side
/// by side, then the copies held back from them and the types too few to band fill the gaps.
///
/// <see cref="FillStrips"/> builds each strip as densely as it can from any parts: the strip holds
/// the largest remaining part, as its band or as a column of it, and every other part fills the
/// strip's gaps, so big parts go first and small ones fill around them.
///
/// A person nesting many copies of a few parts lays them out in tidy columns; a frontier packer
/// placing one part at a time instead leaves every group's last column ragged and scatters the
/// smallest parts into the gaps between groups.
/// </summary>
internal sealed class BandedFill(
    IReadOnlyList<PartType> types,
    BlockCatalog blocks,
    NestPlateStock stock,
    ClampObstacles? clamps,
    Func<FrontierPacker> packer,
    Comparison<SheetFill> compare,
    WorkCounter counter,
    long budget)
{
    /// <summary>Fewest copies a band may hold back to be narrower; a third of them when more.</summary>
    private const int MaxHeldBack = 4;

    private sealed record Band(int Type, int Copies, double Length, IReadOnlyList<Placed> Members);

    /// <summary>The fill, or null when nothing bands on this sheet.</summary>
    public SheetFill? Fill(IReadOnlyList<int> remaining, CancellationToken token)
    {
        if (stock.Outline != null)
            return null;
        var region = stock.WorkArea;
        var grouped = types.Where(t => remaining[t.Index] >= GroupedFill.MinGroup && t.Orientations.Count > 0)
            .Select(t => t.Index).ToArray();
        if (grouped.Length == 0)
            return null;
        // Per type, its narrowest full band first, then narrower bands holding fewer copies.
        var options = new Dictionary<int, List<Band>>();
        foreach (var (t, list) in grouped.Zip(BandsInParallel(grouped, remaining, region, token)))
            if (list.Count > 0)
                options[t] = list;
        if (options.Count == 0)
            return null;
        var bands = options.ToDictionary(o => o.Key, o => o.Value[0]);

        // Both orders with every band full; then, on the better one, hold back copies band by
        // band while that improves the sheet.
        var orders = new[]
        {
            bands.Values.OrderByDescending(b => b.Length).ThenBy(b => b.Type).Select(b => b.Type).ToArray(),
            bands.Values.OrderBy(b => b.Length).ThenBy(b => b.Type).Select(b => b.Type).ToArray(),
        };
        int[]? bestOrder = null;
        SheetFill? best = null;
        foreach (var order in orders)
        {
            var fill = Run(order, order.ToDictionary(t => t, _ => 0), options, remaining, region, token);
            if (best == null || compare(fill, best) < 0)
            {
                best = fill;
                bestOrder = order;
            }
        }
        var choice = bestOrder!.ToDictionary(t => t, _ => 0);
        foreach (var t in bestOrder!)
            for (var k = 1; k < options[t].Count && counter.Value < budget; k++)
            {
                var trial = new Dictionary<int, int>(choice) { [t] = k };
                var candidate = Run(bestOrder, trial, options, remaining, region, token);
                if (compare(candidate, best!) < 0)
                {
                    best = candidate;
                    choice = trial;
                }
            }
        return best;
    }

    /// <summary>
    /// Strip by strip: each strip holds the largest remaining part, either as its Lattice band
    /// (full or holding fewer copies) or packed largest part first in a strip one to three copies
    /// wide, and every other part fills the strip's gaps, largest piece first. A beam of the
    /// <see cref="Beam"/> partial sheets densest so far (part area per unit of length used) is
    /// carried strip to strip, so a strip is judged by what it leaves for the rest of the sheet,
    /// not by its own density alone; the best finished sheet wins.
    /// </summary>
    public SheetFill? FillStrips(IReadOnlyList<int> remaining, Func<Box, FrontierPacker> within, CancellationToken token)
    {
        if (stock.Outline != null)
            return null;
        var region = stock.WorkArea;
        var spacing = System.Math.Max(0, stock.PartSpacing);
        var bandCache = new Dictionary<(int, int), List<Band>>();
        var lookBack = types.Where(t => remaining[t.Index] > 0).SelectMany(t => t.Orientations)
            .Select(o => System.Math.Max(o.Width, o.Height)).DefaultIfEmpty(0).Max();
        var beam = new List<StripState> { new(new List<Placed>(), remaining.ToArray(), 0, region.Left) };
        var finished = new List<StripState>();
        while (beam.Count > 0)
        {
            token.ThrowIfCancellationRequested();
            var children = new List<StripState>();
            foreach (var state in beam)
            {
                var largest = types.Where(t => state.Rest[t.Index] > 0 && t.Orientations.Count > 0)
                    .OrderByDescending(t => t.Area).ThenBy(t => t.Index).FirstOrDefault();
                if (largest == null || state.Front >= region.Right || counter.Value >= budget)
                {
                    finished.Add(state);
                    continue;
                }
                var t = largest.Index;
                var start = state.Placed.Count == 0 ? state.Front : state.Front + spacing;
                var offered = 0;
                void Offer(List<Placed> seatedNow, double end)
                {
                    var reach = System.Math.Min(region.Right, end + 1e-6);
                    if (reach <= state.Front)
                        return;
                    var counts = state.Rest.ToArray();
                    foreach (var p in seatedNow)
                        counts[p.Orientation.TypeIndex]--;
                    var all = state.Placed.Concat(seatedNow).ToList();
                    // The gap fill looks back one part length behind the strip: gaps further back
                    // were filled with earlier strips, and parts there cost nothing to skip.
                    var left = System.Math.Max(region.Left, start - lookBack);
                    var fill = counts.Any(c => c > 0)
                        ? within(new Box(left, region.Bottom, reach - left, region.Width)).Fill(counts, token, all, region.Top)
                        : new SheetFill(stock, Array.Empty<Placed>(), 0);
                    var parts = seatedNow.Concat(fill.Parts).ToList();
                    if (parts.Count == 0 || !parts.Any(p => p.Orientation.TypeIndex == t))
                        return;
                    var rest = state.Rest.ToArray();
                    foreach (var p in parts)
                        rest[p.Orientation.TypeIndex]--;
                    children.Add(new StripState(all.Concat(fill.Parts).ToList(), rest,
                        state.Area + parts.Sum(p => types[p.Orientation.TypeIndex].Area),
                        System.Math.Max(state.Front, parts.Max(p => p.Right))));
                    offered++;
                }
                if (state.Rest[t] >= GroupedFill.MinGroup)
                {
                    if (!bandCache.TryGetValue((t, state.Rest[t]), out var bands))
                        bandCache[(t, state.Rest[t])] = bands = Bands(t, state.Rest[t], region, token);
                    foreach (var band in bands)
                    {
                        if (band.Length > region.Right - start + 1e-9)
                            continue;
                        var members = Seat(band, start, region);
                        if (members.Count > 0)
                            Offer(members, members.Max(p => p.Right));
                    }
                }
                // Strip widths of one to three copies across, each distinct width once.
                var widths = new List<double>();
                foreach (var o in largest.Orientations)
                    for (var k = 1; k <= 3; k++)
                    {
                        var width = k * o.Width + (k - 1) * spacing;
                        if (!widths.Any(w => System.Math.Abs(w - width) < 0.05))
                            widths.Add(width);
                    }
                foreach (var width in widths.OrderBy(w => w))
                    Offer(new List<Placed>(), start + width);
                if (offered == 0)
                    finished.Add(state);
            }
            double Rank(StripState c) => -c.Area / System.Math.Max(c.Front - region.Left, 1e-9);
            beam = new List<StripState>();
            foreach (var child in children.OrderBy(Rank).ThenBy(c => c.Front))
            {
                if (beam.Count >= Beam)
                    break;
                if (beam.Any(b => System.Math.Abs(b.Front - child.Front) < 1e-6 && System.Math.Abs(b.Area - child.Area) < 1e-6
                    && b.Rest.SequenceEqual(child.Rest)))
                    continue;
                beam.Add(child);
            }
        }
        SheetFill? best = null;
        foreach (var state in finished)
        {
            var placed = state.Placed.ToList();
            var partArea = state.Area;
            if (state.Rest.Any(r => r > 0))
            {
                var tail = packer().Fill(state.Rest, token, placed);
                placed.AddRange(tail.Parts);
                partArea += tail.PartArea;
            }
            var sheet = new SheetFill(stock, placed, partArea);
            if (best == null || compare(sheet, best) < 0)
                best = sheet;
        }
        return best;
    }

    /// <summary>Partial sheets carried strip to strip.</summary>
    private const int Beam = 4;

    private sealed record StripState(List<Placed> Placed, int[] Rest, double Area, double Front);

    /// <summary>A band's members starting at <paramref name="front"/>, flush to the bottom, or to
    /// the top when that keeps more of them off the clamps; members on a clamp are left out.</summary>
    private List<Placed> Seat(Band band, double front, Box region)
    {
        var minX = band.Members.Min(p => p.Left);
        var minY = band.Members.Min(p => p.Bottom);
        var maxY = band.Members.Max(p => p.Top);
        List<Placed> Shifted(double dy) => band.Members
            .Select(m => new Placed(m.Orientation, m.X + front - minX, m.Y + dy)).Where(p => !OnClamp(p)).ToList();
        var members = Shifted(region.Bottom - minY);
        if (members.Count < band.Members.Count && region.Top - maxY > region.Bottom - minY + 1e-9)
        {
            var top = Shifted(region.Top - maxY);
            if (top.Count > members.Count)
                members = top;
        }
        return members;
    }

    /// <summary>The bands placed left to right in <paramref name="order"/>, then everything
    /// else filling around them.</summary>
    private SheetFill Run(int[] order, Dictionary<int, int> choice, Dictionary<int, List<Band>> options,
        IReadOnlyList<int> remaining, Box region, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var seated = new List<Placed>();
        var rest = remaining.ToArray();
        var front = region.Left;
        var spacing = System.Math.Max(0, stock.PartSpacing);
        foreach (var t in order)
        {
            var band = options[t][choice[t]];
            var room = region.Right - front;
            if (room <= 0)
                break;
            if (band.Length > room + 1e-9)
            {
                // The sheet ends inside this band: as many copies as the room left holds.
                var partial = Pattern(t, band.Copies, room, region.Width, token);
                if (partial == null)
                    continue;
                band = partial;
            }
            var minX = band.Members.Min(p => p.Left);
            var minY = band.Members.Min(p => p.Bottom);
            var maxY = band.Members.Max(p => p.Top);
            // Flush to the bottom, or to the top when that keeps more copies off the clamps.
            List<Placed> Shifted(double dy) => band.Members
                .Select(m => new Placed(m.Orientation, m.X + front - minX, m.Y + dy)).Where(p => !OnClamp(p)).ToList();
            var members = Shifted(region.Bottom - minY);
            if (members.Count < band.Members.Count && region.Top - maxY > region.Bottom - minY + 1e-9)
            {
                var top = Shifted(region.Top - maxY);
                if (top.Count > members.Count)
                    members = top;
            }
            var maxX = front;
            foreach (var placed in members)
            {
                seated.Add(placed);
                rest[t]--;
                maxX = System.Math.Max(maxX, placed.Right);
            }
            front = maxX + spacing;
        }
        var parts = seated.ToList();
        var partArea = seated.Sum(p => types[p.Orientation.TypeIndex].Area);
        if (rest.Any(r => r > 0))
        {
            var leftovers = packer().Fill(rest, token, seated);
            parts.AddRange(leftovers.Parts);
            partArea += leftovers.PartArea;
        }
        return new SheetFill(stock, parts, partArea);
    }

    /// <summary>
    /// The type's bands of full region height, narrowest full one first, then each narrower one
    /// holding at most <see cref="HeldBack"/> fewer copies. Lattice patterns grow in whole rows or
    /// columns, so each next band is asked for just short of the last one's length, and a few
    /// halvings toward the area bound catch patterns the steps skip.
    /// </summary>
    private List<Band> Bands(int t, int copies, Box region, CancellationToken token)
    {
        var type = types[t];
        var fewest = copies - HeldBack(copies);
        var found = new List<Band>();
        void Keep(Band band)
        {
            if (band.Copies >= fewest && !found.Any(b => b.Copies >= band.Copies && b.Length <= band.Length + 1e-6))
            {
                found.RemoveAll(b => b.Copies <= band.Copies && b.Length >= band.Length - 1e-6);
                found.Add(band);
            }
        }
        var length = region.Length;
        for (var step = 0; step < MaxSteps; step++)
        {
            var band = Pattern(t, copies, length, region.Width, token);
            if (band == null || band.Copies < fewest)
                break;
            Keep(band);
            length = band.Length - StepShort;
        }
        var full = found.Where(b => b.Copies >= copies).OrderBy(b => b.Length).FirstOrDefault();
        if (full == null)
            return new List<Band>();
        var lo = System.Math.Max(type.Orientations.Min(o => System.Math.Min(o.Width, o.Height)),
            copies * type.Area / region.Width);
        var hi = full.Length;
        for (var probe = 0; probe < Probes && hi - lo > StepShort; probe++)
        {
            var mid = (lo + hi) / 2;
            var band = Pattern(t, copies, mid, region.Width, token);
            if (band != null)
                Keep(band);
            if (band != null && band.Copies >= copies)
                hi = band.Length;
            else
                lo = mid;
        }
        full = found.Where(b => b.Copies >= copies).OrderBy(b => b.Length).First();
        return found.Where(b => b.Copies < copies && b.Length < full.Length - 1e-6).OrderBy(b => -b.Copies)
            .Take(MaxNarrower).Prepend(full).ToList();
    }

    /// <summary>
    /// <see cref="Bands"/> for each type at once. Each type's search depends only on its own
    /// patterns (fixed step counts, no shared budget), so the result is the same in any order.
    /// </summary>
    private List<Band>[] BandsInParallel(int[] typeIndexes, IReadOnlyList<int> remaining, Box region, CancellationToken token)
    {
        var result = new List<Band>[typeIndexes.Length];
        System.Threading.Tasks.Parallel.For(0, typeIndexes.Length,
            new System.Threading.Tasks.ParallelOptions { CancellationToken = token },
            i => result[i] = Bands(typeIndexes[i], remaining[typeIndexes[i]], region, token));
        return result;
    }

    /// <summary>Most copies a narrower band may leave to the gap fill.</summary>
    private static int HeldBack(int copies) => System.Math.Max(MaxHeldBack, copies / 3);

    private const int MaxSteps = 6;

    /// <summary>Narrower bands offered per type, those holding the most copies first.</summary>
    private const int MaxNarrower = 2;
    private const int Probes = 3;
    private const double StepShort = 0.01;

    /// <summary>The type's Lattice pattern in a length x width box, up to the copies wanted.</summary>
    private Band? Pattern(int t, int copies, double length, double width, CancellationToken token)
    {
        var members = blocks.Band(types[t], copies, new Box(0, 0, length, width), token);
        // Bounds carry the arc flattening padding; a pattern they push past the box is no band.
        if (members.Count == 0 || members.Max(p => p.Top) - members.Min(p => p.Bottom) > width + 1e-9)
            return null;
        return new Band(t, members.Count, members.Max(p => p.Right) - members.Min(p => p.Left), members);
    }

    /// <summary>
    /// Whether a band member at its pose enters a clamp keep-out (its clearance included); such
    /// members are left to the gap fill, which packs around the clamps.
    /// </summary>
    private bool OnClamp(Placed member)
    {
        if (clamps == null)
            return false;
        var point = new PointD(member.X, member.Y);
        for (var i = 0; i < clamps.Count; i++)
        {
            var nfp = clamps.Get(i, member.Orientation);
            var b = nfp.Bounds;
            if (point.x < b.left || point.x > b.right || point.y < b.top || point.y > b.bottom)
                continue;
            foreach (var path in nfp.Region)
                if (Clipper.IsPositive(path) && Clipper.PointInPolygon(point, path) != PointInPolygonResult.IsOutside)
                    return true;
        }
        return false;
    }
}
