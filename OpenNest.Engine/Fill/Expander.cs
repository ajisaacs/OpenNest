using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using OpenNest.Converters;
using OpenNest.Geometry;

namespace OpenNest.Engine.Fill
{
    /// <summary>
    /// Pushes a group of selected parts apart until every constrained pair
    /// (selected↔selected and selected↔obstacle) reaches a target part-to-part
    /// spacing, with the plate work area and all non-selected parts as hard
    /// boundaries. The inverse of <see cref="Compactor"/>: it grows gaps instead
    /// of closing them.
    /// <para>
    /// Input with overlaps is accepted: an overlapping pair is a pair with
    /// negative clearance and is separated along the minimum-translation
    /// direction. Moves are straight-line only — a pair that could separate only
    /// by routing around a blocker is reported as a violation instead. Final
    /// positions are always overlap-free; nothing moves off the work area.
    /// </para>
    /// <para>
    /// Anchor policy: within a violated pair the later-indexed selected part
    /// moves; the counterpart moves only as a fallback when the anchor mover is
    /// fully blocked by a work-area edge. Walls (non-selected parts) never move.
    /// </para>
    /// </summary>
    public static class Expander
    {
        public sealed class Options
        {
            /// <summary>First spacing probed by the search; also the floor for the doubling step.</summary>
            public double InitialStep = 1.0;

            /// <summary>Bisection stops once the achievable spacing is known within this tolerance.</summary>
            public double Tolerance = 0.01;

            /// <summary>Relaxation iteration cap per separation run.</summary>
            public int MaxIterations = 100;

            /// <summary>Upper bound for the spacing search. 0 = auto (work-area diagonal).</summary>
            public double MaxSpacing = 0;
        }

        public sealed class Violation
        {
            public Part A;
            public Part B;

            /// <summary>Clearance actually reached (may be negative for unresolvable overlaps).</summary>
            public double Achieved;

            /// <summary>True when a work-area edge, not a part, blocked the last needed move.</summary>
            public bool BlockedByEdge;
        }

        public sealed class Result
        {
            /// <summary>
            /// Minimum part-to-part clearance the returned layout satisfies,
            /// never below zero. 0 with violations means the layout was only
            /// cleaned up as far as possible, not opened up.
            /// </summary>
            public double AchievedSpacing;

            /// <summary>True when the run was cancelled; no positions were changed.</summary>
            public bool Cancelled;

            /// <summary>Pairs that could not reach <see cref="AchievedSpacing"/>.</summary>
            public List<Violation> Violations = new();
        }

        /// <summary>
        /// Raises the part-to-part spacing of the selection as far as the plate
        /// and its other parts allow, applying the best spacing found. Plate
        /// PartSpacing/EdgeSpacing are not modified; edges keep their own
        /// EdgeSpacing floor while only part-to-part clearance chases the target.
        /// Mutates <paramref name="selected"/> locations; probing never touches
        /// them, so a failed or cancelled run leaves the layout unchanged.
        /// </summary>
        public static Result Expand(
            List<Part> selected,
            Plate plate,
            Options options = null,
            CancellationToken token = default
        )
        {
            if (plate == null)
                throw new ArgumentNullException(nameof(plate));
            if (selected == null || selected.Count < 2)
                throw new ArgumentException(
                    "Expand requires at least two selected parts.",
                    nameof(selected)
                );
            if (selected.Any(p => !plate.Parts.Contains(p)))
                throw new ArgumentException(
                    "All selected parts must belong to the plate.",
                    nameof(selected)
                );

            var opt = options ?? new Options();
            var context = SeparationContext.Prepare(selected, plate);
            var entry = context.Positions(); // all parts; movers are [0, count)
            var result = new Result();

            var sMin = MinimumPairClearance(context, entry);
            result.AchievedSpacing = System.Math.Max(0, sMin);

            if (token.IsCancellationRequested)
            {
                result.Cancelled = true;
                return result;
            }

            var sGood = sMin;
            var cap = opt.MaxSpacing > 0 ? opt.MaxSpacing : context.SpacingCap;
            double hi; // first spacing that failed; double.NaN = none yet

            (bool Converged, List<Vector> Positions) Probe(double spacing)
            {
                var attempt = Separate(context, entry, spacing, opt.MaxIterations, token);
                return (attempt.Converged, attempt.Positions);
            }

            // Doubling phase: commit every spacing that converges. The cap gets
            // its own probe even when the step jumps past it, and no spacing is
            // probed twice.
            var s = System.Math.Max(opt.InitialStep, 2 * System.Math.Max(0, sGood));
            hi = double.NaN;

            while (true)
            {
                if (s > cap)
                {
                    if (cap > sGood + opt.Tolerance)
                        s = cap;
                    else
                        break;
                }

                if (s <= sGood + opt.Tolerance)
                    break;

                if (token.IsCancellationRequested)
                {
                    result.Cancelled = true;
                    return result;
                }

                var probe = Probe(s);

                if (probe.Converged)
                {
                    sGood = s;
                    if (sGood >= cap)
                        break;
                    s = System.Math.Max(s * 2, sGood + opt.Tolerance);
                }
                else
                {
                    hi = s;
                    break;
                }
            }

            // Bisection between the last spacing that converged and the first that failed.
            if (!double.IsNaN(hi))
            {
                var lo = System.Math.Max(0, sGood);
                while (hi - lo > opt.Tolerance)
                {
                    if (token.IsCancellationRequested)
                    {
                        result.Cancelled = true;
                        return result;
                    }

                    var mid = (lo + hi) / 2;
                    if (Probe(mid).Converged)
                        lo = mid;
                    else
                        hi = mid;
                }
                sGood = lo;
            }

            // Finalize: re-separate at the applied spacing from the entry state.
            // Deterministic, so this reproduces any committed probe exactly; when
            // sGood was never probed (overlapping entry) it still performs the
            // best-effort cleanup and yields the violation report.
            var final = Separate(context, entry, System.Math.Max(0, sGood), opt.MaxIterations, token);

            if (token.IsCancellationRequested)
            {
                result.Cancelled = true;
                return result;
            }

            var positions = final.Positions;

            Apply(selected, entry, positions);

            var measured = MinimumPairClearance(context, positions);
            result.AchievedSpacing = System.Math.Max(0, System.Math.Min(System.Math.Max(0, sGood), measured));
            result.Violations = final.Violations;
            return result;
        }

