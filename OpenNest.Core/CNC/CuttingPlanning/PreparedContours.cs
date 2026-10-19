using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using OpenNest.CNC.CuttingStrategy;
using OpenNest.Diagnostics;
using OpenNest.Geometry;

namespace OpenNest.CNC.CuttingPlanning;

/// <summary>A nominal native entry owned by one preparation, not an actual emitted pierce.</summary>
public sealed record ContourChoice(int ContourOrdinal, int EntityOrdinal, Vector Point)
{
    internal PreparedContours Owner { get; init; }
}

/// <summary>
/// Immutable owned clean contours and settings. Callers supply the clean, already-rotated
/// program and keep it stable during Capture. This is not a topology or lead-path verdict.
/// </summary>
public sealed class PreparedContours
{
    private readonly Shape[] shapes;
    private readonly CuttingParameters parameters;
    private readonly List<Entity> scribes;
    // Open break cuts in program order, each travel-ordered with its scrap on the left. They are
    // not contours: no choices, no material; every full or prefix emission cuts them first.
    private readonly List<Shape> breakCuts;
    // Closed cutouts with tab spans, in program order. Like break cuts they are not choices:
    // every full or prefix emission cuts their pieces after the break cuts.
    private readonly List<Shape> tabbedCutouts;
    // By contour ordinal; all null without pins. A pinned contour has exactly one entry and
    // its own owned settings (the pin's lead-in, lead-out and tab).
    private readonly ContourChoice[] pinned;
    private readonly CuttingParameters[] contourParameters;
    private readonly int[] pinNumbers;

    private PreparedContours(Shape[] shapes, List<Entity> scribes, List<Shape> breakCuts, List<Shape> tabbedCutouts,
        CuttingParameters parameters)
    {
        this.shapes = shapes;
        this.scribes = scribes;
        this.breakCuts = breakCuts;
        this.tabbedCutouts = tabbedCutouts;
        this.parameters = parameters;
        pinned = new ContourChoice[shapes.Length];
        pinNumbers = new int[shapes.Length];
    }

    private PreparedContours(PreparedContours unpinned, IReadOnlyList<CutPinResolution> pins, CancellationToken token)
        : this(unpinned.shapes, unpinned.scribes, unpinned.breakCuts, unpinned.tabbedCutouts, unpinned.parameters)
    {
        contourParameters = new CuttingParameters[shapes.Length];
        var rejected = new List<CutPinResolution>();
        foreach (var pin in pins)
        {
            token.ThrowIfCancellationRequested();
            if (pin == null)
                throw new ArgumentException("A pin resolution is missing.", nameof(pins));
            if (!pin.IsResolved)
                continue; // The caller reports why; the contour stays automatic.
            var contour = pin.ContourOrdinal;
            if (contour < 0 || contour >= Count || pin.EntityOrdinal < 0
                || pin.EntityOrdinal >= shapes[contour].Entities.Count)
                throw new ArgumentException("A pin resolution does not belong to these contours.", nameof(pins));
            if (pinned[contour] != null)
                throw new ArgumentException("Two pins resolve to one contour.", nameof(pins));
            // The emitter tabs only the outside, so a tab elsewhere cannot be cut as pinned.
            if (pin.Pin.HasTab && contour != PerimeterOrdinal)
            {
                rejected.Add(pin);
                continue;
            }
            var choice = new ContourChoice(contour, pin.EntityOrdinal, pin.Point) { Owner = this };
            ValidateChoice(choice);
            pinned[contour] = choice;
            pinNumbers[contour] = pin.PinIndex + 1;
            contourParameters[contour] = PinParameters(parameters, pin.Pin);
        }
        RejectedPins = rejected.AsReadOnly();
    }

    public int Count => shapes.Length;

    /// <summary>The pierce zone every lead-in pierce of these contours keeps; 0 when off.</summary>
    internal double PierceZoneRadius => parameters.GetPierceZoneRadius();
    public int PerimeterOrdinal => Count - 1;

    /// <summary>Resolved pins this preparation could not honour: a tab on an internal contour.</summary>
    public IReadOnlyList<CutPinResolution> RejectedPins { get; } = Array.Empty<CutPinResolution>();

    /// <summary>True when a drawing pin fixes this contour's entry, leads and tab.</summary>
    public bool IsPinned(int contourOrdinal) => GetShape(contourOrdinal) != null && pinned[contourOrdinal] != null;

