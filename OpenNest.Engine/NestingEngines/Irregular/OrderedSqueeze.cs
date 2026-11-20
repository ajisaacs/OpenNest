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
/// Shortens one sheet: packs its parts again inside a strip that ends short of where they reach
/// now, and keeps shortening the strip while they still all fit.
///
/// The frontier packer lets the sheet state pick the next part, and its rule (least front advance
/// per area) sends small parts first, so a part as long as the layout can end up behind them and
/// set the sheet's length. Here the parts go in a given order instead, largest first to begin
/// with, each at the lowest point any of its orientations reaches across the strip (then nearest
/// the strip's start), the way a sheet is laid out by hand: the big parts along one edge, the
/// rest stacked on them, the small ones in the gaps. A part that does not fit is left out, and a
/// local search over the order (move one part to another position, best move first; then a few
/// seeded swaps from the best order so far) looks for an order that leaves none out. Each strip
/// that takes them all is followed by a shorter one, starting from the order that worked.
///
/// Every position is a vertex of an exact free region (inner-fit rectangle less the no-fit
/// polygons of clamps and placed parts), as in <see cref="FrontierPacker"/>. Decodes sharing the
/// first parts of an order share those placements. Work is counted, not timed, so the result is
/// deterministic; the swaps come from a fixed seed.
/// </summary>
internal sealed class OrderedSqueeze(
    IReadOnlyList<PartType> types,
    NoFitCache nfps,
    NestPlateStock stock,
    ClampObstacles? clamps,
    WorkCounter counter,
    Func<PartType, IReadOnlyList<Orientation>> orientations)
{
    /// <summary>Most parts a sheet may hold to be squeezed: a decode costs about their square.</summary>
    internal const int MaxParts = 30;

    /// <summary>How much shorter each strip ends, as a share of the work area along the strip.</summary>
    internal const double Step = 0.005;

    /// <summary>Work one strip length may spend looking for an order that fits.</summary>
    internal const long TryWork = 600_000;

    /// <summary>Fewest parts whose search gets all of <see cref="TryWork"/>.</summary>
    private const int FullSearchParts = 11;

    /// <summary>Work a whole squeeze may spend.</summary>
    internal const long MaxWork = 3_000_000;

    /// <summary>Seeded swaps applied to the best order so far when a local search stalls.</summary>
    private const int Kicks = 3;

    /// <summary>Rounds in a row, per part, that place nothing new before a search gives up.</summary>
    private const int IdleRounds = 2;

    /// <summary>Same slack as the frontier packer's inner-fit rectangle.</summary>
    private const double FitSlack = 2e-4;

    private const double Tie = 1e-6;

    /// <summary>
    /// The parts of <paramref name="current"/> packed into the shortest strip along
    /// <paramref name="axis"/> found, or null when no strip shorter than they reach now takes them all.
    /// </summary>
    public SheetFill? Fill(IReadOnlyList<Placed> current, PackAxis axis, CancellationToken token)
    {
        if (current.Count < 2 || current.Count > MaxParts || stock.Outline != null)
            return null;
        var work = stock.WorkArea;
        var start = axis == PackAxis.X ? work.Left : work.Bottom;
        var across = axis == PackAxis.X ? work.Width : work.Length;
        var step = Step * (axis == PackAxis.X ? work.Length : work.Width);
        if (!(step > 0))
            return null;

        var order = current.Select(p => p.Orientation.TypeIndex)
            .OrderByDescending(t => types[t].Area).ThenBy(t => t).ToList();
        var area = order.Sum(t => types[t].Area);
        var random = new Random(1);
        var limit = counter.Value + MaxWork;
        var tryWork = TryBudget(current.Count);
        var reach = Extent(current, axis) - step;
        List<Placed>? best = null;
        // A strip with less room than the parts' area holds no order of them.
        while (counter.Value < limit && (reach - start) * across >= area)
        {
            token.ThrowIfCancellationRequested();
            var strip = axis == PackAxis.X
                ? new Box(work.Left, work.Bottom, reach - work.Left, work.Width)
                : new Box(work.Left, work.Bottom, work.Length, reach - work.Bottom);
            var found = Search(order, strip, axis, area, random, System.Math.Min(limit, counter.Value + tryWork), token);
            if (found is not var (parts, fitted))
                break;
            best = parts;
            order = fitted;
            reach = Extent(parts, axis) - step;
        }
        return best == null ? null : new SheetFill(stock, best, area);
    }

    /// <summary>
    /// Work one strip length may spend on <paramref name="parts"/> parts. A round of single-part
    /// moves decodes about n^2 orders of n parts, each placed against up to n others, so the same
    /// number of rounds costs fewer parts a fourth power less.
    /// </summary>
    internal static long TryBudget(int parts)
    {
        var share = System.Math.Min(1.0, (double)parts / FullSearchParts);
        return System.Math.Max(1, (long)(TryWork * share * share * share * share));
    }

    private static double Extent(IReadOnlyList<Placed> parts, PackAxis axis) =>
        axis == PackAxis.X ? parts.Max(p => p.Right) : parts.Max(p => p.Top);

    /// <summary>An order's decode, ranked by the part area left out, then by how low the placed
    /// parts sit across the strip (their area-weighted center).</summary>
    private readonly record struct Score(double Unplaced, double Moment)
    {
        public bool Beats(Score other, double scale) =>
            Unplaced < other.Unplaced - 1e-9 * scale
            || (Unplaced <= other.Unplaced + 1e-9 * scale && Moment < other.Moment - 1e-9 * System.Math.Max(1, System.Math.Abs(other.Moment)));
    }

    /// <summary>
    /// Looks for an order that fits every part in <paramref name="strip"/>: from
    /// <paramref name="seed"/>, the best single-part move while one improves the decode, then the
    /// best order so far with a few seeded swaps, until <paramref name="limit"/> work is spent.
    /// </summary>
    private (List<Placed> Parts, List<int> Order)? Search(List<int> seed, Box strip, PackAxis axis, double area,
        Random random, long limit, CancellationToken token)
    {
        var decoder = new Decoder(types, (type, placed) => Place(type, placed, strip, axis, token), axis);
        bool Fits(Score score) => score.Unplaced <= 1e-9 * area;
        var order = seed.ToList();
        var bestOrder = order;
        Score? bestScore = null;
        var idle = 0;
        while (true)
        {
            token.ThrowIfCancellationRequested();
            var known = decoder.Placements;
            var score = decoder.Decode(order, area, out var placed);
            while (!Fits(score) && counter.Value < limit)
            {
                var move = score;
                List<int>? moved = null;
                List<Placed>? movedParts = null;
                for (var from = 0; from < order.Count && !Fits(move) && counter.Value < limit; from++)
                    for (var to = 0; to < order.Count && counter.Value < limit; to++)
                    {
                        // One step back is the same order as moving the neighbor one step forward.
                        if (to == from || to == from - 1)
                            continue;
                        token.ThrowIfCancellationRequested();
                        var next = order.ToList();
                        next.RemoveAt(from);
                        next.Insert(to, order[from]);
                        // Copies of one part trade places without changing the order.
                        if (next.SequenceEqual(order))
                            continue;
                        var result = decoder.Decode(next, area, out var parts);
                        if (!result.Beats(move, area))
                            continue;
                        move = result;
                        moved = next;
                        movedParts = parts;
                        if (Fits(move))
                            break;
                    }
                if (moved == null)
                    break;
                order = moved;
                score = move;
                placed = movedParts!;
            }
            if (Fits(score))
                return (placed, order);
            if (counter.Value >= limit)
                return null;
            // Orders already decoded cost no work, so the budget alone never ends a search that
            // has run out of new ones: a few parts have only a few orders. Stop once the swaps
            // keep coming back to placements already made.
            idle = decoder.Placements == known ? idle + 1 : 0;
            if (idle >= IdleRounds * order.Count)
                return null;
            if (bestScore is not { } incumbent || score.Beats(incumbent, area))
            {
                bestScore = score;
                bestOrder = order;
            }
            order = bestOrder.ToList();
            for (var kick = 0; kick < Kicks; kick++)
            {
                var a = random.Next(order.Count);
                var b = random.Next(order.Count);
                (order[a], order[b]) = (order[b], order[a]);
            }
        }
    }

    /// <summary>
    /// Places an order's parts one after another in a strip. A placement depends only on the
    /// parts before it, so the decodes of one strip share a tree of order prefixes.
    /// </summary>
    private sealed class Decoder(IReadOnlyList<PartType> types, Func<PartType, IReadOnlyList<Placed>, Placed?> place, PackAxis axis)
    {
        private sealed class Node
        {
            /// <summary>Null when the part fits nowhere after its prefix.</summary>
            public Placed? Pose;
            public Dictionary<int, Node>? Next;
        }

        private readonly Node root = new();

        /// <summary>Placements worked out so far; a decode that adds none met only known prefixes.</summary>
        public int Placements { get; private set; }

        public Score Decode(IReadOnlyList<int> order, double area, out List<Placed> placed)
        {
            placed = new List<Placed>(order.Count);
            var node = root;
            double placedArea = 0, moment = 0;
            foreach (var type in order)
            {
                node.Next ??= new Dictionary<int, Node>();
                if (!node.Next.TryGetValue(type, out var next))
                {
                    node.Next[type] = next = new Node { Pose = place(types[type], placed) };
                    Placements++;
                }
                node = next;
                if (next.Pose is not { } pose)
                    continue;
                placed.Add(pose);
                var weight = types[type].Area;
                placedArea += weight;
                moment += weight * (axis == PackAxis.X ? (pose.Bottom + pose.Top) / 2 : (pose.Left + pose.Right) / 2);
            }
            return new Score(area - placedArea, moment);
        }
    }

    /// <summary>
    /// The pose of <paramref name="type"/> that ends lowest across the strip, over all its
    /// orientations: then nearest the strip's start, then starting lowest. Null when none fits.
    /// </summary>
    private Placed? Place(PartType type, IReadOnlyList<Placed> placed, Box strip, PackAxis axis, CancellationToken token)
    {
        var poses = orientations(type).Where(o => o.Width <= strip.Length + 1e-9 && o.Height <= strip.Width + 1e-9).ToList();
        var clampCount = clamps?.Count ?? 0;
        // A part too long for the strip still costs its lookup, so every new placement is counted.
        counter.Add((long)System.Math.Max(1, poses.Count) * System.Math.Max(1, placed.Count + clampCount));
        if (poses.Count == 0)
            return null;
        var results = new (Placed? Pose, double Top, double Side, double Foot)[poses.Count];
        // Orientations are independent; combining them in catalog order keeps the choice deterministic.
        Parallel.For(0, poses.Count, new ParallelOptions { CancellationToken = token }, index =>
        {
            var o = poses[index];
            var minX = strip.Left - o.MinX;
            var maxX = System.Math.Max(minX, strip.Right - o.MaxX);
            var minY = strip.Bottom - o.MinY;
            var maxY = System.Math.Max(minY, strip.Top - o.MaxY);
            var free = new PathsD
            {
                new PathD
                {
                    new(minX - FitSlack, minY - FitSlack),
                    new(maxX + FitSlack, minY - FitSlack),
                    new(maxX + FitSlack, maxY + FitSlack),
                    new(minX - FitSlack, maxY + FitSlack),
                },
            };
            // One difference against every blocker at once: the same region as subtracting them
            // in turn, at a fraction of the cost.
            var blocked = new PathsD();
            void Block(Nfp nfp, double dx, double dy)
            {
                if (nfp.Bounds.right + dx < minX - FitSlack || nfp.Bounds.left + dx > maxX + FitSlack
                    || nfp.Bounds.bottom + dy < minY - FitSlack || nfp.Bounds.top + dy > maxY + FitSlack)
                    return;
                blocked.AddRange(dx == 0 && dy == 0 ? nfp.Region : Clipper.TranslatePaths(nfp.Region, dx, dy));
            }
            for (var i = 0; i < clampCount; i++)
                Block(clamps!.Get(i, o), 0, 0);
            foreach (var part in placed)
                Block(nfps.Get(part.Orientation, o), part.X, part.Y);
            if (blocked.Count > 0)
                free = Clipper.Difference(free, blocked, FillRule.NonZero, NoFitCache.Precision);

            Placed? best = null;
            double bestTop = double.PositiveInfinity, bestSide = double.PositiveInfinity, bestFoot = double.PositiveInfinity;
            foreach (var path in free)
            {
                // A sliver thinner than the precision grid is no real room.
                if (path.Count < 3)
                    continue;
                foreach (var raw in path)
                {
                    var x = System.Math.Clamp(raw.x, minX, maxX);
                    var y = System.Math.Clamp(raw.y, minY, maxY);
                    double top, side, foot;
                    if (axis == PackAxis.X)
                    {
                        top = y + o.MaxY;
                        side = x + o.MinX;
                        foot = y + o.MinY;
                    }
                    else
                    {
                        top = x + o.MaxX;
                        side = y + o.MinY;
                        foot = x + o.MinX;
                    }
                    if (best != null && !Lower(top, side, foot, bestTop, bestSide, bestFoot))
                        continue;
                    best = new Placed(o, x, y);
                    bestTop = top;
                    bestSide = side;
                    bestFoot = foot;
                }
            }
            results[index] = (best, bestTop, bestSide, bestFoot);
        });

        (Placed? Pose, double Top, double Side, double Foot) chosen = default;
        foreach (var result in results)
            if (result.Pose != null
                && (chosen.Pose == null || Lower(result.Top, result.Side, result.Foot, chosen.Top, chosen.Side, chosen.Foot)))
                chosen = result;
        return chosen.Pose;
    }

    private static bool Lower(double top, double side, double foot, double bestTop, double bestSide, double bestFoot) =>
        top < bestTop - Tie
        || (top <= bestTop + Tie && (side < bestSide - Tie || (side <= bestSide + Tie && foot < bestFoot - Tie)));
}