        /// <summary>
        /// Relaxes the given parts apart to a fixed target spacing against the
        /// plate. Works on scratch positions; the caller applies them. Exposed
        /// for testing and for callers that manage their own spacing search.
        /// </summary>
        public static (bool Converged, List<Vector> Positions, List<Violation> Violations) Separate(
            List<Part> selected,
            Plate plate,
            double spacing,
            int maxIterations = 100,
            CancellationToken token = default
        )
        {
            var context = SeparationContext.Prepare(selected, plate);
            var positions = context.Positions();
            return Separate(context, positions, spacing, maxIterations, token);
        }

        private static void Apply(List<Part> selected, List<Vector> from, List<Vector> to)
        {
            // Only movers occupy [0, selected.Count); walls never move.
            for (var i = 0; i < selected.Count; i++)
            {
                var delta = to[i] - from[i];
                if (delta.X != 0 || delta.Y != 0)
                    selected[i].Offset(delta);
            }
        }

        private static double MinimumPairClearance(SeparationContext context, List<Vector> positions)
        {
            var min = double.MaxValue;

            foreach (var pair in context.Pairs)
            {
                var (distance, _) = context.PairClearance(pair.IndexA, pair.IndexB, positions);
                if (distance < min)
                    min = distance;
            }

            return min == double.MaxValue ? 0 : min;
        }