    /// <summary>The one-based drawing pin that fixes this contour, or null.</summary>
    public int? PinNumberOf(int contourOrdinal) => IsPinned(contourOrdinal) ? pinNumbers[contourOrdinal] : null;

    /// <summary>The pinned contour's only entry, or null for an automatic contour.</summary>
    public ContourChoice PinnedEntry(int contourOrdinal) => IsPinned(contourOrdinal) ? pinned[contourOrdinal] : null;

    /// <summary>
    /// As <see cref="Capture(Program, CuttingParameters, CancellationToken)"/>, then fixes every
    /// resolved pin's contour: that contour's only entry is the pin's point and it is emitted with
    /// the pin's own lead-in, lead-out and tab through the same emitter, so its tab gap is genuine
    /// and certifiable. <paramref name="pins"/> must come from <see cref="CutPinResolver"/> against
    /// contours of this same program; unresolved pins are skipped (the caller reports them), a pin
    /// with a tab on an internal contour is listed in <see cref="RejectedPins"/>, and every other
    /// contour keeps the automatic selection with <paramref name="parameters"/>.
    /// </summary>
    public static PreparedContours Capture(Program program, CuttingParameters parameters,
        IReadOnlyList<CutPinResolution> pins, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(pins);
        var unpinned = Capture(program, parameters, token);
        return pins.Any(p => p?.IsResolved != false) ? new PreparedContours(unpinned, pins, token) : unpinned;
    }

    // The pin's leads replace every contour type's leads, since the type the emitter detects is
    // not the caller's concern; angle rounding is off because a pinned entry never moves.
    private static CuttingParameters PinParameters(CuttingParameters shared, ContourCutPin pin)
    {
        var owned = OwnedCuttingParameters.Copy(shared);
        owned.ExternalLeadIn = pin.LeadIn;
        owned.InternalLeadIn = pin.LeadIn;
        owned.ArcCircleLeadIn = pin.LeadIn;
        owned.ExternalLeadOut = pin.LeadOut;
        owned.InternalLeadOut = pin.LeadOut;
        owned.ArcCircleLeadOut = pin.LeadOut;
        owned.RoundLeadInAngles = false;
        owned.TabsEnabled = pin.HasTab;
        if (pin.HasTab)
            owned.TabConfig = pin.Tab;
        return owned;
    }

    private CuttingParameters ParametersFor(int contourOrdinal) => contourParameters?[contourOrdinal] ?? parameters;

