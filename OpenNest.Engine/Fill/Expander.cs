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
    /// Input with overlaps is accepted for safe direct separation; unresolved
    /// overlaps remain in the violation report. Moves are straight-line only —
    /// a pair that requires routing around a blocker remains unresolved.
    /// Plate edges retain their EdgeSpacing floor.
    /// </para>
    /// <para>
    /// Anchor policy: within a violated selected pair only its later-indexed
    /// selected part moves. Nonselected parts never move; there is no counterpart
    /// fallback when the prescribed mover is blocked.
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

            /// <summary>Pairs still below the attempted spacing, including
            /// unresolved negative clearances when no positive target was feasible.</summary>
            public List<Violation> Violations = new();
        }

        /// <summary>
        /// Raises the part-to-part spacing of the selection as far as the plate
        /// and its other parts allow, applying the best spacing found. Plate
        /// PartSpacing/EdgeSpacing are not modified; edges keep their own
        /// EdgeSpacing floor while only part-to-part clearance chases the target.
        /// Mutates <paramref name="selected"/> locations only after the final
        /// proposal passes validation; failed individual probes and cancellation
        /// leave all live locations unchanged. A completed run can apply safe
        /// partial separation and report any remaining violations.
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
            // Floors belong to this public operation, not to any individual probe.
            // A failed final check leaves every live part untouched.
            if (!context.MeetsEntryFloors(positions, entry))
                throw new InvalidOperationException("Expand proposal violates an entry clearance floor.");

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

                // Retry every pair on the next pass if another blocker moved;
                // a complete pass without progress stops genuinely pinned layouts.
                var moved = 0.0;

                foreach (var pair in context.Pairs)
                {

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

                    // A zero-distance hole contact can have an ambiguous nearest-
                    // boundary fallback. Only if the normal candidate makes no
                    // safe progress, retry its opposite through the same path,
                    // floor and active-improvement checks. Never move the anchor.
                    var attempt = ProposeMove(context, positions, pair, distance, direction, need, epsMove);
                    if (attempt.Applied <= 0 && distance <= 1e-7)
                    {
                        var opposite = ProposeMove(context, positions, pair, distance, -direction, need, epsMove);
                        if (opposite.Applied > attempt.Applied)
                            attempt = opposite;
                    }
                    var applied = attempt.Applied;
                    if (applied > 0)
                    {
                        positions[moverIndex] = attempt.Proposal;
                        moved += applied;
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

                    // No progress now; a later part may unblock this pair on
                    // the next pass. The final sweep remeasures all pairs.
                }

                if (moved < epsMove)
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
                var (distance, directionA) = context.PairClearance(
                    pair.IndexA,
                    pair.IndexB,
                    positions
                );

                if (distance >= spacing)
                    continue;

                var direction = pair.Mover == pair.IndexA ? directionA : -directionA;
                var need = target - distance;
                var attempt = ProposeMove(context, positions, pair, distance, direction, need, epsMove);
                if (attempt.Applied <= 0 && distance <= 1e-7)
                {
                    var opposite = ProposeMove(context, positions, pair, distance, -direction, need, epsMove);
                    if (opposite.Applied > attempt.Applied)
                        attempt = opposite;
                }
                // Only an actual first work-area limit is an edge blocker.
                // An available move at the iteration budget, or a part/floor
                // limiting first, is not evidence of an edge obstruction.
                var blockedByEdge = attempt.EdgeLimited && !attempt.PartLimited
                    && attempt.Applied < need - epsMove;
                violations.Add(
                    new Violation
                    {
                        A = context.PartOf(pair.IndexA),
                        B = context.PartOf(pair.IndexB),
                        Achieved = distance,
                        BlockedByEdge = blockedByEdge,
                    }
                );
            }

            return (violations.Count == 0, positions, violations);
        }

        private static (double Applied, Vector Proposal, bool EdgeLimited, bool PartLimited) ProposeMove(
            SeparationContext context, List<Vector> positions, Pair pair,
            double distance, Vector direction, double need, double epsMove)
        {
            var mover = pair.Mover;
            var start = positions[mover];
            var edgeRoom = context.ClipToWorkArea(mover, start, direction, need);
            var room = context.ClipToMaterial(mover, positions, direction, edgeRoom);
            var edgeLimited = edgeRoom < need - epsMove;
            var partLimited = room < edgeRoom - epsMove;
            if (room <= epsMove)
                return (0, start, edgeLimited, partLimited);

            var trial = start + direction * room;
            if (context.MaintainsValidity(mover, trial, positions, pair, distance))
                return (room, trial, edgeLimited, partLimited);

            var lo = 0.0;
            var hi = room;
            for (var bisect = 0; bisect < 24 && hi - lo > 1e-6; bisect++)
            {
                var mid = (lo + hi) / 2;
                if (context.MaintainsValidity(mover, start + direction * mid, positions, pair, distance))
                    lo = mid;
                else
                    hi = mid;
            }
            return lo > epsMove
                ? (lo, start + direction * lo, edgeLimited, true)
                : (0, start, edgeLimited, true);
        }

        /// <summary>One constrained part↔part pair with its anchor mover.</summary>
        private sealed class Pair
        {
            public int IndexA;
            public int IndexB;
            public int Mover;
            public double EntryFloor;
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
            private readonly int clampStart; // synthetic walls, after actual plate parts
            private readonly IReadOnlyList<Box> clampBoxes; // exact placement-frame keep-outs
            private readonly Box workArea;

            public readonly List<Pair> Pairs = new();
            public readonly double SpacingCap;

            private sealed class RingSet
            {
                public Polygon Outer;
                public List<Polygon> Rings = new(); // outer + cutout rings, local frame
                public List<Polygon> Holes = new();
                public List<Entity> Boundaries = new(); // native outer + hole loops, local frame
                public SlideContactGeometry Contact;
                public Box NativeBounds; // perimeter's native-entity envelope
            }

            private SeparationContext(
                List<Part> parts,
                List<RingSet> shapes,
                Box[] localBoxes,
                int moverCount,
                int clampStart,
                IReadOnlyList<Box> clampBoxes,
                Box workArea,
                double spacingCap
            )
            {
                this.parts = parts;
                this.shapes = shapes;
                this.localBoxes = localBoxes;
                this.moverCount = moverCount;
                this.clampStart = clampStart;
                this.clampBoxes = clampBoxes;
                this.workArea = workArea;
                SpacingCap = spacingCap;
            }

            public static SeparationContext Prepare(List<Part> selected, Plate plate)
            {
                var movers = new List<Part>(selected);
                var walls = plate.Parts.Where(p => !movers.Contains(p)).ToList();
                var clampStart = movers.Count + walls.Count;
                // Clamp walls include the bottom-edge clearance envelope.
                var clampBoxes = Jobs.InteractiveClamps.ClearanceRectangles(plate);
                walls.AddRange(Jobs.InteractiveClamps.WallParts(plate));
                if (clampBoxes.Count != walls.Count + movers.Count - clampStart)
                    throw new InvalidOperationException("Clamp wall preparation changed its ordering.");

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
                    clampStart,
                    clampBoxes,
                    workArea,
                    spacingCap
                );
                context.BuildPairs();
                var entry = context.Positions();
                foreach (var pair in context.Pairs)
                    pair.EntryFloor = context.PairClearance(pair.IndexA, pair.IndexB, entry).Distance;
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
                        prepared.NativeBounds = profile.Perimeter.BoundingBox;
                        prepared.Outer = profile.Perimeter.ToPolygonWithTolerance(0.001);
                        // Polygon.Cleanup compares vertices at geometry epsilon and
                        // can erase an accepted sub-grid clamp. Preserve exact line
                        // endpoints instead of treating its material as empty.
                        if ((profile.Perimeter.BoundingBox.Length < KeepOutOverlap.ToleranceFreeBelow
                            || profile.Perimeter.BoundingBox.Width < KeepOutOverlap.ToleranceFreeBelow)
                            && profile.Perimeter.Entities.All(e => e is Line))
                            prepared.Outer = ExactLineRing(profile.Perimeter.Entities);
                        prepared.Rings.Add(prepared.Outer);
                        prepared.Boundaries.AddRange(profile.Perimeter.Entities);

                        foreach (var cutout in profile.Cutouts)
                        {
                            var hole = cutout.ToPolygonWithTolerance(0.001);
                            prepared.Rings.Add(hole);
                            prepared.Holes.Add(hole);
                            prepared.Boundaries.AddRange(cutout.Entities);
                        }
                        prepared.Contact = SlideContactGeometry.Prepare(prepared.Boundaries);
                    }
                }

                programs.Add(program, prepared);
                return prepared;
            }

            private static Polygon ExactLineRing(List<Entity> entities)
            {
                var polygon = new Polygon();
                foreach (var entity in entities)
                    polygon.Vertices.Add(((Line)entity).StartPoint);
                if (polygon.Vertices.Count > 0)
                    polygon.Vertices.Add(polygon.Vertices[0]);
                polygon.UpdateBounds();
                return polygon;
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
            /// Stop at the first material-blocking contact along the whole straight
            /// translation, including hole walls. Destination clearance alone can
            /// accept a pose on the far side of a thin wall.
            /// </summary>
            public double ClipToMaterial(int index, List<Vector> positions, Vector direction, double room)
            {
                if (room <= 0 || !double.IsFinite(room)
                    || !double.IsFinite(direction.X) || !double.IsFinite(direction.Y))
                    return 0;
                var moving = shapes[index];
                if (moving.Contact?.IsResolved != true || moving.Outer == null)
                    return 0;

                for (var i = 0; i < parts.Count; i++)
                {
                    if (i == index)
                        continue;
                    var stationary = shapes[i];
                    if (i >= clampStart && KeepOutOverlap.NeedsToleranceFreeTest(clampBoxes[i - clampStart]))
                    {
                        // Slide contact and polygon overlap have tolerances larger
                        // than a tiny keep-out. Clip at the earliest possible
                        // native-bound contact; if bounds overlap already, refuse
                        // uncertain motion instead of crossing the physical zone.
                        room = System.Math.Min(room, ClipToNativeBox(
                            moving, positions[index], direction, room, clampBoxes[i - clampStart]));
                        if (room <= 0)
                            return 0;
                        continue;
                    }
                    if (stationary.Contact?.IsResolved != true || stationary.Outer == null)
                        return 0; // Unknown boundary is not empty space.

                    var origin = positions[index];
                    var start = origin;
                    var alreadyOverlapping = Collision.HasOverlap(
                        CloneAt(moving.Outer, origin), CloneAt(stationary.Outer, positions[i]),
                        moving.Holes.Count == 0 ? null : CloneAll(moving.Holes, origin),
                        stationary.Holes.Count == 0 ? null : CloneAll(stationary.Holes, positions[i]));
                    var exit = 0.0;
                    if (alreadyOverlapping)
                    {
                        // The ordinary slide query only certifies initially clear
                        // geometry. A convex line-only overlap can exit directly
                        // along a separating support normal; other overlaps remain
                        // accepted input but cannot be moved without a path proof.
                        if (!TryConvexDirectExit(moving, stationary, origin, positions[i], direction, out exit)
                            || exit > room)
                            return 0;
                        start = origin + direction * exit;
                        if (Collision.HasOverlap(
                            CloneAt(moving.Outer, start), CloneAt(stationary.Outer, positions[i])))
                            return 0;
                    }

                    var query = SpatialQuery.DirectionalDistance(
                        CloneEntitiesAt(moving.Boundaries, start),
                        CloneEntitiesAt(stationary.Boundaries, positions[i]),
                        direction,
                        new SlideContactClassifier(moving.Contact, start, stationary.Contact, positions[i]));
                    if (double.IsNaN(query) || query < 0)
                        return 0;
                    if (query < double.MaxValue)
                        room = System.Math.Min(room, exit + query);
                }
                return room;
            }

            private static double ClipToNativeBox(
                RingSet mover, Vector start, Vector direction, double room, Box obstacle)
            {
                var bounds = mover.NativeBounds;
                if (bounds == null || !double.IsFinite(bounds.Left) || !double.IsFinite(bounds.Right)
                    || !double.IsFinite(bounds.Bottom) || !double.IsFinite(bounds.Top)
                    || !double.IsFinite(obstacle.Left) || !double.IsFinite(obstacle.Right)
                    || !double.IsFinite(obstacle.Bottom) || !double.IsFinite(obstacle.Top))
                    return 0;
                var from = bounds.Translate(start);
                var enter = double.NegativeInfinity;
                var leave = double.PositiveInfinity;
                if (!Slab(from.Left, from.Right, obstacle.Left, obstacle.Right,
                    direction.X, ref enter, ref leave)
                    || !Slab(from.Bottom, from.Top, obstacle.Bottom, obstacle.Top,
                        direction.Y, ref enter, ref leave)
                    || leave <= 0 || enter >= room)
                    return room;
                return System.Math.Max(0, enter);
            }

            private static bool Slab(double low, double high, double wallLow, double wallHigh,
                double speed, ref double enter, ref double leave)
            {
                if (speed == 0)
                    return high > wallLow && low < wallHigh;
                var first = (wallLow - high) / speed;
                var last = (wallHigh - low) / speed;
                enter = System.Math.Max(enter, System.Math.Min(first, last));
                leave = System.Math.Min(leave, System.Math.Max(first, last));
                return enter < leave;
            }

            private static List<Entity> CloneEntitiesAt(List<Entity> entities, Vector position)
            {
                var result = new List<Entity>(entities.Count);
                foreach (var entity in entities)
                {
                    var copy = entity.Clone();
                    copy.Offset(position);
                    result.Add(copy);
                }
                return result;
            }

            private static bool TryConvexDirectExit(
                RingSet moving, RingSet stationary, Vector movePos, Vector fixedPos,
                Vector direction, out double exit)
            {
                exit = 0;
                if (moving.Holes.Count != 0 || stationary.Holes.Count != 0
                    || moving.Boundaries.Count == 0 || stationary.Boundaries.Count == 0
                    || moving.Boundaries.Any(e => e is not Line)
                    || stationary.Boundaries.Any(e => e is not Line)
                    || !IsConvex(moving.Outer) || !IsConvex(stationary.Outer))
                    return false;

                // The ray must point along an actual separating support normal.
                // It must be the nearer exit, not the direction that burrows
                // through the other convex part before reaching its far side.
                var normal = false;
                foreach (var ring in new[] { moving.Outer, stationary.Outer })
                    foreach (var line in ring.ToLines())
                    {
                        var edge = line.pt2 - line.pt1;
                        var length = System.Math.Sqrt(edge.DotProduct(edge));
                        if (length > 1e-8 &&
                            System.Math.Abs((edge.X * direction.X + edge.Y * direction.Y) / length) < 1e-7)
                            normal = true;
                    }
                if (!normal)
                    return false;

                var (minA, maxA) = Project(moving.Outer, movePos, direction);
                var (minB, maxB) = Project(stationary.Outer, fixedPos, direction);
                if (!double.IsFinite(minA) || !double.IsFinite(maxB)
                    || maxB - minA > maxA - minB + 1e-7)
                    return false;
                exit = System.Math.Max(0, maxB - minA);
                return double.IsFinite(exit);
            }

            private static (double Min, double Max) Project(Polygon polygon, Vector origin, Vector axis)
            {
                var min = double.PositiveInfinity;
                var max = double.NegativeInfinity;
                foreach (var point in polygon.Vertices)
                {
                    var projection = (point + origin).DotProduct(axis);
                    min = System.Math.Min(min, projection);
                    max = System.Math.Max(max, projection);
                }
                return (min, max);
            }

            private static bool IsConvex(Polygon polygon)
            {
                var points = polygon.Vertices;
                if (points.Count < 4)
                    return false;
                var sign = 0;
                for (var i = 0; i < points.Count - 1; i++)
                {
                    var a = points[i];
                    var b = points[(i + 1) % (points.Count - 1)];
                    var c = points[(i + 2) % (points.Count - 1)];
                    var cross = (b.X - a.X) * (c.Y - b.Y) - (b.Y - a.Y) * (c.X - b.X);
                    if (!double.IsFinite(cross))
                        return false;
                    if (System.Math.Abs(cross) < 1e-8)
                        continue;
                    var next = System.Math.Sign(cross);
                    if (sign != 0 && sign != next)
                        return false;
                    sign = next;
                }
                return sign != 0;
            }

            // Smaller than the 0.002 tessellation margin; immutable entry floors
            // prevent this comparison tolerance accumulating over iterations.
            private const double FloorTolerance = 1e-6;

            public bool MeetsEntryFloors(List<Vector> positions, List<Vector> entry)
            {
                for (var i = 0; i < moverCount; i++)
                    if (!InsideWorkArea(i, positions[i]) &&
                        (positions[i].X != entry[i].X || positions[i].Y != entry[i].Y))
                        return false;
                foreach (var pair in Pairs)
                {
                    var clearance = PairClearance(pair.IndexA, pair.IndexB, positions).Distance;
                    if (!double.IsFinite(clearance) || clearance < pair.EntryFloor - FloorTolerance)
                        return false;
                }
                return true;
            }

            private bool InsideWorkArea(int index, Vector position)
            {
                var box = localBoxes[index].Translate(position);
                return box.Left >= workArea.Left - 1e-9
                    && box.Right <= workArea.Right + 1e-9
                    && box.Bottom >= workArea.Bottom - 1e-9
                    && box.Top <= workArea.Top + 1e-9;
            }

            /// <summary>Check active improvement and every affected pair against
            /// its immutable operation-entry floor on scratch positions.</summary>
            public bool MaintainsValidity(
                int index, Vector trial, List<Vector> positions, Pair active, double activeDistance)
            {
                if (!InsideWorkArea(index, trial))
                    return false;

                var current = positions[index];
                positions[index] = trial;
                try
                {
                    var improved = PairClearance(active.IndexA, active.IndexB, positions).Distance;
                    if (!double.IsFinite(improved) || improved <= activeDistance + 1e-7)
                        return false;
                    foreach (var pair in Pairs)
                    {
                        if (pair.IndexA != index && pair.IndexB != index)
                            continue;
                        var clearance = PairClearance(pair.IndexA, pair.IndexB, positions).Distance;
                        if (!double.IsFinite(clearance) || clearance < pair.EntryFloor - FloorTolerance)
                            return false;
                        // Once an initial overlap is cleared, never reintroduce it.
                        if (clearance < 0)
                        {
                            positions[index] = current;
                            var before = PairClearance(pair.IndexA, pair.IndexB, positions).Distance;
                            positions[index] = trial;
                            if (before >= 0)
                                return false;
                        }
                    }
                    return true;
                }
                finally
                {
                    positions[index] = current;
                }
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
