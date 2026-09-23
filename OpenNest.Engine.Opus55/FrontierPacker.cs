using Clipper2Lib;
using OpenNest.Engine.Jobs;
using OpenNest.Geometry;

namespace OpenNest.Engine.Opus55;

/// <summary>Direction the packing front sweeps across the sheet (the free strip is left behind it).</summary>
internal enum PackAxis
{
    /// <summary>Front moves in +X; parts settle toward low X, then low Y.</summary>
    X,

    /// <summary>Front moves in +Y; parts settle toward low Y, then low X.</summary>
    Y,
}

internal sealed record Placed(Orientation Orientation, double X, double Y)
{
    public double Left => X + Orientation.MinX;
    public double Right => X + Orientation.MaxX;
    public double Bottom => Y + Orientation.MinY;
    public double Top => Y + Orientation.MaxY;
}

internal sealed record SheetFill(NestPlateStock Stock, IReadOnlyList<Placed> Parts, double PartArea);

/// <summary>
/// Fills one sheet with a frontier-advance rule over incrementally maintained free regions.
///
/// For every (part type, orientation) still in play the packer keeps the exact set of legal
/// reference points: the inner-fit rectangle of the work area minus the no-fit polygons of
/// everything already placed. Each placement subtracts one translated NFP from each region,
/// so regions only shrink, and a region that empties is retired for the rest of the sheet.
///
/// Choice rule, applied over all types and orientations at once (not in a fixed order):
///   1. Gap fill - if any part fits without pushing the packing front forward, place the
///      largest such part at its lowest such point.
///   2. Otherwise advance - place the part whose front advance per unit area^beta is smallest,
///      i.e. the one that buys the most material coverage for the sheet length it consumes.
/// Parts are never placed in a sequence given up front; the sheet state decides what comes next.
/// </summary>
internal sealed class FrontierPacker
{
    /// <summary>Slack added around the inner-fit rectangle so zero-width fits survive Clipper;
    /// chosen points are clamped back, which moves them far less than the clearance margin.</summary>
    private const double FitSlack = 2e-4;

    private const double Tie = 1e-6;

    private readonly IReadOnlyList<PartType> types;
    private readonly NoFitCache nfps;
    private readonly NestPlateStock stock;
    private readonly PackAxis axis;
    private readonly double beta;
    private readonly Box work;
    private readonly WorkCounter counter;

    public FrontierPacker(IReadOnlyList<PartType> types, NoFitCache nfps, NestPlateStock stock, PackAxis axis, double beta, WorkCounter counter)
    {
        this.counter = counter;
        this.types = types;
        this.nfps = nfps;
        this.stock = stock;
        this.axis = axis;
        this.beta = beta;
        work = WorkArea(stock);
    }

    public static Box WorkArea(NestPlateStock stock)
    {
        var left = stock.Quadrant is 1 or 4 ? 0 : -stock.Size.Length;
        var bottom = stock.Quadrant is 1 or 2 ? 0 : -stock.Size.Width;
        return new Box(
            left + stock.EdgeSpacing.Left,
            bottom + stock.EdgeSpacing.Bottom,
            stock.Size.Length - stock.EdgeSpacing.Left - stock.EdgeSpacing.Right,
            stock.Size.Width - stock.EdgeSpacing.Bottom - stock.EdgeSpacing.Top
        );
    }

    /// <summary>True when the orientation's bounds fit the work area at all (Box.Length is the X extent).</summary>
    public static bool Fits(Orientation o, Box work) =>
        o.Width <= work.Length + 1e-9 && o.Height <= work.Width + 1e-9;

    public SheetFill Fill(IReadOnlyList<int> remaining, CancellationToken token)
    {
        var left = remaining.ToArray();
        var states = new List<Region>();
        foreach (var type in types)
        {
            if (left[type.Index] <= 0)
                continue;
            foreach (var o in type.Orientations)
                if (Fits(o, work))
                    states.Add(new Region(o, work));
        }

        var placed = new List<Placed>();
        var partArea = 0.0;
        var front = axis == PackAxis.X ? work.Left : work.Bottom;

        while (states.Count > 0)
        {
            token.ThrowIfCancellationRequested();
            var choice = Choose(states, front);
            if (choice == null)
                break;

            var (region, point) = choice.Value;
            var part = new Placed(region.Orientation, point.x, point.y);
            placed.Add(part);
            var typeIndex = region.Orientation.TypeIndex;
            partArea += types[typeIndex].Area;
            front = System.Math.Max(front, axis == PackAxis.X ? part.Right : part.Top);

            if (--left[typeIndex] == 0)
                states.RemoveAll(s => s.Orientation.TypeIndex == typeIndex);

            // Each surviving region loses the positions the new part now blocks. Regions are
            // independent, so they update in parallel without affecting determinism.
            var snapshot = states.ToArray();
            counter.Add(snapshot.Length);
            Parallel.For(
                0,
                snapshot.Length,
                new ParallelOptions { CancellationToken = token },
                i => snapshot[i].Subtract(nfps.Get(part.Orientation, snapshot[i].Orientation), part.X, part.Y)
            );
            states.RemoveAll(s => s.IsEmpty);
        }

        return new SheetFill(stock, placed, partArea);
    }