    public static PreparedContours Capture(Program program, CuttingParameters parameters, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        var ownedParameters = OwnedCuttingParameters.Copy(parameters);
        ExecutionMotionReader.ReadSupported(program, Vector.Zero, null, token);
        // ToGeometry constructs fresh native entities. Normalize an owned motion graph
        // to incremental mode so absolute subprograms retain their frame offsets too.
        // Retain execution boundaries: ShapeBuilder can join duplicate closed contours.
        var geometryProgram = CopyForGeometry(program, token);
        var contours = new List<Shape>();
        var scribes = new List<Entity>();
        var breakCuts = new List<Shape>();
        var tabbedCutouts = new List<Shape>();
        Shape breakCut = null;
        var current = new Shape();
        foreach (var entity in geometryProgram.ToGeometry())
        {
            token.ThrowIfCancellationRequested();
            if (entity.GetType() != typeof(Line) && entity.GetType() != typeof(Arc) && entity.GetType() != typeof(Circle))
                throw new NotSupportedException("Unsupported native geometry.");
            if (entity.Layer == SpecialLayers.Break)
            {
                if (current.Entities.Count != 0)
                    throw new ArgumentException("Preparation requires closed execution contours.");
                if (entity is Circle || entity.Length <= PostVerificationGeometry.Epsilon || !double.IsFinite(entity.Length))
                    throw new ArgumentException("Degenerate break cut entity.");
                if (breakCut == null || End(breakCut.Entities[^1]).DistanceTo(Start(entity)) > PostVerificationGeometry.Epsilon)
                {
                    breakCut = new Shape();
                    breakCuts.Add(breakCut);
                }
                breakCut.Entities.Add(entity);
                continue;
            }
            breakCut = null;
            if (entity.Layer == SpecialLayers.Rapid || entity.Layer == SpecialLayers.Scribe)
            {
                if (current.Entities.Count != 0)
                    throw new ArgumentException("Preparation requires closed execution contours.");
                if (entity.Layer == SpecialLayers.Scribe)
                    scribes.Add(entity);
                continue;
            }
            if (entity.Layer != SpecialLayers.Cut && entity.Layer != SpecialLayers.Display && entity.Layer != SpecialLayers.Tab)
                throw new ArgumentException("Preparation requires clean cut/display contours.");
            if (entity.Length <= PostVerificationGeometry.Epsilon || !double.IsFinite(entity.Length))
                throw new ArgumentException("Degenerate contour entity.");
            if (entity is Circle)
            {
                if (current.Entities.Count != 0 || entity.Layer == SpecialLayers.Tab)
                    throw new ArgumentException("Circle interrupts an open contour.");
                current.Entities.Add(entity);
                FinishContour();
                continue;
            }
            if (current.Entities.Count > 0 && End(current.Entities[^1]).DistanceTo(Start(entity)) > PostVerificationGeometry.Epsilon)
                throw new ArgumentException("Discontinuous native contour.");
            current.Entities.Add(entity);
            if (NominalContourClosure.IsClosed(Start(current.Entities[0]), End(entity)))
                FinishContour();
        }
        if (current.Entities.Count != 0 || contours.Count == 0)
            throw new ArgumentException("Preparation requires nonempty closed contours.");
        var perimeter = 0;
        for (var i = 1; i < contours.Count; i++)
        {
            var box = contours[i].BoundingBox;
            var best = contours[perimeter].BoundingBox;
            if (box.Width * box.Length > best.Width * best.Length)
                perimeter = i;
        }
        var outer = contours[perimeter];
        contours.RemoveAt(perimeter);
        contours.Add(outer);
        foreach (var cut in breakCuts)
            cut.UpdateBounds();
        return new(contours.ToArray(), scribes, breakCuts, tabbedCutouts, ownedParameters);

        void FinishContour()
        {
            current.UpdateBounds();
            if (ContourCuttingStrategy.IsTabbedCutout(current))
            {
                if (current.Entities.TrueForAll(e => e.Layer == SpecialLayers.Tab))
                    throw new ArgumentException("A tabbed cutout has nothing to cut.");
                tabbedCutouts.Add(current);
            }
            else
                contours.Add(current);
            current = new Shape();
        }
    }

    /// <summary>The closest native entry to <paramref name="approach"/>; a pinned contour's pinned entry.</summary>
    public ContourChoice ClosestEntry(int contourOrdinal, Vector approach)
    {
        PostVerificationGeometry.Validate(approach);
        var shape = GetShape(contourOrdinal);
        if (pinned[contourOrdinal] is { } pin)
            return pin;
        var point = shape.ClosestPointTo(approach, out var entity);
        return Entry(contourOrdinal, shape.Entities.IndexOf(entity), point);
    }

    /// <summary>
    /// Closest native point first, then source-order vertices/midpoints or eight circle
    /// angles (0 through 315 degrees). Geometrical duplicates keep their first entity.
    /// Emit before validating actual paths: circle rounding/clamping may move this point.
    /// A pinned contour has its pinned entry only.
    /// </summary>
    public IReadOnlyList<ContourChoice> Entries(int contourOrdinal, Vector approach, int maxEntries = 16, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        if (maxEntries <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxEntries));
        var shape = GetShape(contourOrdinal);
        if (pinned[contourOrdinal] is { } pin)
            return new[] { pin };
        var entries = new List<ContourChoice>();
        entries.Add(ClosestEntry(contourOrdinal, approach));
        for (var i = 0; i < shape.Entities.Count && entries.Count < maxEntries; i++)
        {
            token.ThrowIfCancellationRequested();
            var entity = shape.Entities[i];
            if (entity is Circle circle)
            {
                for (var angle = 0; angle < 8 && entries.Count < maxEntries; angle++)
                    Add(i, circle.Center + new Vector(System.Math.Cos(angle * System.Math.PI / 4),
                        System.Math.Sin(angle * System.Math.PI / 4)) * circle.Radius);
            }
            else
            {
                Add(i, Start(entity));
                Add(i, entity is Line line ? line.MidPoint : ((Arc)entity).MidPoint());
                Add(i, End(entity));
            }
        }
        token.ThrowIfCancellationRequested();
        return entries.AsReadOnly();

