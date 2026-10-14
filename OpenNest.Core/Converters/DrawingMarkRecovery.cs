using System.Collections.Generic;
using System.Linq;
using OpenNest.CNC;
using OpenNest.Geometry;
using OpenNest.Math;

namespace OpenNest.Converters;

/// <summary>
/// Puts etch marks back on drawings that lost them. Nests converted from PEP kept etch marks
/// only in each placed part's saved cutting program; the drawing itself had none, so removing
/// lead-ins (which rebuilds the part from its drawing) silently removed the marks too.
/// </summary>
public static class DrawingMarkRecovery
{
    private const double MatchTolerance = 0.001;

    /// <summary>
    /// For each drawing without etch marks whose saved placed programs all carry the same marks,
    /// adds those marks to the drawing in its own frame and refreshes the drawing's clean placed
    /// parts. Saved programs are never changed. Drawings whose saved programs disagree, or that
    /// already have marks, are left alone. Returns the number of drawings that were given marks.
    /// </summary>
    public static int Apply(Nest nest)
    {
        if (nest == null)
            return 0;

        var parts = nest.Plates.SelectMany(plate => plate.Parts).ToList();
        var recovered = 0;
        foreach (var group in parts.GroupBy(part => part.BaseDrawing, ReferenceEqualityComparer.Instance))
        {
            var drawing = (Drawing)group.Key;
            if (Apply(drawing, group.ToList()))
                recovered++;
        }
        return recovered;
    }

    /// <summary>Recovers marks for one drawing from its placed parts; true when marks were added.</summary>
    public static bool Apply(Drawing drawing, IReadOnlyList<Part> placedParts)
    {
        if (drawing?.Program == null || drawing.IsCutOff || placedParts == null)
            return false;
        // An incremental call's end position is not tracked by Program.EndPoint, so only
        // flat drawings (which is what conversion produces) can be extended safely.
        if (HasMarks(drawing.Program) || drawing.Program.Codes.Any(code => code is SubProgramCall))
            return false;

        var saved = placedParts.Where(part => ReferenceEquals(part.BaseDrawing, drawing) && part.HasManualLeadIns)
            .ToList();
        if (saved.Count == 0)
            return false;

        var marks = MarksInDrawingFrame(saved[0]);
        if (marks.Count == 0 || saved.Skip(1).Any(part => !SameMarks(marks, MarksInDrawingFrame(part))))
            return false;

        AppendMarks(drawing.Program, marks);
        drawing.UpdateArea();
        drawing.RecomputeCanonicalAngle();

        foreach (var part in placedParts.Where(part => ReferenceEquals(part.BaseDrawing, drawing)
                     && !part.HasManualLeadIns))
            part.Update();
        return true;
    }

    private static bool HasMarks(Program program) =>
        program.Codes.Any(code => code is LinearMove { Layer: LayerType.Scribe } or ArcMove { Layer: LayerType.Scribe }
            || code is SubProgramCall { Program: not null } call && HasMarks(call.Program));

    /// <summary>
    /// The part's marks as they would sit in its drawing: <see cref="Part.RemoveLeadIns"/>
    /// rebuilds the part as the drawing turned by <see cref="Part.Rotation"/>, so undo that turn.
    /// </summary>
    private static List<Entity> MarksInDrawingFrame(Part part)
    {
        var marks = ConvertProgram.ToGeometry(part.Program)
            .Where(entity => entity.Layer == SpecialLayers.Scribe)
            .ToList();
        if (!Tolerance.IsEqualTo(part.Rotation, 0))
            foreach (var mark in marks)
                mark.Rotate(-part.Rotation);
        return marks;
    }

    private static bool SameMarks(List<Entity> expected, List<Entity> actual)
    {
        if (expected.Count != actual.Count)
            return false;
        var unmatched = actual.ToList();
        foreach (var mark in expected)
        {
            var index = unmatched.FindIndex(other => SameMark(mark, other));
            if (index < 0)
                return false;
            unmatched.RemoveAt(index);
        }
        return true;
    }

    // Direction does not matter for an etch: both marks must lie on each other.
    private static bool SameMark(Entity a, Entity b) =>
        a.Type == b.Type && LiesOn(Samples(a), b) && LiesOn(Samples(b), a);

    private static bool LiesOn(List<Vector> points, Entity entity) =>
        points.Count > 0 && points.All(point => entity.ClosestPointTo(point).DistanceTo(point) <= MatchTolerance);

    private static List<Vector> Samples(Entity entity) => entity switch
    {
        Line line => new List<Vector>
        {
            line.StartPoint,
            line.EndPoint,
            new((line.StartPoint.X + line.EndPoint.X) / 2, (line.StartPoint.Y + line.EndPoint.Y) / 2),
        },
        Arc arc => new List<Vector> { arc.StartPoint(), arc.EndPoint(), arc.MidPoint() },
        Circle circle => new List<Vector>
        {
            new(circle.Center.X + circle.Radius, circle.Center.Y),
            new(circle.Center.X - circle.Radius, circle.Center.Y),
            new(circle.Center.X, circle.Center.Y + circle.Radius),
            new(circle.Center.X, circle.Center.Y - circle.Radius),
        },
        _ => new List<Vector>(),
    };

    /// <summary>
    /// Appends the marks after the drawing's existing motion, so the existing codes and the
    /// drawing's first-rapid origin stay exactly as they were.
    /// </summary>
    private static void AppendMarks(Program program, List<Entity> marks)
    {
        var shape = new Shape();
        shape.Entities.AddRange(marks);
        var markProgram = ConvertGeometry.ToProgram(shape);

        // Start from the drawing's end point, with an explicit rapid onto the first mark.
        var start = EndOf(program);
        var first = markProgram.Codes.OfType<Motion>().First();
        var positioned = new Program(Mode.Absolute);
        positioned.Codes.Add(new RapidMove(start));
        if (first is not RapidMove)
            positioned.Codes.Add(new RapidMove(StartOf(marks[0])));
        positioned.Codes.AddRange(markProgram.Codes);

        if (program.Mode == Mode.Incremental)
            positioned.Mode = Mode.Incremental;
        // Drop the move onto the drawing's end point; it is where the drawing already stops.
        program.Codes.AddRange(positioned.Codes.Skip(1));
    }

    // Where a flat program leaves the head (callers exclude sub-program calls).
    private static Vector EndOf(Program program)
    {
        var position = Vector.Zero;
        foreach (var motion in program.Codes.OfType<Motion>())
            position = program.Mode == Mode.Incremental ? position + motion.EndPoint : motion.EndPoint;
        return position;
    }

    private static Vector StartOf(Entity entity) => entity switch
    {
        Line line => line.StartPoint,
        Arc arc => arc.StartPoint(),
        Circle circle => new Vector(circle.Center.X + circle.Radius, circle.Center.Y),
        _ => Vector.Zero,
    };
}