    private (Region, PointD)? Choose(List<Region> states, double front)
    {
        Region? bestRegion = null;
        var bestPoint = default(PointD);
        var bestFills = false;
        var bestValue = double.PositiveInfinity;
        var bestSide = double.PositiveInfinity;
        var bestLead = double.PositiveInfinity;

        foreach (var region in states)
        {
            if (!region.TryLowest(axis, front, out var point, out var advance, out var side, out var lead))
                continue;
            var area = types[region.Orientation.TypeIndex].Area;
            var fills = advance <= Tie;
            // Gap fill prefers bigger parts (negated area); advance prefers least advance per area.
            var value = fills ? -area : advance / System.Math.Pow(System.Math.Max(area, 1e-12), beta);

            var better = bestRegion == null
                || (fills && !bestFills)
                || (
                    fills == bestFills
                    && (
                        value < bestValue - Tie * System.Math.Max(1, System.Math.Abs(bestValue))
                        || (
                            value <= bestValue + Tie * System.Math.Max(1, System.Math.Abs(bestValue))
                            && (side < bestSide - Tie || (side <= bestSide + Tie && lead < bestLead - Tie))
                        )
                    )
                );
            if (!better)
                continue;
            bestRegion = region;
            bestPoint = point;
            bestFills = fills;
            bestValue = value;
            bestSide = side;
            bestLead = lead;
        }

        return bestRegion == null ? null : (bestRegion, bestPoint);
    }

    /// <summary>Legal reference points for one orientation on this sheet.</summary>
    private sealed class Region
    {
        private readonly double minX, minY, maxX, maxY;
        private PathsD free;
        private RectD bounds;

        public Region(Orientation orientation, Box work)
        {
            Orientation = orientation;
            minX = work.Left - orientation.MinX;
            maxX = work.Right - orientation.MaxX;
            minY = work.Bottom - orientation.MinY;
            maxY = work.Top - orientation.MaxY;
            // Guard against fits that are infeasible by less than the bounds tolerance.
            if (maxX < minX)
                maxX = minX;
            if (maxY < minY)
                maxY = minY;
            free = new PathsD
            {
                new PathD
                {
                    new(minX - FitSlack, minY - FitSlack),
                    new(maxX + FitSlack, minY - FitSlack),
                    new(maxX + FitSlack, maxY + FitSlack),
                    new(minX - FitSlack, maxY + FitSlack),
                },
            };
            bounds = Clipper.GetBounds(free);
        }

        public Orientation Orientation { get; }
        public bool IsEmpty => free.Count == 0;

        public void Subtract(Nfp nfp, double dx, double dy)
        {
            if (
                nfp.Bounds.right + dx < bounds.left
                || nfp.Bounds.left + dx > bounds.right
                || nfp.Bounds.bottom + dy < bounds.top
                || nfp.Bounds.top + dy > bounds.bottom
            )
                return;
            var clip = Clipper.TranslatePaths(nfp.Region, dx, dy);
            free = Clipper.Difference(free, clip, FillRule.NonZero, NoFitCache.Precision);
            // Drop numerical dust; a sliver thinner than the precision grid is no real room.
            free.RemoveAll(p => p.Count < 3);
            bounds = free.Count == 0 ? default : Clipper.GetBounds(free);
        }

        /// <summary>
        /// Best vertex of the free region: least front advance, then lowest cross-axis position,
        /// then lowest leading edge. Vertices suffice because every score is linear in position.
        /// </summary>
        public bool TryLowest(PackAxis axis, double front, out PointD point, out double advance, out double side, out double lead)
        {
            point = default;
            advance = side = lead = double.PositiveInfinity;
            var found = false;
            var o = Orientation;
            foreach (var path in free)
                foreach (var raw in path)
                {
                    var x = System.Math.Clamp(raw.x, minX, maxX);
                    var y = System.Math.Clamp(raw.y, minY, maxY);
                    double reach, across, start;
                    if (axis == PackAxis.X)
                    {
                        reach = x + o.MaxX;
                        across = y + o.MinY;
                        start = x + o.MinX;
                    }
                    else
                    {
                        reach = y + o.MaxY;
                        across = x + o.MinX;
                        start = y + o.MinY;
                    }
                    var adv = System.Math.Max(0, reach - front);
                    var better = !found
                        || adv < advance - Tie
                        || (adv <= advance + Tie && (across < side - Tie || (across <= side + Tie && start < lead - Tie)));
                    if (!better)
                        continue;
                    found = true;
                    point = new PointD(x, y);
                    advance = adv;
                    side = across;
                    lead = start;
                }
            return found;
        }
    }
}