        private static (bool Converged, List<Vector> Positions, List<Violation> Violations) Separate(
            SeparationContext context,
            List<Vector> start,
            double spacing,
            int maxIterations,
            CancellationToken token
        )
        {
            var positions = new List<Vector>(start);
            var stuck = new HashSet<int>();
            var violations = new List<Violation>();
            var epsMove = 1e-4;

            // Internal margin absorbs the clearance kernel's tessellation error so
            // the applied spacing holds against the production validators.
            var target = spacing + 0.002;

            var iterationLimit = maxIterations < 1 ? 1 : maxIterations;

            for (var iteration = 0; iteration < iterationLimit; iteration++)
            {
                if (token.IsCancellationRequested)
                    return (false, start, violations);

                var moved = 0.0;
                var stuckChanged = false;

                foreach (var pair in context.Pairs)
                {
                    if (stuck.Contains(pair.Id))
                        continue;

                    var (distance, directionA) = context.PairClearance(
                        pair.IndexA,
                        pair.IndexB,
                        positions
                    );

                    // Trigger at the user spacing, not the internal margin: a
                    // pair already at the requested spacing must not be nudged,
                    // or feasible layouts at the ceiling (every pair exactly at
                    // spacing) would oscillate forever. The margin only sets how
                    // far past the trigger a push carries, absorbing tessellation
                    // error in the measurement.
                    if (distance >= spacing)
                        continue;

                    var need = target - distance;

                    // Anchor policy (decided): only the later-indexed selected
                    // part of a violated pair moves. Counterparts and walls
                    // never do — a pair whose anchor mover cannot reach the
                    // target is a violation, not an invitation to drift the
                    // anchor.
                    var moverIndex = pair.Mover;

                    // Clearance direction translates A away from B; a mover on
                    // the B side travels the opposite way.
                    var direction = moverIndex == pair.IndexA ? directionA : -directionA;

                    var room = context.ClipToWorkArea(
                        moverIndex,
                        positions[moverIndex],
                        direction,
                        need
                    );

                    // Take the largest valid step up to `room`: partial moves let
                    // a blocked mover advance again once its own blockers move
                    // away in later iterations (a wave separates a chain).
                    var applied = 0.0;
                    var blockedByEdge = false;

                    if (room > 0)
                    {
                        var trial = positions[moverIndex] + direction * room;

                        if (context.MaintainsValidity(moverIndex, trial, positions))
                        {
                            positions[moverIndex] = trial;
                            applied = room;
                        }
                        else
                        {
                            var lo = 0.0;
                            var hi2 = room;
                            for (var bisect = 0; bisect < 24 && hi2 - lo > 1e-6; bisect++)
                            {
                                var mid = (lo + hi2) / 2;
                                if (
                                    context.MaintainsValidity(
                                        moverIndex,
                                        positions[moverIndex] + direction * mid,
                                        positions
                                    )
                                )
                                    lo = mid;
                                else
                                    hi2 = mid;
                            }

                            if (lo > epsMove)
                            {
                                positions[moverIndex] = positions[moverIndex] + direction * lo;
                                applied = lo;
                            }
                        }

                        blockedByEdge = applied < need - epsMove;
                        moved += applied;
                    }
                    else
                    {
                        blockedByEdge = true;
                    }

                    // A pair fully separated to the user target (the internal margin
                    // absorbs tessellation slack) is satisfied even if not to target.
                    if (applied > 0)
                    {
                        var (finalDistance, _) = context.PairClearance(
                            pair.IndexA,
                            pair.IndexB,
                            positions
                        );
                        if (finalDistance >= spacing)
                            continue;
                        // Partial progress: keep the pair live — its blockers may
                        // move away in later iterations and unblock the rest.
                        continue;
                    }

                    // Zero progress twice in a row parks the pair; the final sweep
                    // re-measures everything, so mid-loop bookkeeping never lies.
                    if (stuck.Contains(pair.Id))
                        continue;

                    stuck.Add(pair.Id);
                    stuckChanged = true;
                }

                if (moved < epsMove && !stuckChanged)
                    break;
            }

            // Honest verdict: stuck bookkeeping and the internal margin can both
            // let a pair read as satisfied mid-loop while a later pair move
            // un-does it (oscillation). Re-measure every constrained pair at the
            // final positions once; the violations this sweep finds are the
            // report, and any violation makes the run non-converged.
            violations.Clear();
            foreach (var pair in context.Pairs)
            {
                var (distance, _) = context.PairClearance(
                    pair.IndexA,
                    pair.IndexB,
                    positions
                );

                if (distance >= spacing)
                    continue;

                violations.Add(
                    new Violation
                    {
                        A = context.PartOf(pair.IndexA),
                        B = context.PartOf(pair.IndexB),
                        Achieved = distance,
                        BlockedByEdge = stuck.Contains(pair.Id),
                    }
                );
            }

            return (violations.Count == 0, positions, violations);
        }

        /// <summary>One constrained part↔part pair with its anchor mover.</summary>
        private sealed class Pair
        {
            public int Id;
            public int IndexA;
            public int IndexB;
            public int Mover;
        }

        /// <summary>
        /// Per-run prepared geometry. Rings are local-frame polygons (world = local
        /// + scratch position), prepared once per distinct Program by reference,
        /// mirroring <see cref="PartOverlapChecker"/>'s caching but translatable.
        /// </summary>
        private sealed class SeparationContext
        {
            private readonly List<Part> parts; // movers [0, moverCount) then walls
            private readonly List<RingSet> shapes; // per part
            private readonly Box[] localBoxes; // per part, local frame
            private readonly int moverCount;
            private readonly Box workArea;

            public readonly List<Pair> Pairs = new();
            public readonly double SpacingCap;

            private sealed class RingSet
            {
                public Polygon Outer;
                public List<Polygon> Rings = new(); // outer + cutout rings, local frame
                public List<Polygon> Holes = new();
            }

