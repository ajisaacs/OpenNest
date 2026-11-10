using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using OpenNest.Diagnostics;
using OpenNest.Geometry;
using OpenNest.Math;

namespace OpenNest.Engine.Fill
{
    /// <summary>
    /// Fix Spacing: clears the pairs the overlap check flags (shared material or a gap under the
    /// plate's part spacing) with the smallest moves it can find. One part of a flagged pair is
    /// pushed straight away from the other by just enough to reach the part spacing; any part that
    /// push crowds is pushed the same way by its own shortfall, and so on down the chain, so a
    /// fix can ripple through parts already locked in by their neighbors. Parts the chain never
    /// reaches stay put.
    /// <para>
    /// Both parts of the pair and sixteen directions are tried; the chain with the least total
    /// travel wins. A chain fails when it would push the pair's other part, a common-line member
    /// or a part past the work-area edge or into a clamp. When every one-sided chain fails, the
    /// opening is split: one part goes as far as its chain has room and the other goes the
    /// opposite way for the rest. Each chosen fix is checked with the overlap check itself and kept
    /// only when the plate's findings drop, so a fix never trades a spacing pair for an edge, clamp
    /// or new spacing finding. Cut-offs are not obstacles: the caller regenerates them.
    /// </para>
    /// </summary>
    public static class SpacingFixer
    {
        public sealed class Result
        {
            /// <summary>Flagged pairs (overlaps and spacing pairs) in scope before the fix.</summary>
            public int FlaggedPairs;

            /// <summary>Flagged pairs in scope after the fix; equals <see cref="FlaggedPairs"/>
            /// when nothing moved.</summary>
            public int RemainingPairs;

            /// <summary>Parts that moved, in plate order.</summary>
            public List<Part> MovedParts = new();

            /// <summary>Longest single part move.</summary>
            public double LargestMove;

            /// <summary>True when the run was canceled; no part was moved.</summary>
            public bool Canceled;

            /// <summary>True when the overlap check could not read every part or pair, so
            /// unseen problems may remain.</summary>
            public bool Incomplete;
        }

        /// <summary>How far past the spacing a push carries: beyond the check's slack, so a fixed
        /// pair reads as clear.</summary>
        private const double Margin = SpacingOutline.SpacingSlack;

        /// <summary>Candidate fixes checked with the overlap check per flagged pair.</summary>
        private const int PlansPerPair = 4;

        private const int DirectionCount = 16;


