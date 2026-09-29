using System.Collections.Immutable;
using OpenNest.CNC;
using OpenNest.Converters;
using OpenNest.Geometry;
using OpenNest.Math;

namespace OpenNest.Reporting;

internal static class NestReportGeometry
{
    internal static ReportGeometry Capture(Program? program, Vector location, string context)
    {
        ValidatePoint(location, context);
        var current = new Vector();
        // ConvertProgram recurses without cycle/null guards and silently skips unknown codes.
        // Validate first; never clone through SubProgramCall's rotating setters.
        ValidateProgram(program, ref current, new HashSet<Program>(ReferenceEqualityComparer.Instance), context);
        var entities = ConvertProgram.ToGeometry(program!);
        var contours = ImmutableArray.CreateBuilder<ReportContour>();
        var segments = ImmutableArray.CreateBuilder<ReportSegment>();
        var left = double.PositiveInfinity;
        var bottom = double.PositiveInfinity;
        var right = double.NegativeInfinity;
        var top = double.NegativeInfinity;

        void Flush()
        {
            if (segments.Count == 0)
                return;
            var closed = SamePoint(segments[0].Start, segments[^1].End);
            contours.Add(new ReportContour(segments.ToImmutable(), closed));
            segments.Clear();
        }

        foreach (var entity in entities)
        {
            if (!SpecialLayers.IsMaterial(entity.Layer)
                || entity.Layer == SpecialLayers.Leadin || entity.Layer == SpecialLayers.Leadout)
            {
                Flush();
                continue;
            }

            ReportSegment segment;
            switch (entity)
            {
                case Line line:
                    if (line.StartPoint.X == line.EndPoint.X && line.StartPoint.Y == line.EndPoint.Y)
                        continue;
                    segment = new ReportSegment(Point(line.StartPoint, location, context),
                        Point(line.EndPoint, location, context), null, 0, 0, 0);
                    break;
                case Arc arc:
                    segment = new ReportSegment(Point(arc.StartPoint(), location, context),
                        Point(arc.EndPoint(), location, context), Point(arc.Center, location, context),
                        arc.Radius, arc.StartAngle, arc.SweepAngle() * (arc.IsReversed ? -1 : 1));
                    break;
                case Circle circle:
                    Flush();
                    var start = Point(new Vector(circle.Center.X + circle.Radius, circle.Center.Y), location, context);
                    segment = new ReportSegment(start, start, Point(circle.Center, location, context),
                        circle.Radius, 0, circle.Rotation == RotationType.CW ? -2 * System.Math.PI : 2 * System.Math.PI);
                    break;
                default:
                    throw Error(context, $"unsupported geometry '{entity.GetType().Name}'.");
            }

            if (segment.Center != null && (!double.IsFinite(segment.Radius) || segment.Radius <= 0
                || !double.IsFinite(segment.StartAngle) || !double.IsFinite(segment.SweepAngle)
                || segment.SweepAngle == 0 || System.Math.Abs(segment.SweepAngle) > 2 * System.Math.PI))
                throw Error(context, "invalid arc/circle data.");
            if (segments.Count > 0 && !SamePoint(segments[^1].End, segment.Start))
                Flush();
            segments.Add(segment);
            if (SamePoint(segments[0].Start, segment.End))
                Flush();

            var box = entity.BoundingBox;
            var low = Point(new Vector(box.Left, box.Bottom), location, context);
            var high = Point(new Vector(box.Right, box.Top), location, context);
            left = System.Math.Min(left, low.X);
            bottom = System.Math.Min(bottom, low.Y);
            right = System.Math.Max(right, high.X);
            top = System.Math.Max(top, high.Y);
        }
        Flush();
        if (contours.Count == 0)
            throw Error(context, "missing drawable material geometry.");
        if (!double.IsFinite(right - left) || !double.IsFinite(top - bottom))
            throw Error(context, "geometry bounds are not finite.");
        return new ReportGeometry(contours.ToImmutable(), new ReportBounds(left, bottom, right, top));
    }