            private SeparationContext(
                List<Part> parts,
                List<RingSet> shapes,
                Box[] localBoxes,
                int moverCount,
                Box workArea,
                double spacingCap
            )
            {
                this.parts = parts;
                this.shapes = shapes;
                this.localBoxes = localBoxes;
                this.moverCount = moverCount;
                this.workArea = workArea;
                SpacingCap = spacingCap;
            }

            public static SeparationContext Prepare(List<Part> selected, Plate plate)
            {
                var movers = new List<Part>(selected);
                var walls = plate.Parts.Where(p => !movers.Contains(p)).ToList();

                var parts = new List<Part>(movers.Count + walls.Count);
                parts.AddRange(movers);
                parts.AddRange(walls);

                var programs = new Dictionary<CNC.Program, RingSet>(
                    ReferenceEqualityComparer.Instance
                );
                var shapes = new List<RingSet>(parts.Count);
                var localBoxes = new Box[parts.Count];

                for (var i = 0; i < parts.Count; i++)
                {
                    shapes.Add(PrepareProgram(programs, parts[i].Program));
                    localBoxes[i] = LocalBox(parts[i]);
                }

                var workArea = plate.WorkArea();
                var spacingCap = System.Math.Sqrt(
                    workArea.Length * workArea.Length + workArea.Width * workArea.Width
                );

                var context = new SeparationContext(
                    parts,
                    shapes,
                    localBoxes,
                    movers.Count,
                    workArea,
                    spacingCap
                );
                context.BuildPairs();
                return context;
            }

            private static RingSet PrepareProgram(
                Dictionary<CNC.Program, RingSet> programs,
                CNC.Program program
            )
            {
                if (programs.TryGetValue(program, out var existing))
                    return existing;

                var prepared = new RingSet();
                var entities = ConvertProgram
                    .ToGeometry(program)
                    .Where(e => SpecialLayers.IsMaterial(e.Layer))
                    .ToList();

                if (entities.Count > 0)
                {
                    var profile = new ShapeProfile(entities);

                    if (profile.Perimeter != null)
                    {
                        prepared.Outer = profile.Perimeter.ToPolygonWithTolerance(0.001);
                        prepared.Rings.Add(prepared.Outer);

                        foreach (var cutout in profile.Cutouts)
                        {
                            var hole = cutout.ToPolygonWithTolerance(0.001);
                            prepared.Rings.Add(hole);
                            prepared.Holes.Add(hole);
                        }
                    }
                }

                programs.Add(program, prepared);
                return prepared;
            }

            private static Box LocalBox(Part part)
            {
                var box = part.BoundingBox;
                return new Box(
                    box.Left - part.Location.X,
                    box.Bottom - part.Location.Y,
                    box.Length,
                    box.Width
                );
            }

            private void BuildPairs()
            {
                var id = 0;

                for (var a = 0; a < parts.Count; a++)
                {
                    for (var b = a + 1; b < parts.Count; b++)
                    {
                        var aMover = a < moverCount;
                        var bMover = b < moverCount;

                        if (!aMover && !bMover)
                            continue;

                        Pairs.Add(
                            new Pair
                            {
                                Id = id++,
                                IndexA = a,
                                IndexB = b,
                                // Anchor policy: the later-index mover moves.
                                Mover = bMover ? b : a,
                            }
                        );
                    }
                }
            }

            public bool IsMover(int index) => index < moverCount;

            public Part PartOf(int index) => parts[index];

            /// <summary>Current world positions of every part in pair-index order.</summary>
            public List<Vector> Positions() => parts.Select(p => p.Location).ToList();