        /// <summary>
        /// Finds the plate's flagged pairs and pushes their parts apart to the part spacing.
        /// <paramref name="scope"/> limits the fix to pairs with at least one part in it; null or
        /// empty means the whole plate. Parts move only once every fix has been checked.
        /// </summary>
        public static Result Fix(Plate plate, IReadOnlyCollection<Part> scope = null,
            CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(plate);
            try
            {
                return FixCore(plate, scope, token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                return new Result { Canceled = true };
            }
        }

        private static Result FixCore(Plate plate, IReadOnlyCollection<Part> scope, CancellationToken token)
        {
            var result = new Result();
            var parts = plate.Parts.ToArray(); // report ids are positions in this array
            var inScope = scope is { Count: > 0 }
                ? new HashSet<Part>(scope, ReferenceEqualityComparer.Instance)
                : null;

            var cache = new OverlapMaterialCache();
            var entry = PlateOverlapAnalyzer.CapturePlate(plate, cache, token);
            var report = PlateOverlapAnalyzer.Analyze(entry, token);
            result.Incomplete = !report.IsComplete;

            var flagged = Flagged(report, parts, inScope);
            result.FlaggedPairs = result.RemainingPairs = flagged.Count;
            if (flagged.Count == 0)
                return result;

            // Overlaps first, then the tightest gaps: they need the longest pushes.
            var order = flagged.OrderBy(pair => pair.Gap).ToList();

            // Fast path: plan every pair on the search's own conservative checks and run the
            // overlap check once. It must find nothing new and fewer findings than at entry.
            var layout = new Layout(plate, parts, token);
            var committed = false;
            foreach (var (a, b, _) in order)
            {
                token.ThrowIfCancellationRequested();
                if (layout.Clear(a, b))
                    continue; // an earlier push already opened it
                if (layout.Plans(a, b, 1).FirstOrDefault() is { } plan)
                {
                    layout.Commit(plan);
                    committed = true;
                }
            }
            if (!committed)
                return result;
            var final = PlateOverlapAnalyzer.Analyze(Moved(entry, layout.Positions(null)), report, token);

            if (!OnlyFewer(report, final, parts))
            {
                // Something new appeared: redo the pairs one at a time, keeping only fixes the
                // overlap check rates better.
                layout = new Layout(plate, parts, token);
                final = report;
                var score = Score(report);
                foreach (var (a, b, _) in order)
                {
                    token.ThrowIfCancellationRequested();
                    if (layout.Clear(a, b))
                        continue;
                    foreach (var plan in layout.Plans(a, b, PlansPerPair))
                    {
                        token.ThrowIfCancellationRequested();
                        var after = PlateOverlapAnalyzer.Analyze(Moved(entry, layout.Positions(plan)), final, token);
                        if (Score(after) >= score)
                            continue;
                        layout.Commit(plan);
                        final = after;
                        score = Score(after);
                        break;
                    }
                }
                if (ReferenceEquals(final, report))
                    return result;
            }
            report = final;

            token.ThrowIfCancellationRequested();
            var positions = layout.Positions(null);
            for (var i = 0; i < parts.Length; i++)
            {
                var to = positions[i];
                if (to.X == parts[i].Location.X && to.Y == parts[i].Location.Y)
                    continue;
                result.LargestMove = System.Math.Max(result.LargestMove, to.DistanceTo(parts[i].Location));
                parts[i].Offset(to - parts[i].Location);
                result.MovedParts.Add(parts[i]);
            }
            result.RemainingPairs = Flagged(report, parts, inScope).Count;
            result.Incomplete = !report.IsComplete;
            return result;
        }

        /// <summary>Overlap and spacing pairs (by part position, lower first) with at least one
        /// part in scope; an overlap's gap reads as zero. Clamp findings carry a negative slot and
        /// are not part pairs.</summary>
        private static List<(int A, int B, double Gap)> Flagged(PlateOverlapReport report, Part[] parts,
            HashSet<Part> scope)
        {
            var pairs = new Dictionary<(int, int), double>();
            foreach (var pair in report.Pairs.Where(pair => pair.PartBId >= 0))
                pairs[(pair.PartAId, pair.PartBId)] = 0;
            foreach (var pair in report.SpacingPairs)
                pairs.TryAdd((pair.PartAId, pair.PartBId), pair.Gap);
            return pairs
                .Where(pair => scope == null || scope.Contains(parts[pair.Key.Item1])
                    || scope.Contains(parts[pair.Key.Item2]))
                .Select(pair => (pair.Key.Item1, pair.Key.Item2, pair.Value))
                .ToList();
        }

        /// <summary>True when <paramref name="after"/> has fewer findings than
        /// <paramref name="before"/> and none it did not have: no new pair, edge, clamp or
        /// unreadable finding.</summary>
        private static bool OnlyFewer(PlateOverlapReport before, PlateOverlapReport after, Part[] parts)
        {
            if (Score(after) >= Score(before)
                || after.EdgeFindings.Count > before.EdgeFindings.Count
                || after.ClampFindings.Count > before.ClampFindings.Count
                || after.Issues.Count > before.Issues.Count
                || after.SpacingIssues.Count > before.SpacingIssues.Count)
                return false;
            var entryPairs = Flagged(before, parts, null).Select(pair => (pair.A, pair.B)).ToHashSet();
            return Flagged(after, parts, null).All(pair => entryPairs.Contains((pair.A, pair.B)));
        }

        /// <summary>Everything the overlap check reports, so a fix cannot trade a spacing pair
        /// for an edge, clamp or unreadable finding.</summary>
        private static int Score(PlateOverlapReport report) =>
            report.Pairs.Count + report.SpacingPairs.Count + report.ClampFindings.Count
            + report.EdgeFindings.Count + report.Issues.Count + report.SpacingIssues.Count;

        /// <summary>The entry snapshot with every part at <paramref name="positions"/>.</summary>
        private static PlateOverlapSnapshot Moved(PlateOverlapSnapshot entry, Vector[] positions)
        {
            var captured = entry.Parts
                .Select(part => part.Location.X == positions[part.Id].X && part.Location.Y == positions[part.Id].Y
                    ? part
                    : part with { Location = positions[part.Id] })
                .ToList();
            return new PlateOverlapSnapshot(captured, entry.Issues.ToList(), entry.Clamps.ToList(),
                entry.CutOffClampFindings.ToList(), entry.PartSpacing, entry.CommonLinePairs, entry.Frame);
        }

        /// <summary>One candidate fix: each pushed part's displacement, and their total length.</summary>
        private sealed record Plan(Dictionary<int, Vector> Moves, double Travel);

        /// <summary>A part a chain starts from, pushed <see cref="Travel"/> along <see cref="Direction"/>.</summary>
        private readonly record struct Seed(int Part, Vector Direction, double Travel);

        /// <summary>
        /// Scratch poses and native contours for the push search. A pair may close to its required
        /// gap, never below its committed gap when that is already smaller. Contours are flattened
        /// coarser than the overlap check's; circumscribed, they understate gaps, so the search errs
        /// toward pushing slightly further, and the check confirms every plan.
        /// </summary>
        private sealed class Layout
        {
            /// <summary>Search flattening: understates a gap by at most twice this, under the margin.</summary>
            private const double SearchChord = SpacingOutline.SpacingSlack / 2;

            /// <summary>Rounding allowance when a gap is compared with one measured at another pose.</summary>
            private const double Rounding = 1e-7;

            /// <summary>How far a pair the fix is not opening may close below its floor: rounding
            /// when a gap is measured again at a moved pose, far under the check's slack. Without
            /// it, rows already at the spacing pass on nudges of a millionth.</summary>
            private const double Settle = 1e-5;

            /// <summary>A relaxation move this small is undone when the layout holds without it.</summary>
            private const double TinyMove = 1e-4;

            private readonly Part[] parts;
            private readonly Vector[] baseline;
            private readonly NativeContours[] local; // null: no material, or a cut-off
            private readonly bool[] locked;
            private readonly double spacing;
            private readonly IReadOnlyDictionary<(int, int), double> kerfs;
            private readonly Box workArea;
            private readonly IReadOnlyList<Box> clamps;
            private readonly CancellationToken token;
            private readonly Dictionary<(int, int), double> baselineGaps = new();
            private readonly NativeContours[] world; // committed-pose contours, filled on demand
            private readonly Vector[] directions;

            public Layout(Plate plate, Part[] parts, CancellationToken token)
            {
                this.parts = parts;
                this.token = token;
                spacing = System.Math.Max(0, plate.PartSpacing);
                kerfs = PartSpacingReview.CommonLinePairs(plate, parts);
                workArea = plate.WorkArea();
                clamps = Jobs.InteractiveClamps.ClearanceRectangles(plate);
                baseline = parts.Select(part => part.Location).ToArray();
                local = new NativeContours[parts.Length];
                world = new NativeContours[parts.Length];
                locked = new bool[parts.Length];
                var profiles = new Dictionary<(CNC.Program, long), ShapeProfile>();
                var contours = new Dictionary<ShapeProfile, NativeContours>(ReferenceEqualityComparer.Instance);
                for (var i = 0; i < parts.Length; i++)
                {
                    token.ThrowIfCancellationRequested();
                    var part = parts[i];
                    if (part.BaseDrawing == null || part.BaseDrawing.IsCutOff)
                        continue;
                    locked[i] = plate.CommonLineGroupOf(part) != null;
                    try
                    {
                        var profile = PartSpacingReview.Profile(part, profiles);
                        if (profile?.Perimeter == null)
                            continue;
                        if (!contours.TryGetValue(profile, out var shape))
                            contours[profile] = shape = NativeContours.Of(profile, Vector.Zero, SearchChord);
                        local[i] = shape;
                    }
                    catch (Exception exception) when (exception is ArgumentException or InvalidOperationException
                        or NotSupportedException or ArithmeticException)
                    {
                        // Unreadable material: the overlap check reports it; it neither moves nor blocks.
                    }
                }
                directions = Enumerable.Range(0, DirectionCount)
                    .Select(k => 2 * System.Math.PI * k / DirectionCount)
                    .Select(angle => new Vector(PortableMath.Cos(angle), PortableMath.Sin(angle)))
                    .ToArray();
            }

            /// <summary>Every part's position with <paramref name="plan"/> applied (null: none).</summary>
            public Vector[] Positions(Plan plan)
            {
                var positions = (Vector[])baseline.Clone();
                if (plan != null)
                    foreach (var (index, move) in plan.Moves)
                        positions[index] = baseline[index] + move;
                return positions;
            }

            public void Commit(Plan plan)
            {
                foreach (var (index, move) in plan.Moves)
                {
                    baseline[index] += move;
                    world[index] = null;
                }
                baselineGaps.Clear();
            }

            /// <summary>True when the pair already meets its required gap at the committed layout.</summary>
            public bool Clear(int a, int b)
            {
                if (local[a] == null || local[b] == null)
                    return true;
                return Gap(At(a, baseline[a]), At(b, baseline[b]), Required(a, b))
                    >= Required(a, b) - SpacingOutline.SpacingSlack;
            }

            /// <summary>
            /// Candidate chains for one flagged pair, least total travel first. Each part of the
            /// pair is pushed alone in each direction first; when every such chain runs out of room,
            /// the opening is split: one part goes as far as its chain allows and the other part
            /// goes the opposite way for the rest.
            /// </summary>
            public IEnumerable<Plan> Plans(int a, int b, int limit)
            {
                if (local[a] == null || local[b] == null)
                    yield break;
                var required = Required(a, b);

                // The opening push bounds a chain's travel, so the cheapest openings are chained
                // first and the rest are pruned by the best chains found.
                var openings = new List<(int Mover, int Fixed, Vector Direction, double Travel)>();
                foreach (var (mover, other) in new[] { (b, a), (a, b) })
                {
                    if (locked[mover])
                        continue;
                    var fixedShape = At(other, baseline[other]);
                    foreach (var direction in directions)
                    {
                        token.ThrowIfCancellationRequested();
                        var travel = Smallest(mover, direction, 0, fixedShape, required + Margin);
                        if (!double.IsNaN(travel))
                            openings.Add((mover, other, direction, travel));
                    }
                }
                openings.Sort((x, y) => x.Travel.CompareTo(y.Travel));

                var plans = new List<Plan>();
                foreach (var opening in openings)
                {
                    token.ThrowIfCancellationRequested();
                    if (plans.Count >= limit && opening.Travel >= plans.Max(p => p.Travel))
                        break;
                    var plan = Chain(new[] { new Seed(opening.Mover, opening.Direction, opening.Travel) },
                        opening.Fixed);
                    if (plan != null)
                        plans.Add(plan);
                }

                // No straight push has room: let every crowded pair share its shortfall instead.
                if (plans.Count == 0)
                {
                    var plan = Relax(a, b);
                    if (plan != null)
                        plans.Add(plan);
                }

                foreach (var plan in plans.OrderBy(p => p.Travel).Take(limit))
                    yield return plan;
            }

            /// <summary>
            /// Opens the pair by constraint relaxation, for layouts where every straight chain runs
            /// into the edge: each crowded pair is pushed apart along the normal between its closest
            /// points, the shortfall shared equally (a common-line member or a part held at the
            /// work-area edge takes none), and passes repeat until every gap holds. A part moves only
            /// once something crowds it, so the moves stay local and may differ in direction. Null
            /// when it does not settle, or when a pair touches and has no normal.
            /// </summary>
            private Plan Relax(int a, int b)
            {
                const int Passes = 400;
                var pairTarget = Required(a, b) + Margin;
                var positions = (Vector[])baseline.Clone();
                var shapes = new NativeContours[parts.Length];
                var moved = new HashSet<int> { a, b };
                // A pair that held is measured again only after one of its parts moves.
                var version = new int[parts.Length];
                var held = new Dictionary<(int, int), (int, int)>();

                NativeContours Shape(int i) => shapes[i] ??= At(i, positions[i]);

                double Target(int i, int j) => (i == a && j == b) || (i == b && j == a)
                    ? pairTarget
                    : System.Math.Min(Required(i, j), BaselineGap(i, j)) - Settle;

                // Moves part i along the normal by up to the requested amount; returns how far it went.
                double Shift(int i, Vector normal, double amount)
                {
                    var from = positions[i];
                    var to = Hold(i, from + normal * amount);
                    if (to.X == from.X && to.Y == from.Y)
                        return 0;
                    positions[i] = to;
                    shapes[i] = null;
                    version[i]++;
                    moved.Add(i);
                    return (to.X - from.X) * normal.X + (to.Y - from.Y) * normal.Y;
                }

                // True when every gap around part i meets its target at the current poses.
                bool Holds(int i)
                {
                    for (var j = 0; j < parts.Length; j++)
                    {
                        if (j == i || local[j] == null)
                            continue;
                        var required = Required(i, j);
                        if (!Near(Shape(i).Bounds, Shape(j).Bounds, required + Margin))
                            continue;
                        if (Shape(i).Nested(Shape(j))
                            || Shape(i).GapTo(Shape(j), required + Margin, out _, out _) < Target(i, j) - Rounding)
                            return false;
                    }
                    return true;
                }

                for (var pass = 0; pass < Passes; pass++)
                {
                    token.ThrowIfCancellationRequested();
                    var corrected = false;
                    foreach (var i in moved.ToList())
                    {
                        for (var j = 0; j < parts.Length; j++)
                        {
                            if (j == i || local[j] == null)
                                continue;
                            var key = i < j ? (i, j) : (j, i);
                            if (held.TryGetValue(key, out var stamp) && stamp == (version[key.Item1], version[key.Item2]))
                                continue;
                            var required = Required(i, j);
                            if (!Near(Shape(i).Bounds, Shape(j).Bounds, required + Margin))
                                continue;
                            var target = Target(i, j);
                            var nested = Shape(i).Nested(Shape(j));
                            var gap = Shape(i).GapTo(Shape(j), required + Margin, out var onI, out var onJ);
                            if (gap >= target - Rounding && !nested)
                            {
                                held[key] = (version[key.Item1], version[key.Item2]);
                                continue;
                            }
                            if (gap <= 1e-6 || nested)
                                return null; // touching or overlapping: no direction to push
                            var movableI = Movable(i);
                            var movableJ = Movable(j);
                            if (!movableI && !movableJ)
                                return null;

                            // Share the shortfall; what one part cannot take (a common-line member,
                            // or a part at the edge) goes to the other at once.
                            var normal = (onI - onJ) * (1 / gap);
                            var shortfall = target - gap + 1e-6;
                            var shareI = movableI && movableJ ? shortfall / 2 : movableI ? shortfall : 0;
                            var wentI = movableI ? Shift(i, normal, shareI) : 0;
                            var wentJ = movableJ ? Shift(j, -normal, shortfall - wentI) : 0;
                            if (movableI && wentI + wentJ < shortfall - Rounding)
                                wentI += Shift(i, normal, shortfall - wentI - wentJ);
                            if (wentI + wentJ <= Rounding)
                                return null; // both held by the edge: the walls never give
                            corrected = true;
                        }
                    }
                    if (corrected)
                        continue;

                    // Settled. A part nudged by rounding-sized corrections goes back when every
                    // gap around it still holds there.
                    foreach (var i in moved.Where(i => i != a && i != b).ToList())
                    {
                        var move = positions[i] - baseline[i];
                        if (move.DistanceTo(Vector.Zero) >= TinyMove)
                            continue;
                        var at = positions[i];
                        positions[i] = baseline[i];
                        shapes[i] = null;
                        if (Holds(i))
                            continue;
                        positions[i] = at;
                        shapes[i] = null;
                    }

                    var moves = new Dictionary<int, Vector>();
                    foreach (var i in moved)
                    {
                        var move = positions[i] - baseline[i];
                        if (move.X == 0 && move.Y == 0)
                            continue;
                        if (!InBounds(i, Shape(i)))
                            return null;
                        moves[i] = move;
                    }
                    return moves.Count == 0 ? null : new Plan(moves, moves.Values.Sum(move => move.DistanceTo(Vector.Zero)));
                }
                return null;
            }

            /// <summary>False for a part that never moves: a common-line member or one without material.</summary>
            private bool Movable(int index) => !locked[index] && local[index] != null;

            /// <summary><paramref name="position"/> shifted back inside the work area, or no further
            /// outside than the part's committed pose.</summary>
            private Vector Hold(int index, Vector position)
            {
                var extent = local[index].Bounds;
                var committed = baseline[index];
                return new Vector(
                    Within(workArea.Left - extent.Left, workArea.Right - extent.Right, position.X, committed.X),
                    Within(workArea.Bottom - extent.Bottom, workArea.Top - extent.Top, position.Y, committed.Y));

                // A part already past an edge may stay where it was, never go further.
                static double Within(double low, double high, double at, double from)
                {
                    low = System.Math.Min(low, from);
                    high = System.Math.Max(high, from);
                    return low > high ? from : System.Math.Clamp(at, low, high);
                }
            }

            /// <summary>
            /// Pushes each seed part along its direction and every part that crowds a pushed part
            /// the same way by its own shortfall, down the chain. Null when the chain reaches
            /// <paramref name="fixedPart"/>, a locked part, a part already pushed another way, the
            /// work-area edge or a clamp.
            /// </summary>
            private Plan Chain(IReadOnlyList<Seed> seeds, int fixedPart)
            {
                var travel = new Dictionary<int, double>();
                var heading = new Dictionary<int, Vector>();
                var queue = new Queue<int>();
                foreach (var seed in seeds.Where(seed => seed.Travel > 0))
                {
                    travel[seed.Part] = seed.Travel;
                    heading[seed.Part] = seed.Direction;
                    queue.Enqueue(seed.Part);
                }

                var steps = 0;
                while (queue.Count > 0)
                {
                    token.ThrowIfCancellationRequested();
                    if (++steps > 4 * parts.Length)
                        return null;
                    var pushed = queue.Dequeue();
                    var direction = heading[pushed];
                    var shape = At(pushed, baseline[pushed] + direction * travel[pushed]);
                    if (!InBounds(pushed, shape))
                        return null;

                    for (var other = 0; other < parts.Length; other++)
                    {
                        if (other == pushed || local[other] == null)
                            continue;
                        var moved = heading.TryGetValue(other, out var otherHeading);
                        var otherTravel = moved ? travel[other] : 0;
                        var otherPosition = moved ? baseline[other] + otherHeading * otherTravel : baseline[other];
                        var required = Required(pushed, other);
                        // Bounds first: translating contours copies every flattened vertex.
                        var otherBounds = local[other].Bounds;
                        if (!Near(shape.Bounds, new Box(otherBounds.Left + otherPosition.X,
                                otherBounds.Bottom + otherPosition.Y, otherBounds.Length, otherBounds.Width),
                                required + Margin))
                            continue;
                        var floor = System.Math.Min(required, BaselineGap(pushed, other));
                        if (Gap(shape, At(other, otherPosition), required) >= floor - Settle)
                            continue;
                        if (other == fixedPart || locked[other]
                            || (moved && (otherHeading.X != direction.X || otherHeading.Y != direction.Y)))
                            return null;
                        var target = floor < required ? floor : required + Margin;
                        var further = Smallest(other, direction, otherTravel, shape, target);
                        if (double.IsNaN(further))
                            return null;
                        travel[other] = further;
                        heading[other] = direction;
                        queue.Enqueue(other);
                    }
                }
                return travel.Count == 0
                    ? null
                    : new Plan(travel.ToDictionary(move => move.Key, move => heading[move.Key] * move.Value),
                        travel.Values.Sum());
            }

            /// <summary>
            /// The least travel past <paramref name="from"/> along <paramref name="direction"/> at
            /// which <paramref name="part"/> sits at least <paramref name="target"/> from
            /// <paramref name="obstacle"/>; NaN when it is not reached within a few spacings.
            /// </summary>
            private double Smallest(int part, Vector direction, double from, NativeContours obstacle, double target)
            {
                bool Clears(double travel) =>
                    Gap(At(part, baseline[part] + direction * travel), obstacle, target) >= target - Rounding;

                var gap = Gap(At(part, baseline[part] + direction * from), obstacle, target);
                if (gap >= target - Rounding)
                    return from;
                // A translation changes a gap by at most its length.
                var lo = from;
                var step = System.Math.Max(target - System.Math.Max(0, gap), 1e-4);
                var cap = from + System.Math.Max(4 * target, 4 * step);
                var hi = from + step;
                while (!Clears(hi))
                {
                    lo = hi;
                    step *= 2;
                    hi = lo + step;
                    if (hi > cap)
                        return double.NaN;
                }
                while (hi - lo > 1e-5)
                {
                    var mid = (lo + hi) / 2;
                    if (Clears(mid))
                        hi = mid;
                    else
                        lo = mid;
                }
                return hi;
            }

            private double Required(int a, int b) =>
                kerfs.TryGetValue(a < b ? (a, b) : (b, a), out var kerf) ? System.Math.Min(kerf, spacing) : spacing;

            private double BaselineGap(int a, int b)
            {
                var key = a < b ? (a, b) : (b, a);
                if (!baselineGaps.TryGetValue(key, out var gap))
                    baselineGaps[key] = gap = Gap(At(a, baseline[a]), At(b, baseline[b]), Required(a, b) + 1);
                return gap;
            }

            /// <summary>Material gap, negative when one part sits in the other's material.</summary>
            private static double Gap(NativeContours a, NativeContours b, double reach)
            {
                if (!Near(a.Bounds, b.Bounds, reach + Margin))
                    return double.PositiveInfinity;
                if (a.Nested(b))
                    return -1;
                return a.GapTo(b, reach + Margin, out _);
            }

            private static bool Near(Box a, Box b, double reach) =>
                a.Left <= b.Right + reach && b.Left <= a.Right + reach
                && a.Bottom <= b.Top + reach && b.Bottom <= a.Top + reach;

            /// <summary>Inside the work area, or no further outside than at the committed layout;
            /// and into no clamp it was not already touching.</summary>
            private bool InBounds(int index, NativeContours shape)
            {
                var start = At(index, baseline[index]).Bounds;
                var box = shape.Bounds;
                if (box.Left < System.Math.Min(workArea.Left, start.Left) - Tolerance.Epsilon
                    || box.Right > System.Math.Max(workArea.Right, start.Right) + Tolerance.Epsilon
                    || box.Bottom < System.Math.Min(workArea.Bottom, start.Bottom) - Tolerance.Epsilon
                    || box.Top > System.Math.Max(workArea.Top, start.Top) + Tolerance.Epsilon)
                    return false;
                return clamps.All(clamp => !clamp.Intersects(box) || clamp.Intersects(start));
            }

            /// <summary>The part's contours at <paramref name="position"/>; committed poses are cached.</summary>
            private NativeContours At(int index, Vector position)
            {
                var committed = position.X == baseline[index].X && position.Y == baseline[index].Y;
                if (committed && world[index] != null)
                    return world[index];
                var shape = local[index].Translated(position);
                if (committed)
                    world[index] = shape;
                return shape;
            }
        }
    }
}