        void Add(int entityOrdinal, Vector point)
        {
            token.ThrowIfCancellationRequested();
            if (entries.Count < maxEntries && !entries.Any(e => e.Point.DistanceTo(point) <= PostVerificationGeometry.Epsilon))
                entries.Add(Entry(contourOrdinal, entityOrdinal, point));
        }
    }

    /// <summary>
    /// Every contour with a native entity within <paramref name="tolerance"/> of
    /// <paramref name="point"/>, in ordinal order, each with its closest entity and the exact
    /// closest native point on it. A locating aid for drawing pins, not an entry choice.
    /// </summary>
    internal IReadOnlyList<(int ContourOrdinal, int EntityOrdinal, Vector Point)> ContoursNear(Vector point, double tolerance)
    {
        PostVerificationGeometry.Validate(point);
        var found = new List<(int, int, Vector)>();
        for (var ordinal = 0; ordinal < Count; ordinal++)
        {
            var shape = shapes[ordinal];
            var closest = shape.ClosestPointTo(point, out var entity);
            if (closest.DistanceTo(point) <= tolerance)
                found.Add((ordinal, shape.Entities.IndexOf(entity), closest));
        }
        return found;
    }

    public ContourChoice Entry(int contourOrdinal, int entityOrdinal, Vector point)
    {
        var choice = new ContourChoice(contourOrdinal, entityOrdinal, point) { Owner = this };
        ValidateChoice(choice);
        return choice;
    }

    /// <summary>
    /// The uncapped preferred automatic start catalogue for one contour, in preference then
    /// contour-travel order: convex corners of the contour's own winding, then straight-edge
    /// midpoints, then tangent line/arc joints. Reflex and cusp vertices, collinear
    /// line/line splits, circles and interior points never appear; each geometric point is
    /// reported once, keeping the most preferred kind. A pure-arc contour can have no
    /// preferred point at all — <see cref="AutomaticEntryCandidatesWithFallbacks"/> supplies
    /// those. Manual entry through <see cref="Entry"/> / <see cref="ClosestEntry"/> is
    /// unaffected.
    /// </summary>
    internal IReadOnlyList<ContourEntryCandidate> AutomaticEntryCandidates(int contourOrdinal, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        var shape = GetShape(contourOrdinal);
        if (pinned[contourOrdinal] is { } pin)
            return new[] { new ContourEntryCandidate(pin, AutomaticEntryKind.Pinned) };
        if (IsSingleCircle(shape))
            throw new ArgumentException("Circles have no preferred corners or joints; use the fallback catalogue.");
        return MergeByGeometry(PreferredCandidates(shape, contourOrdinal, token));
    }

    /// <summary>
    /// The complete uncapped automatic start catalogue: the preferred points of
    /// <see cref="AutomaticEntryCandidates"/> followed by tier-3 fallbacks — native arc
    /// midpoints, near-convex-corner points on straight edges, the eight compass points of a
    /// whole circle, and the exact target-facing closest point toward
    /// <paramref name="lookAhead"/> (pass the arrival point there when there is no next cut).
    /// A pure-circle contour therefore yields compass points instead of refusing. Every
    /// fallback passes the same reflex/cusp exclusion and geometric duplicate merge as the
    /// preferred tier; fallbacks never replace a preferred point at the same geometry. A
    /// pinned contour's catalogue is its pinned entry alone: nothing may relocate it.
    /// </summary>
    internal IReadOnlyList<ContourEntryCandidate> AutomaticEntryCandidatesWithFallbacks(
        int contourOrdinal,
        Vector? lookAhead = null,
        CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        var shape = GetShape(contourOrdinal);
        if (pinned[contourOrdinal] is { } pin)
            return new[] { new ContourEntryCandidate(pin, AutomaticEntryKind.Pinned) };
        var all = IsSingleCircle(shape)
            ? new List<ContourEntryCandidate>()
            : PreferredCandidates(shape, contourOrdinal, token);
        all.AddRange(FallbackCandidates(shape, contourOrdinal, lookAhead, token));
        return MergeByGeometry(all);
    }

    private static bool IsSingleCircle(Shape shape) =>
        shape.Entities.Count == 1 && shape.Entities[0] is Circle;

    /// <summary>Preferred tier: convex corners, straight midpoints, tangent joints.</summary>
    private List<ContourEntryCandidate> PreferredCandidates(Shape shape, int contourOrdinal, CancellationToken token)
        => CataloguePoints(shape, token)
            .Select(p => new ContourEntryCandidate(Entry(contourOrdinal, p.EntityOrdinal, p.Point), p.Kind))
            .ToList();

    /// <summary>
    /// Tier-3 fallbacks for one contour, each already run through the reflex/cusp exclusion:
    /// native arc midpoints, the eight compass points of whole circles, near-convex-corner
    /// insets on straight edges, and the exact target-facing closest point toward
    /// <paramref name="lookAhead"/>. No ranking and no lead-safety verdict here.
    /// </summary>
    private List<ContourEntryCandidate> FallbackCandidates(
        Shape shape,
        int contourOrdinal,
        Vector? lookAhead,
        CancellationToken token)
    {
        var fallbacks = new List<ContourEntryCandidate>();
        var lead = ApplicableLeadInLength(contourOrdinal);
        var count = shape.Entities.Count;

        for (var i = 0; i < count; i++)
        {
            token.ThrowIfCancellationRequested();
            switch (shape.Entities[i])
            {
                case Arc arc:
                    // The native midpoint of an arc (exact native API, never tessellation).
                    fallbacks.Add(Fallback(i, arc.MidPoint(), AutomaticEntryKind.ArcMidpoint));
                    break;
                case Circle circle:
                    // The legacy eight compass points, same native construction.
                    for (var angle = 0; angle < 8; angle++)
                        fallbacks.Add(Fallback(i, circle.Center + new Vector(
                            System.Math.Cos(angle * System.Math.PI / 4),
                            System.Math.Sin(angle * System.Math.PI / 4)) * circle.Radius,
                            AutomaticEntryKind.CircleCompass));
                    break;
            }
        }

        // Near-convex-corner fallbacks: about twice the applicable lead-in length back from
        // each convex corner along each incident STRAIGHT edge, only when strictly inside
        // that edge. Short edges simply omit the point; it never extrapolates past an edge
        // endpoint and so never lands on the reflex/cusp vertex at the far end.
        if (lead > 0 && !(count == 1 && shape.Entities[0] is Circle))
        {
            foreach (var corner in CataloguePoints(shape, token)
                         .Where(p => p.Kind == AutomaticEntryKind.ConvexCorner))
            {
                token.ThrowIfCancellationRequested();
                // Back INTO each incident edge from the corner: the edge ending at the
                // corner retreats against its own travel, the edge starting at the corner
                // advances along its own travel.
                AddInset(corner.EntityOrdinal, inward: false);
                AddInset((corner.EntityOrdinal + 1) % count, inward: true);
            }
        }

        if (lookAhead != null)
        {
            token.ThrowIfCancellationRequested();
            PostVerificationGeometry.Validate(lookAhead.Value);
            var facing = ClosestEntry(contourOrdinal, lookAhead.Value);
            // A raw closest point may land exactly on a reflex/cusp vertex; automatic
            // selection must never sneak a forbidden inside corner back in, so drop it.
            if (!IsForbiddenVertex(shape, facing.Point))
                fallbacks.Add(Fallback(facing.EntityOrdinal, facing.Point, AutomaticEntryKind.TargetFacing));
        }
        token.ThrowIfCancellationRequested();
        return fallbacks;

        void AddInset(int entityOrdinal, bool inward)
        {
            // `inward` selects along the edge's own travel from its start; without it the
            // point retreats against travel. Both ways move BACK INTO the edge from the
            // corner, which sits at the edge's end (inward=false) or start (inward=true).
            if (shape.Entities[entityOrdinal] is not Line line)
                return;
            var direction = (line.EndPoint - line.StartPoint).Normalize();
            var offset = direction * (2 * lead);
            // Adjacent native endpoints may differ within contour-continuity tolerance.
            // Anchor to the selected edge itself, never the preceding edge's vertex.
            var point = inward ? line.StartPoint + offset : line.EndPoint - offset;
            // Strictly inside the edge by projection parameter (distance alone loses the
            // sign when a short edge is overshoot): never the corner, never the far
            // endpoint (where a reflex vertex might sit). Short edges omit the point.
            var t = (point.X - line.StartPoint.X) * direction.X + (point.Y - line.StartPoint.Y) * direction.Y;
            if (t <= PostVerificationGeometry.Epsilon || t >= line.Length - PostVerificationGeometry.Epsilon)
                return;
            fallbacks.Add(Fallback(entityOrdinal, point, AutomaticEntryKind.NearCorner));
        }

        ContourEntryCandidate Fallback(int entityOrdinal, Vector point, AutomaticEntryKind kind)
            => new(Entry(contourOrdinal, entityOrdinal, point), kind);
    }

    /// <summary>
    /// At equal geometric points the most preferred kind wins, independent of which entity
    /// supplied it; the winner keeps its own ordinal and point. Result order: preference,
    /// then entity ordinal.
    /// </summary>
    private static IReadOnlyList<ContourEntryCandidate> MergeByGeometry(List<ContourEntryCandidate> candidates)
    {
        var byPoint = new Dictionary<(long, long), ContourEntryCandidate>();
        var order = new List<(long, long)>();
        foreach (var candidate in candidates)
        {
            var key = candidate.GeometryKey;
            if (!byPoint.TryGetValue(key, out var existing))
            {
                byPoint[key] = candidate;
                order.Add(key);
            }
            else if (candidate.Kind < existing.Kind)
                byPoint[key] = candidate;
        }
        return order
            .OrderBy(key => byPoint[key].Kind)
            .ThenBy(key => byPoint[key].Choice.EntityOrdinal)
            .Select(key => byPoint[key])
            .ToList();
    }

    /// <summary>
    /// True when <paramref name="point"/> sits on a vertex of the contour that is reflex or
    /// cusp from either travel direction — a point automatic selection must never emit.
    /// </summary>
    private bool IsForbiddenVertex(Shape shape, Vector point)
    {
        for (var i = 0; i < shape.Entities.Count; i++)
        {
            if (shape.Entities[i] is Circle)
                continue; // A whole circle has no vertex.
            var vertex = End(shape.Entities[i]);
            if (vertex.DistanceTo(point) > PostVerificationGeometry.Epsilon)
                continue;
            if (ContourCuttingStrategy.TryClassifyAutomaticStartCorner(shape, vertex, shape.Entities[i], out var corner)
                && corner.Kind is ContourCuttingStrategy.CornerKind.Reflex or ContourCuttingStrategy.CornerKind.Cusp)
                return true;
        }
        return false;
    }

    /// <summary>
    /// The applicable lead-in length for a contour following the emitter's own selection:
    /// the perimeter (last shape) is external, anything else internal; non-length lead-in
    /// styles contribute 0, which omits the near-corner fallback instead of approximating.
    /// </summary>
    private double ApplicableLeadInLength(int contourOrdinal)
        => (contourOrdinal == PerimeterOrdinal
                ? parameters.ExternalLeadIn
                : parameters.InternalLeadIn) switch
        {
            LineLeadIn line => line.Length,
            LineLineLeadIn lineLine => lineLine.Length1,
            _ => 0,
        };

    /// <summary>
    /// The preferred catalogue points for one contour: for each entity, its convex corner
    /// (end vertex, classified from the contour's own winding) and its straight-edge midpoint,
    /// plus tangent line/arc joints. Reflex, cusp and collinear-split vertices contribute
    /// nothing; a single whole circle yields no points at all.
    /// </summary>
    private List<(int EntityOrdinal, Vector Point, AutomaticEntryKind Kind)> CataloguePoints(Shape shape, CancellationToken token)
    {
        var found = new List<(int, Vector, AutomaticEntryKind)>();
        var count = shape.Entities.Count;
        if (count == 1 && shape.Entities[0] is Circle)
        {
            // A whole circle has no corners or joints; only the fallback tier applies.
            return found;
        }

        if (count < 2)
            throw new ArgumentException("Contour has no vertex to classify.");
        for (var i = 0; i < count; i++)
        {
            token.ThrowIfCancellationRequested();
            var entity = shape.Entities[i];

            // Vertex reached by travelling along entity i (its end point), reported under
            // entity i. The closed contour guarantees every vertex appears exactly once
            // this way; each is classified from the contour's own winding.
            var vertex = End(entity);
            if (ContourCuttingStrategy.TryClassifyAutomaticStartCorner(shape, vertex, entity, out var corner))
            {
                switch (corner.Kind)
                {
                    case ContourCuttingStrategy.CornerKind.Convex:
                        found.Add((i, vertex, AutomaticEntryKind.ConvexCorner));
                        break;
                    case ContourCuttingStrategy.CornerKind.Smooth
                        when entity is Line && Next(i) is Arc:
                        // A line leaving into an arc: the tangent joint. The reverse travel
                        // order classifies the same joint from the arc, matched below.
                        found.Add((i, vertex, AutomaticEntryKind.TangentJoint));
                        break;
                    // Reflex, cusp, collinear splits (smooth line→line) and arc→line joins
                    // of a plain straight edge are not preferred automatic starts here.
                    case ContourCuttingStrategy.CornerKind.Smooth
                        when entity is Arc && Next(i) is Line:
                        found.Add((i, vertex, AutomaticEntryKind.TangentJoint));
                        break;
                }
            }

            if (entity is Line line)
                found.Add((i, line.MidPoint, AutomaticEntryKind.StraightMidpoint));
        }
        return found;

        Entity Next(int index) => shape.Entities[(index + 1) % count];
    }

    /// <summary>Emits every contour once in caller order, holes before perimeter, with scribes once.</summary>
    public Program Emit(IReadOnlyList<ContourChoice> choices)
    {
        Validate(choices, true);
        return EmitPrefix(choices);
    }

    /// <summary>
    /// Diagnostic seam: emit exactly ONE owned contour — with its normal lead-in and
    /// lead-out and its ORIGINAL contour type (the perimeter keeps External even when no
    /// holes precede it) — so a candidate's emitted leads can be validated before the hole
    /// choices exist. The result is a throwaway probe, not a plan: it must never be
    /// installed on a Part or accepted as complete output. Normal <see cref="Emit"/> and
    /// <see cref="EmitPrefix"/> keep the perimeter-last rule untouched.
    /// </summary>
    internal Program EmitCandidateForValidation(ContourChoice choice)
    {
        if (choice == null || !ReferenceEquals(choice.Owner, this))
            throw new ArgumentException("Foreign contour choice.");
        ValidateChoice(choice);
        // Same owned clones as a real emission; the source shapes/settings are never used
        // directly, so the probe cannot drift the preparation or mutate it.
        return new ContourCuttingStrategy { Parameters = parameters }.EmitCandidateIsolated(
            shapes.Select(s => (Shape)s.Clone()).ToArray(), scribes.Select(e => e.Clone()).ToList(), choice,
            contourParameters);
    }

    // Each prefix is a standalone owned program, including the same scribes and break cuts once.
    internal Program EmitPrefix(IReadOnlyList<ContourChoice> choices)
    {
        Validate(choices, false);
        return new ContourCuttingStrategy { Parameters = parameters }.EmitPrepared(
            shapes.Select(s => (Shape)s.Clone()).ToArray(), scribes.Select(e => e.Clone()).ToList(), choices,
            contourParameters, breakCuts.Select(s => (Shape)s.Clone()).ToList(),
            tabbedCutouts.Select(s => (Shape)s.Clone()).ToList());
    }

    /// <summary>Open break cuts this preparation cuts before its contours.</summary>
    public int BreakCutCount => breakCuts.Count;

    /// <summary>Tabbed cutouts this preparation cuts, as pieces, after its break cuts and before its contours.</summary>
    public int TabbedCutoutCount => tabbedCutouts.Count;

    /// <summary>Open pieces the tabbed cutouts are cut in, each a lead-in and cut group of its own.</summary>
    public int TabbedPieceCount => tabbedCutouts.Sum(c => ContourCuttingStrategy.TabbedCutoutPieces(c).Count);

    internal void ValidateCompleteChoices(IReadOnlyList<ContourChoice> choices) => Validate(choices, true);

    // Build expected-emission metadata independently of the selected payload BEFORE
    // replay. Final verification only reads this immutable execution and clean geometry.
    internal SelectedContourProgram CaptureSelectedProgram(IReadOnlyList<ContourChoice> choices,
        Vector location, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var expected = ExecutionMotionReader.ReadSupported(Emit(choices), location, null, token);
        // Only the outside is tabbed, with its own pin's tab when it is pinned.
        var perimeter = ParametersFor(PerimeterOrdinal);
        return new(this, choices, expected, perimeter.TabsEnabled ? perimeter.TabConfig.Size : 0);
    }

    private void Validate(IReadOnlyList<ContourChoice> choices, bool complete)
    {
        if (choices == null || (complete && choices.Count != Count) || choices.Count > Count)
            throw new ArgumentException("Every contour must be represented exactly once.");
        var visited = new HashSet<int>();
        foreach (var choice in choices)
        {
            ValidateChoice(choice);
            if (!visited.Add(choice.ContourOrdinal) || choice.ContourOrdinal == PerimeterOrdinal && visited.Count != Count)
                throw new ArgumentException("Duplicate contour or perimeter before its internal contours.");
        }
    }

    private void ValidateChoice(ContourChoice choice)
    {
        if (choice == null || !ReferenceEquals(choice.Owner, this))
            throw new ArgumentException("Foreign contour choice.");
        PostVerificationGeometry.Validate(choice.Point);
        var shape = GetShape(choice.ContourOrdinal);
        if (choice.EntityOrdinal < 0 || choice.EntityOrdinal >= shape.Entities.Count)
            throw new ArgumentException("Foreign entity ordinal.");
        var distance = shape.Entities[choice.EntityOrdinal].ClosestPointTo(choice.Point).DistanceTo(choice.Point);
        if (!double.IsFinite(distance) || distance > PostVerificationGeometry.Epsilon)
            throw new ArgumentException("Entry is not on the selected native entity.");
        // A pinned contour is cut exactly as pinned: no other entry may be emitted for it.
        if (pinned[choice.ContourOrdinal] is { } pin && pin != choice)
            throw new ArgumentException("A pinned contour keeps its drawing pin's entry.");
    }

    private Shape GetShape(int ordinal) => ordinal < 0 || ordinal >= Count
        ? throw new ArgumentException("Foreign contour ordinal.") : shapes[ordinal];

    /// <summary>
    /// Representative POINTS per contour ordinal (bounding-box centre) for hole routing
    /// only — the S10 <c>CuttingHoleOrder</c> proxy. The perimeter has no entry; it is the
    /// route's fixed endpoint, not a stop. These are ordering proxies, never cut points.
    /// </summary>
    internal IReadOnlyList<Vector?> HoleCentres(CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        var centres = new Vector?[Count];
        for (var contour = 0; contour < PerimeterOrdinal; contour++)
        {
            token.ThrowIfCancellationRequested();
            centres[contour] = GetShape(contour).BoundingBox.Center;
        }
        return centres;
    }

    /// <summary>
    /// Whether <paramref name="point"/> lies inside the internal contour <paramref name="contourOrdinal"/>.
    /// A routing hint only (chorded at the overlap check's tolerance), never a clearance verdict.
    /// </summary>
    internal bool HoleContains(int contourOrdinal, Vector point)
    {
        var shape = GetShape(contourOrdinal);
        return contourOrdinal != PerimeterOrdinal && shape.BoundingBox.Contains(point)
            && shape.ToPolygonWithTolerance(PlateOverlapAnalyzer.ChordTolerance).ContainsPoint(point);
    }

    private static Vector Start(Entity entity) => entity is Line line ? line.StartPoint : ((Arc)entity).StartPoint();
    private static Vector End(Entity entity) => entity is Line line ? line.EndPoint : ((Arc)entity).EndPoint();

    private static Program CopyForGeometry(Program source, CancellationToken token)
    {
        var copies = new Dictionary<Program, Program>(ReferenceEqualityComparer.Instance);
        return Copy(source);

        Program Copy(Program current)
        {
            token.ThrowIfCancellationRequested();
            if (copies.TryGetValue(current, out var existing))
                return existing;
            var copy = new Program(current.Mode);
            copies.Add(current, copy);
            foreach (var code in current.Codes)
            {
                token.ThrowIfCancellationRequested();
                var ownedCode = code.Clone();
                if (ownedCode is SubProgramCall call)
                    call.BindProgram(Copy(((SubProgramCall)code).Program));
                copy.Codes.Add(ownedCode);
            }
            copy.Mode = Mode.Incremental;
            return copy;
        }
    }

}