    private static void ValidateProgram(Program? program, ref Vector current,
        HashSet<Program> active, string context)
    {
        if (program?.Codes == null)
            throw Error(context, "missing program or subprogram reference.");
        if (!active.Add(program))
            throw Error(context, "cyclic subprogram reference.");
        if (active.Count > 256)
            throw Error(context, "unsupported subprogram nesting depth (maximum 256).");
        if (!double.IsFinite(program.Rotation) || !Enum.IsDefined(program.Mode))
            throw Error(context, "invalid program transform or coordinate mode.");
        var origin = current;
        for (var index = 0; index < program.Codes.Count; index++)
        {
            var code = program.Codes[index];
            var codeContext = $"{context}, code {index + 1}";
            switch (code)
            {
                case SubProgramCall call when code.Type == CodeType.SubProgramCall:
                    ValidatePoint(call.Offset, codeContext);
                    if (!double.IsFinite(call.Rotation))
                        throw Error(codeContext, "invalid subprogram rotation.");
                    current = origin + call.Offset;
                    ValidatePoint(current, codeContext);
                    ValidateProgram(call.Program, ref current, active, $"{codeContext}, subprogram {call.Id}");
                    break;
                case Motion motion when code is LinearMove && code.Type == CodeType.LinearMove
                    || code is RapidMove && code.Type == CodeType.RapidMove
                    || code is ArcMove && code.Type == CodeType.ArcMove:
                    ValidatePoint(motion.EndPoint, codeContext);
                    var end = program.Mode == Mode.Incremental ? current + motion.EndPoint : motion.EndPoint;
                    ValidatePoint(end, codeContext);
                    if (motion is LinearMove linear && !Enum.IsDefined(linear.Layer))
                        throw Error(codeContext, "unsupported material layer.");
                    if (motion is ArcMove arc)
                    {
                        ValidatePoint(arc.CenterPoint, codeContext);
                        if (!Enum.IsDefined(arc.Rotation) || !Enum.IsDefined(arc.Layer))
                            throw Error(codeContext, "invalid arc rotation or layer.");
                        var center = program.Mode == Mode.Incremental ? current + arc.CenterPoint : arc.CenterPoint;
                        ValidatePoint(center, codeContext);
                        var startRadius = current.DistanceTo(center);
                        var endRadius = end.DistanceTo(center);
                        if (!double.IsFinite(startRadius) || !double.IsFinite(endRadius)
                            || startRadius <= 0 || endRadius <= 0
                            || System.Math.Abs(startRadius - endRadius) > Tolerance.Epsilon)
                            throw Error(codeContext, "invalid arc/circle radius or inconsistent endpoints.");
                    }
                    current = end;
                    break;
                case Comment when code.Type == CodeType.Comment:
                case Feedrate when code.Type == CodeType.SetFeedrate:
                case Kerf when code.Type == CodeType.SetKerf:
                    break;
                default:
                    throw Error(codeContext, code == null ? "missing code." : $"unsupported code '{code.GetType().Name}'.");
            }
        }
        active.Remove(program);
    }

    private static ReportPoint Point(Vector point, Vector location, string context)
    {
        var translated = point + location;
        ValidatePoint(translated, context);
        return new ReportPoint(translated.X, translated.Y);
    }

    private static void ValidatePoint(Vector point, string context)
    {
        if (!double.IsFinite(point.X) || !double.IsFinite(point.Y))
            throw Error(context, "nonfinite geometry coordinate.");
    }

    // Only absorb floating-point trig roundoff, never ShapeBuilder's welding tolerance.
    // A deliberate tab/rapid interruption is never joined by contour construction.
    private static bool SamePoint(ReportPoint first, ReportPoint second) =>
        System.Math.Abs(first.X - second.X) <= 1e-12 && System.Math.Abs(first.Y - second.Y) <= 1e-12;

    private static InvalidOperationException Error(string context, string message) => new($"{context}: {message}");
}
