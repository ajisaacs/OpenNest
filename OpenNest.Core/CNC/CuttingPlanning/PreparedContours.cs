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

    private PreparedContours(Shape[] shapes, List<Entity> scribes, CuttingParameters parameters)
    {
        this.shapes = shapes;
        this.scribes = scribes;
        this.parameters = parameters;
    }

    public int Count => shapes.Length;
    public int PerimeterOrdinal => Count - 1;

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
        var current = new Shape();
        foreach (var entity in geometryProgram.ToGeometry())
        {
            token.ThrowIfCancellationRequested();
            if (entity.GetType() != typeof(Line) && entity.GetType() != typeof(Arc) && entity.GetType() != typeof(Circle))
                throw new NotSupportedException("Unsupported native geometry.");
            if (entity.Layer == SpecialLayers.Rapid || entity.Layer == SpecialLayers.Scribe)
            {
                if (current.Entities.Count != 0)
                    throw new ArgumentException("Preparation requires closed execution contours.");
                if (entity.Layer == SpecialLayers.Scribe)
                    scribes.Add(entity);
                continue;
            }
            if (entity.Layer != SpecialLayers.Cut && entity.Layer != SpecialLayers.Display)
                throw new ArgumentException("Preparation requires clean cut/display contours.");
            if (entity.Length <= PostVerificationGeometry.Epsilon || !double.IsFinite(entity.Length))
                throw new ArgumentException("Degenerate contour entity.");
            if (entity is Circle)
            {
                if (current.Entities.Count != 0)
                    throw new ArgumentException("Circle interrupts an open contour.");
                current.Entities.Add(entity);
                FinishContour();
                continue;
            }
            if (current.Entities.Count > 0 && End(current.Entities[^1]).DistanceTo(Start(entity)) > PostVerificationGeometry.Epsilon)
                throw new ArgumentException("Discontinuous native contour.");
            current.Entities.Add(entity);
            if (Start(current.Entities[0]).DistanceTo(End(entity)) <= PostVerificationGeometry.Epsilon)
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
        return new(contours.ToArray(), scribes, ownedParameters);

        void FinishContour()
        {
            current.UpdateBounds();
            contours.Add(current);
            current = new Shape();
        }
    }

    public ContourChoice ClosestEntry(int contourOrdinal, Vector approach)
    {
        PostVerificationGeometry.Validate(approach);
        var shape = GetShape(contourOrdinal);
        var point = shape.ClosestPointTo(approach, out var entity);
        return Entry(contourOrdinal, shape.Entities.IndexOf(entity), point);
    }

    /// <summary>
    /// Closest native point first, then source-order vertices/midpoints or eight circle
    /// angles (0 through 315 degrees). Geometrical duplicates keep their first entity.
    /// Emit before validating actual paths: circle rounding/clamping may move this point.
    /// </summary>
    public IReadOnlyList<ContourChoice> Entries(int contourOrdinal, Vector approach, int maxEntries = 16, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        if (maxEntries <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxEntries));
        var shape = GetShape(contourOrdinal);
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
    /// preferred point at all — the S04 fallback catalogue supplies those. Manual entry
    /// through <see cref="Entry"/> / <see cref="ClosestEntry"/> is unaffected.
    /// </summary>
    internal IReadOnlyList<ContourEntryCandidate> AutomaticEntryCandidates(int contourOrdinal, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        var shape = GetShape(contourOrdinal);
        if (shape.Entities.Count == 1 && shape.Entities[0] is Circle)
            throw new ArgumentException("Circles have no preferred corners or joints; use the fallback catalogue.");
        if (shape.Entities.Count < 2)
            throw new ArgumentException("Contour has no vertex to classify.");

        // Collect EVERY eligible candidate first, then deduplicate geometrically; at equal
        // points the most preferred kind wins regardless of which entity supplied it.
        var found = new List<ContourEntryCandidate>();
        var count = shape.Entities.Count;
        for (var i = 0; i < count; i++)
        {
            token.ThrowIfCancellationRequested();
            var entity = shape.Entities[i];

            // Vertex reached by travelling along entity i (its end point), reported under
            // entity i. The closed contour guarantees every vertex appears exactly once this
            // way; each is classified from the contour's own winding.
            var vertex = End(entity);
            if (ContourCuttingStrategy.TryClassifyAutomaticStartCorner(shape, vertex, entity, out var corner))
            {
                switch (corner.Kind)
                {
                    case ContourCuttingStrategy.CornerKind.Convex:
                        Add(i, vertex, AutomaticEntryKind.ConvexCorner);
                        break;
                    case ContourCuttingStrategy.CornerKind.Smooth
                        when entity is Line && Next(i) is Arc:
                        // A line leaving into an arc: the tangent joint. The reverse travel
                        // order classifies the same joint from the arc, matched below.
                        Add(i, vertex, AutomaticEntryKind.TangentJoint);
                        break;
                    // Reflex, cusp, collinear splits (smooth line→line) and arc→line joins
                    // of a plain straight edge are not preferred automatic starts here.
                    case ContourCuttingStrategy.CornerKind.Smooth
                        when entity is Arc && Next(i) is Line:
                        Add(i, vertex, AutomaticEntryKind.TangentJoint);
                        break;
                }
            }

            if (entity is Line line)
                Add(i, line.MidPoint, AutomaticEntryKind.StraightMidpoint);
        }
        token.ThrowIfCancellationRequested();

        // Deduplicate: at equal points the most preferred metadata wins, independent of
        // which entity supplied it; the winner's own ordinal and point are kept.
        var byPoint = new Dictionary<(long, long), ContourEntryCandidate>();
        var order = new List<(long, long)>();
        foreach (var candidate in found)
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

        Entity Next(int index) => shape.Entities[(index + 1) % count];

        void Add(int entityOrdinal, Vector point, AutomaticEntryKind kind)
        {
            found.Add(new ContourEntryCandidate(Entry(contourOrdinal, entityOrdinal, point), kind));
        }
    }

    /// <summary>Emits every contour once in caller order, holes before perimeter, with scribes once.</summary>
    public Program Emit(IReadOnlyList<ContourChoice> choices)
    {
        Validate(choices, true);
        return EmitPrefix(choices);
    }

    // Each prefix is a standalone owned program, including the same scribes once.
    internal Program EmitPrefix(IReadOnlyList<ContourChoice> choices)
    {
        Validate(choices, false);
        return new ContourCuttingStrategy { Parameters = parameters }.EmitPrepared(
            shapes.Select(s => (Shape)s.Clone()).ToArray(), scribes.Select(e => e.Clone()).ToList(), choices);
    }

    internal void ValidateCompleteChoices(IReadOnlyList<ContourChoice> choices) => Validate(choices, true);

    // Build expected-emission metadata independently of the selected payload BEFORE
    // replay. Final verification only reads this immutable execution and clean geometry.
    internal SelectedContourProgram CaptureSelectedProgram(IReadOnlyList<ContourChoice> choices,
        Vector location, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var expected = ExecutionMotionReader.ReadSupported(Emit(choices), location, null, token);
        return new(this, choices, expected, parameters.TabsEnabled ? parameters.TabConfig.Size : 0);
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
    }

    private Shape GetShape(int ordinal) => ordinal < 0 || ordinal >= Count
        ? throw new ArgumentException("Foreign contour ordinal.") : shapes[ordinal];

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
