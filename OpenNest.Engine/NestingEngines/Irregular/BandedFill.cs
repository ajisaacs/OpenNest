#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Clipper2Lib;
using OpenNest.Engine.Jobs;
using OpenNest.Geometry;

namespace OpenNest.Engine.NestingEngines.Irregular;

/// <summary>
/// Fills one sheet in full-height strips along its length, two ways.
///
/// <see cref="Fill"/> lays each like-part group out as a band: the group's Lattice pattern (the one
/// interactive Fill gives that part alone, so a round part stacks, a hexagon tiles and an L
/// interlocks with its pair) in the narrowest strip that holds all its copies, or a few rows lower
/// to leave room along the sheet. Bands stand side by side, then the copies held back from them
/// and the types too few to band fill the gaps.
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

    /// <summary>Narrower bands offered per type, those holding the most copies first.</summary>
    private const int MaxNarrower = 2;

    /// <summary>Lengths a full-height band's search steps through.</summary>
    private const int MaxSteps = 6;

    /// <summary>Halvings toward the area bound that follow a full-height band's steps.</summary>
    private const int Probes = 3;

    /// <summary>How far short of the last pattern's length or height the next one is asked for.</summary>
    private const double StepShort = 0.01;

    /// <summary>Lower band heights searched per type.</summary>
    private const int LowerHeights = 3;

    /// <summary>Lengths a lower band's search steps through; it makes no probes.</summary>
    private const int LowerSteps = 3;

    /// <summary>Partial sheets carried strip to strip.</summary>
    private const int Beam = 4;

    /// <summary>Most copies across in a strip packed as a column of its largest part.</summary>
    private const int MaxColumnCopies = 3;

    /// <summary>Column strips whose widths are closer than this are tried once.</summary>
    private const double SameWidth = 0.05;

    /// <summary>Lengths and areas closer than this are the same.</summary>
    private const double Same = 1e-6;

    /// <summary>Slack when testing that a pattern fits the room it is given.</summary>
    private const double FitSlack = 1e-9;

    /// <summary>A type's pattern. <c>Height</c> is the height of the box it was asked for, so a
    /// band cut short at the sheet's end is asked for in the same box.</summary>
    private sealed record Band(int Type, int Copies, double Length, double Height, IReadOnlyList<Placed> Members);

    /// <summary>A partial sheet: the parts placed so far, the copies left per type, the part area
    /// placed and how far along the sheet the strips reach.</summary>
    private sealed record StripState(List<Placed> Placed, int[] Rest, double Area, double Front);

    /// <summary>One <see cref="FillStrips"/> call: the packer for a strip's gaps, how far behind a
    /// strip its gap fill looks and the band searches made so far.</summary>
    private sealed record StripRun(Func<Box, FrontierPacker> Within, double LookBack, CancellationToken Token)
    {
        public Dictionary<(int Type, int Copies), List<Band>> Bands { get; } = new();
    }

    private readonly Box region = stock.WorkArea;

    private readonly double spacing = System.Math.Max(0, stock.PartSpacing);

    /// <summary>The work area's height with a side carrying clamps moved in past their keep-outs:
    /// a band seated there clears the clamps whole.</summary>
    private readonly (double Bottom, double Top) clear = ClearHeight(stock, clamps);

    private static (double Bottom, double Top) ClearHeight(NestPlateStock stock, ClampObstacles? clamps)
    {
        var (area, onEdges) = ClampObstacles.ClearOfEdges(stock.WorkArea, clamps);
        return onEdges ? (area.Bottom, area.Top) : (stock.WorkArea.Bottom, stock.WorkArea.Top);
    }

    /// <summary>The fill, or null when nothing bands on this sheet.</summary>
    public SheetFill? Fill(IReadOnlyList<int> remaining, CancellationToken token)
    {
        if (stock.Outline != null)
            return null;
        var options = BandOptionsByType(remaining, token);
        if (options.Count == 0)
            return null;
        var (bands, best) = BestOrder(options, remaining, token);
        return HoldBack(bands, best, options, remaining, token);
    }

    /// <summary>
    /// <see cref="BandOptions"/> of each type with copies enough to band, the types searched at
    /// once. Each type's search depends only on its own patterns (fixed step counts, no shared
    /// budget), so the result is the same in any order. Types with no band are left out.
    /// </summary>
    private Dictionary<int, List<Band>> BandOptionsByType(IReadOnlyList<int> remaining, CancellationToken token)
    {
        var options = new Dictionary<int, List<Band>>();
        var grouped = types.Where(t => remaining[t.Index] >= GroupedFill.MinGroup && t.Orientations.Count > 0)
            .Select(t => t.Index).ToArray();
        if (grouped.Length == 0)
            return options;
        var found = new List<Band>[grouped.Length];
        Parallel.For(0, grouped.Length, new ParallelOptions { CancellationToken = token },
            i => found[i] = BandOptions(grouped[i], remaining[grouped[i]], token));
        for (var i = 0; i < grouped.Length; i++)
            if (found[i].Count > 0)
                options[grouped[i]] = found[i];
        return options;
    }

    /// <summary>Every type's narrowest full band in the order that packs best: longest first,
    /// shortest first or largest part first.</summary>
    private (Band[] Bands, SheetFill Fill) BestOrder(Dictionary<int, List<Band>> options,
        IReadOnlyList<int> remaining, CancellationToken token)
    {
        var full = options.Values.Select(o => o[0]).ToArray();
        var orders = new[]
        {
            full.OrderByDescending(b => b.Length).ThenBy(b => b.Type).ToArray(),
            full.OrderBy(b => b.Length).ThenBy(b => b.Type).ToArray(),
            // Largest part first: small parts stay last, as filler for the gaps.
            full.OrderByDescending(b => types[b.Type].Area).ThenBy(b => b.Type).ToArray(),
        };
        var bands = orders[0];
        var best = Run(bands, remaining, token);
        foreach (var order in orders.Skip(1))
        {
            var fill = Run(order, remaining, token);
            if (compare(fill, best) < 0)
            {
                best = fill;
                bands = order;
            }
        }
        return (bands, best);
    }

    /// <summary>Band by band, each of the type's other options (narrower bands holding fewer
    /// copies, lower bands) in its place, kept while that improves the sheet.</summary>
    private SheetFill HoldBack(Band[] bands, SheetFill best, Dictionary<int, List<Band>> options,
        IReadOnlyList<int> remaining, CancellationToken token)
    {
        for (var i = 0; i < bands.Length; i++)
        {
            var choices = options[bands[i].Type];
            for (var k = 1; k < choices.Count && counter.Value < budget; k++)
            {
                var trial = bands.ToArray();
                trial[i] = choices[k];
                var candidate = Run(trial, remaining, token);
                if (compare(candidate, best) < 0)
                {
                    best = candidate;
                    bands = trial;
                }
            }
        }
        return best;
    }

    /// <summary>The bands seated left to right in the order given, then everything else filling
    /// around them.</summary>
    private SheetFill Run(IReadOnlyList<Band> bands, IReadOnlyList<int> remaining, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var seated = new List<Placed>();
        var rest = remaining.ToArray();
        var front = region.Left;
        foreach (var chosen in bands)
        {
            var band = chosen;
            var room = region.Right - front;
            if (room <= 0)
                break;
            if (band.Length > room + FitSlack)
            {
                // The sheet ends inside this band: as many copies as the room left holds.
                var partial = Pattern(band.Type, band.Copies, room, band.Height, token);
                if (partial == null)
                    continue;
                band = partial;
            }
            var members = Seat(band, front);
            seated.AddRange(members);
            rest[band.Type] -= members.Count;
            foreach (var member in members)
                front = System.Math.Max(front, member.Right);
            front += spacing;
        }
        return Finish(seated, seated.Sum(p => types[p.Orientation.TypeIndex].Area), rest, token);
    }

    /// <summary>The sheet with the copies left over packed around the parts seated on it.</summary>
    private SheetFill Finish(IReadOnlyList<Placed> seated, double seatedArea, int[] rest, CancellationToken token)
    {
        var parts = seated.ToList();
        var partArea = seatedArea;
        if (rest.Any(r => r > 0))
        {
            var leftovers = packer().Fill(rest, token, seated);
            parts.AddRange(leftovers.Parts);
            partArea += leftovers.PartArea;
        }
        return new SheetFill(stock, parts, partArea);
    }

    /// <summary>Takes each part's copy off the count left of its type.</summary>
    private static void Take(int[] rest, IEnumerable<Placed> parts)
    {
        foreach (var part in parts)
            rest[part.Orientation.TypeIndex]--;
    }

    /// <summary>
    /// Strip by strip: each strip holds the largest remaining part, either as its Lattice band
    /// (full or holding fewer copies) or packed largest part first in a strip one to three copies
    /// wide, and every other part fills the strip's gaps, largest piece first. A beam of the
    /// <see cref="Beam"/> partial sheets densest so far (part area per unit of length used) is
    /// carried strip to strip, so a strip is judged by what it leaves for the rest of the sheet,
    /// not by its own density alone; the best finished sheet wins.
    /// </summary>
    public SheetFill? FillStrips(IReadOnlyList<int> remaining, Func<Box, FrontierPacker> within, CancellationToken token,
        int beamWidth = Beam)
    {
        if (stock.Outline != null)
            return null;
        var longestSide = types.Where(t => remaining[t.Index] > 0).SelectMany(t => t.Orientations)
            .Select(o => System.Math.Max(o.Width, o.Height)).DefaultIfEmpty(0).Max();
        var run = new StripRun(within, longestSide, token);
        var beam = new List<StripState> { new(new List<Placed>(), remaining.ToArray(), 0, region.Left) };
        var finished = new List<StripState>();
        while (beam.Count > 0)
        {
            token.ThrowIfCancellationRequested();
            Prefetch(SearchesAhead(beam, run), token);
            var children = new List<StripState>();
            foreach (var state in beam)
            {
                var next = NextStrips(state, run);
                if (next.Count == 0)
                    finished.Add(state);
                else
                    children.AddRange(next);
            }
            beam = Densest(children, beamWidth);
        }
        SheetFill? best = null;
        foreach (var state in finished)
        {
            var sheet = Finish(state.Placed, state.Area, state.Rest, token);
            if (best == null || compare(sheet, best) < 0)
                best = sheet;
        }
        return best;
    }

    /// <summary>The largest part with copies left, which the next strip holds.</summary>
    private PartType? Largest(int[] rest) => types.Where(t => rest[t.Index] > 0 && t.Orientations.Count > 0)
        .OrderByDescending(t => t.Area).ThenBy(t => t.Index).FirstOrDefault();

    /// <summary>The band searches the beam's next strips need and no strip has made yet, each
    /// once, for <see cref="Prefetch"/> to run ahead in parallel.</summary>
    private List<(int Type, int Copies)> SearchesAhead(List<StripState> beam, StripRun run)
    {
        var searches = new List<(int Type, int Copies)>();
        foreach (var state in beam)
        {
            var largest = Largest(state.Rest);
            if (largest == null || state.Front >= region.Right || state.Rest[largest.Index] < GroupedFill.MinGroup)
                continue;
            var search = (largest.Index, state.Rest[largest.Index]);
            if (!run.Bands.ContainsKey(search) && !searches.Contains(search))
                searches.Add(search);
        }
        return searches;
    }

    /// <summary>
    /// The partial sheets one strip past <paramref name="state"/>: its largest remaining part
    /// seated as each of its bands that fit, then packed as each column width. None when the
    /// sheet is finished: no part left, no room left, the work budget spent or no strip that
    /// takes a copy of the part.
    /// </summary>
    private List<StripState> NextStrips(StripState state, StripRun run)
    {
        var strips = new List<StripState>();
        var largest = Largest(state.Rest);
        if (largest == null || state.Front >= region.Right || counter.Value >= budget)
            return strips;
        var t = largest.Index;
        var start = state.Placed.Count == 0 ? state.Front : state.Front + spacing;
        void Offer(IReadOnlyList<Placed> seated, double end)
        {
            if (Strip(state, t, start, seated, end, run) is { } strip)
                strips.Add(strip);
        }
        if (state.Rest[t] >= GroupedFill.MinGroup)
            foreach (var band in StripBands(t, state.Rest[t], run))
            {
                if (band.Length > region.Right - start + FitSlack)
                    continue;
                var members = Seat(band, start);
                if (members.Count > 0)
                    Offer(members, members.Max(p => p.Right));
            }
        // The strip's own part fills its column first, then the others fill around it.
        foreach (var width in ColumnWidths(largest))
        {
            var reach = Reach(start + width);
            if (reach <= state.Front)
                continue;
            var own = new int[types.Count];
            own[t] = state.Rest[t];
            var column = Behind(start, reach, run).Fill(own, run.Token, state.Placed, region.Top);
            if (column.Parts.Count > 0)
                Offer(column.Parts, start + width);
        }
        return strips;
    }

    /// <summary>
    /// <paramref name="state"/> with one more strip, from <paramref name="start"/> to
    /// <paramref name="end"/>: the parts seated in it, then every part with copies left filling its
    /// gaps. Null when the strip ends behind the front or takes no copy of the
    /// <paramref name="largest"/> part.
    /// </summary>
    private StripState? Strip(StripState state, int largest, double start, IReadOnlyList<Placed> seated, double end,
        StripRun run)
    {
        var reach = Reach(end);
        if (reach <= state.Front)
            return null;
        var rest = state.Rest.ToArray();
        Take(rest, seated);
        var placed = state.Placed.Concat(seated).ToList();
        IReadOnlyList<Placed> gaps = rest.Any(r => r > 0)
            ? Behind(start, reach, run).Fill(rest, run.Token, placed, region.Top).Parts
            : Array.Empty<Placed>();
        var parts = seated.Concat(gaps).ToList();
        if (!parts.Any(p => p.Orientation.TypeIndex == largest))
            return null;
        Take(rest, gaps);
        placed.AddRange(gaps);
        return new StripState(placed, rest, state.Area + parts.Sum(p => types[p.Orientation.TypeIndex].Area),
            System.Math.Max(state.Front, parts.Max(p => p.Right)));
    }

    /// <summary>How far along the sheet a strip ending at <paramref name="end"/> is packed: just
    /// past its end, so a part ending there is inside.</summary>
    private double Reach(double end) => System.Math.Min(region.Right, end + Same);

    /// <summary>
    /// A packer for the strip from <paramref name="start"/> to <paramref name="reach"/>. It looks
    /// back one part length behind the strip: gaps further back were filled with earlier strips,
    /// and parts there cost nothing to skip.
    /// </summary>
    private FrontierPacker Behind(double start, double reach, StripRun run)
    {
        var left = System.Math.Max(region.Left, start - run.LookBack);
        return run.Within(new Box(left, region.Bottom, reach - left, region.Width));
    }

    /// <summary>The type's full-height bands for a strip, searched once per copy count.</summary>
    private List<Band> StripBands(int t, int copies, StripRun run)
    {
        if (!run.Bands.TryGetValue((t, copies), out var bands))
            run.Bands[(t, copies)] = bands = FullBands(t, copies, run.Token);
        return bands;
    }

    /// <summary>Strip widths of one to <see cref="MaxColumnCopies"/> copies of the part across,
    /// each distinct width once, narrowest first.</summary>
    private IEnumerable<double> ColumnWidths(PartType type)
    {
        var widths = new List<double>();
        foreach (var o in type.Orientations)
            for (var k = 1; k <= MaxColumnCopies; k++)
            {
                var width = k * o.Width + (k - 1) * spacing;
                if (!widths.Any(w => System.Math.Abs(w - width) < SameWidth))
                    widths.Add(width);
            }
        return widths.OrderBy(w => w);
    }

    /// <summary>The <paramref name="beamWidth"/> densest partial sheets (part area per unit of
    /// length used), the shorter first among equals and those alike kept once.</summary>
    private List<StripState> Densest(List<StripState> children, int beamWidth)
    {
        double Density(StripState s) => s.Area / System.Math.Max(s.Front - region.Left, 1e-9);
        var beam = new List<StripState>();
        foreach (var child in children.OrderByDescending(Density).ThenBy(c => c.Front))
        {
            if (beam.Count >= beamWidth)
                break;
            if (!beam.Any(b => Alike(b, child)))
                beam.Add(child);
        }
        return beam;
    }

    private static bool Alike(StripState a, StripState b) => System.Math.Abs(a.Front - b.Front) < Same
        && System.Math.Abs(a.Area - b.Area) < Same && a.Rest.SequenceEqual(b.Rest);

    /// <summary>A band's members starting at <paramref name="front"/>, flush to the bottom, or,
    /// when that leaves members on the clamps, flush to the top or just past the clamps along
    /// either side, whichever keeps the most off them; members on a clamp are left out.</summary>
    private List<Placed> Seat(Band band, double front)
    {
        var minX = band.Members.Min(p => p.Left);
        var minY = band.Members.Min(p => p.Bottom);
        var maxY = band.Members.Max(p => p.Top);
        List<Placed> Shifted(double dy) => band.Members
            .Select(m => new Placed(m.Orientation, m.X + front - minX, m.Y + dy)).Where(p => !OnClamp(p)).ToList();
        var lowest = region.Bottom - minY;
        var highest = region.Top - maxY;
        var members = Shifted(lowest);
        var tried = new List<double> { lowest };
        foreach (var dy in new[] { highest, clear.Bottom - minY, clear.Top - maxY })
        {
            if (members.Count == band.Members.Count)
                break;
            // Inside the work area, and a pose not tried yet.
            if (dy < lowest - FitSlack || dy > highest + FitSlack
                || tried.Any(d => System.Math.Abs(d - dy) < FitSlack))
                continue;
            tried.Add(dy);
            var seated = Shifted(dy);
            if (seated.Count > members.Count)
                members = seated;
        }
        return members;
    }

    /// <summary>
    /// The type's bands of full work-area height (<see cref="FullBands"/>), then up to
    /// <see cref="LowerHeights"/> lower ones, each asked for just short of the last pattern's
    /// height, so each is a row or so lower: a lower band can sit past a clamp rail whole, and
    /// leaves a strip along the sheet that other parts fill. Lower bands search fewer lengths.
    /// </summary>
    private List<Band> BandOptions(int t, int copies, CancellationToken token)
    {
        var options = FullBands(t, copies, token);
        var shortest = ShortestSide(types[t]);
        var last = options.FirstOrDefault();
        for (var i = 0; i < LowerHeights && last != null; i++)
        {
            var height = last.Members.Max(p => p.Top) - last.Members.Min(p => p.Bottom) - StepShort;
            if (height < shortest)
                break;
            var lower = Bands(t, copies, height, LowerSteps, 0, token);
            options.AddRange(lower);
            last = lower.FirstOrDefault();
        }
        return options;
    }

    /// <summary>The type's bands of full work-area height; see <see cref="Bands"/>.</summary>
    private List<Band> FullBands(int t, int copies, CancellationToken token, Drawing? prefetch = null) =>
        Bands(t, copies, region.Width, MaxSteps, Probes, token, prefetch);

    /// <summary>
    /// The type's bands of the given height, narrowest full one first, then each narrower one
    /// holding at most <see cref="HeldBack"/> fewer copies. Lattice patterns grow in whole rows or
    /// columns, so each next band is asked for just short of the last one's length, and a few
    /// halvings toward the area bound catch patterns the steps skip.
    /// </summary>
    private List<Band> Bands(int t, int copies, double height, int steps, int probes, CancellationToken token,
        Drawing? prefetch = null)
    {
        var fewest = copies - HeldBack(copies);
        var found = new List<Band>();
        // A band stays unless another holds as many copies in no more length.
        void Keep(Band band)
        {
            if (band.Copies < fewest || found.Any(b => b.Copies >= band.Copies && b.Length <= band.Length + Same))
                return;
            found.RemoveAll(b => b.Copies <= band.Copies && b.Length >= band.Length - Same);
            found.Add(band);
        }
        Band? NarrowestFull() => found.Where(b => b.Copies >= copies).OrderBy(b => b.Length).FirstOrDefault();

        var length = region.Length;
        for (var step = 0; step < steps; step++)
        {
            var band = Pattern(t, copies, length, height, token, prefetch);
            if (band == null || band.Copies < fewest)
                break;
            Keep(band);
            length = band.Length - StepShort;
        }
        var full = NarrowestFull();
        if (full == null)
            return new List<Band>();
        var lo = System.Math.Max(ShortestSide(types[t]), copies * types[t].Area / height);
        var hi = full.Length;
        for (var probe = 0; probe < probes && hi - lo > StepShort; probe++)
        {
            var mid = (lo + hi) / 2;
            var band = Pattern(t, copies, mid, height, token, prefetch);
            if (band != null)
                Keep(band);
            if (band != null && band.Copies >= copies)
                hi = band.Length;
            else
                lo = mid;
        }
        full = NarrowestFull()!;
        return found.Where(b => b.Copies < copies && b.Length < full.Length - Same).OrderByDescending(b => b.Copies)
            .Take(MaxNarrower).Prepend(full).ToList();
    }

    /// <summary>Most copies a narrower band may leave to the gap fill.</summary>
    private static int HeldBack(int copies) => System.Math.Max(MaxHeldBack, copies / 3);

    private static double ShortestSide(PartType type) =>
        type.Orientations.Min(o => System.Math.Min(o.Width, o.Height));

    /// <summary>
    /// Builds the patterns of each (type, copies) band search ahead of need, the searches in
    /// parallel, so the ordinary searches that follow find them cached. Each search fills its
    /// own drawing and charges no work, so the work meter, the orientations and every result
    /// read as without it.
    /// </summary>
    private void Prefetch(IReadOnlyList<(int Type, int Copies)> searches, CancellationToken token)
    {
        if (searches.Count < 2)
            return;
        Parallel.For(0, searches.Count, new ParallelOptions { CancellationToken = token }, i =>
        {
            var drawing = BlockCatalog.PrefetchDrawing(types[searches[i].Type]);
            try
            {
                FullBands(searches[i].Type, searches[i].Copies, token, drawing);
            }
            finally
            {
                BlockCatalog.ReleasePrefetch(drawing);
            }
        });
    }

    /// <summary>The type's Lattice pattern in a length x width box, up to the copies wanted; with a
    /// <paramref name="prefetch"/> drawing, built ahead of need (see <see cref="Prefetch"/>).</summary>
    private Band? Pattern(int t, int copies, double length, double width, CancellationToken token,
        Drawing? prefetch = null)
    {
        var members = blocks.Band(types[t], copies, new Box(0, 0, length, width), token, prefetch);
        // Bounds carry the arc flattening padding; a pattern they push past the box is no band.
        if (members.Count == 0 || members.Max(p => p.Top) - members.Min(p => p.Bottom) > width + FitSlack)
            return null;
        return new Band(t, members.Count, members.Max(p => p.Right) - members.Min(p => p.Left), width, members);
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