            /// <summary>
            /// Signed material clearance between two parts at the given scratch
            /// positions. Material overlap (Collision oracle with hole subtraction)
            /// reports negative penetration through the outer rings; otherwise the
            /// clearance is the minimum boundary distance over all ring pairs, so a
            /// part inside another's cutout measures its true gap to the hole ring
            /// instead of a bogus outer-ring penetration.
            /// </summary>
            public (double Distance, Vector Direction) PairClearance(
                int indexA,
                int indexB,
                List<Vector> positions
            )
            {
                var setA = shapes[indexA];
                var setB = shapes[indexB];

                if (setA.Outer == null || setB.Outer == null)
                    return (0, new Vector(1, 0));

                var offsetA = positions[indexA];
                var offsetB = positions[indexB];

                var outerA = CloneAt(setA.Outer, offsetA);
                var outerB = CloneAt(setB.Outer, offsetB);

                var holesA = setA.Holes.Count == 0 ? null : CloneAll(setA.Holes, offsetA);
                var holesB = setB.Holes.Count == 0 ? null : CloneAll(setB.Holes, offsetB);

                if (Collision.HasOverlap(outerA, outerB, holesA, holesB))
                {
                    var penetration = Clearance.Between(outerA, outerB);
                    if (penetration.Distance < 0)
                        return (penetration.Distance, penetration.Direction);
                    // Hole subtraction resolved what the outers overlap: touching.
                    return (0, penetration.Direction);
                }

                double best = double.MaxValue;
                var bestDir = new Vector(1, 0);

                foreach (var ringA in setA.Rings)
                {
                    var worldA = CloneAt(ringA, offsetA);

                    foreach (var ringB in setB.Rings)
                    {
                        var worldB = CloneAt(ringB, offsetB);
                        var clearance = Clearance.BoundaryDistance(worldA, worldB);

                        if (clearance.Distance < best)
                        {
                            best = clearance.Distance;
                            bestDir = clearance.Direction;
                        }
                    }
                }

                return (best, bestDir);
            }

            /// <summary>
            /// Largest α ≤ need such that translating the part by direction·α keeps
            /// its AABB inside the work area.
            /// </summary>
            public double ClipToWorkArea(int index, Vector position, Vector direction, double need)
            {
                var box = localBoxes[index];
                var left = position.X + box.Left - workArea.Left;
                var right = workArea.Right - (position.X + box.Right);
                var bottom = position.Y + box.Bottom - workArea.Bottom;
                var top = workArea.Top - (position.Y + box.Top);

                var max = need;
                if (direction.X > 0)
                    max = System.Math.Min(max, right / direction.X);
                else if (direction.X < 0)
                    max = System.Math.Min(max, left / -direction.X);

                if (direction.Y > 0)
                    max = System.Math.Min(max, top / direction.Y);
                else if (direction.Y < 0)
                    max = System.Math.Min(max, bottom / -direction.Y);

                return max < 0 ? 0 : max;
            }

            /// <summary>
            /// True when the part at <paramref name="trial"/> stays inside the work
            /// area and keeps no material overlap with any other part (Collision
            /// oracle with hole subtraction, so part-in-cutout stays legal).
            /// </summary>
            public bool MaintainsValidity(int index, Vector trial, List<Vector> positions)
            {
                var box = localBoxes[index];
                var movedBox = box.Translate(trial);

                if (
                    movedBox.Left < workArea.Left - 1e-9
                    || movedBox.Right > workArea.Right + 1e-9
                    || movedBox.Bottom < workArea.Bottom - 1e-9
                    || movedBox.Top > workArea.Top + 1e-9
                )
                    return false;

                var outer = shapes[index].Outer;
                if (outer == null)
                    return true;

                var worldOuter = CloneAt(outer, trial);
                var worldHoles = shapes[index].Holes.Count == 0
                    ? null
                    : CloneAll(shapes[index].Holes, trial);

                for (var i = 0; i < parts.Count; i++)
                {
                    if (i == index)
                        continue;

                    var otherOuter = shapes[i].Outer;
                    if (otherOuter == null)
                        continue;

                    if (!BoxOverlap(movedBox, localBoxes[i].Translate(positions[i]), 0.002))
                        continue;

                    var worldOther = CloneAt(otherOuter, positions[i]);
                    var worldOtherHoles = shapes[i].Holes.Count == 0
                        ? null
                        : CloneAll(shapes[i].Holes, positions[i]);

                    if (
                        Collision.HasOverlap(
                            worldOuter,
                            worldOther,
                            worldHoles,
                            worldOtherHoles
                        )
                    )
                        return false;
                }

                return true;
            }

            private static List<Polygon> CloneAll(List<Polygon> polygons, Vector offset)
            {
                var list = new List<Polygon>(polygons.Count);
                foreach (var polygon in polygons)
                    list.Add(CloneAt(polygon, offset));
                return list;
            }

            /// <summary>Clone with world bounds applied — prepared rings are never mutated.</summary>
            private static Polygon CloneAt(Polygon polygon, Vector offset)
            {
                var clone = (Polygon)polygon.Clone();
                clone.UpdateBounds();
                clone.Offset(offset);
                return clone;
            }

            private static bool BoxOverlap(Box a, Box b, double slack)
            {
                return !(
                    a.Right + slack < b.Left
                    || b.Right + slack < a.Left
                    || a.Top + slack < b.Bottom
                    || b.Top + slack < a.Bottom
                );
            }
        }
    }
}
