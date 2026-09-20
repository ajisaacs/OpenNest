using System;
using System.Collections.Generic;
using System.Linq;
using OpenNest.Bending;
using OpenNest.Geometry;

namespace OpenNest.IO.Bending
{
    public enum BendRepairUnits
    {
        Unspecified,
        Inches,
        Millimeters,
    }

    /// <summary>Explicit opt-in. Distances are physical millimeters, not drawing coordinates.</summary>
    public sealed class BendRepairOptions
    {
        public BendRepairUnits DrawingUnits { get; set; }
        public double MaxEndpointMovementMillimeters { get; set; }
    }

    public sealed record BendRepairReport(
        int BendIndex,
        string Status,
        string Reason,
        Vector OriginalStart,
        Vector OriginalEnd,
        Vector Start,
        Vector End
    );

    /// <summary>Conservative, atomic repair. Never edits cut entities or unassociated marks.</summary>
    public static class BendRepair
    {
        public static List<BendRepairReport> Apply(
            List<Entity> entities,
            List<Bend> bends,
            BendRepairOptions options
        )
        {
            var reports = new List<BendRepairReport>();
            if (options == null)
                return reports;
            var scale = options.DrawingUnits == BendRepairUnits.Inches ? 1 / 25.4 : 1.0;
            var tolerance = 0.001 * scale;
            var limit = options.MaxEndpointMovementMillimeters * scale;
            var valid =
                (
                    options.DrawingUnits == BendRepairUnits.Inches
                    || options.DrawingUnits == BendRepairUnits.Millimeters
                )
                && double.IsFinite(limit)
                && limit > tolerance
                && options.MaxEndpointMovementMillimeters <= 3.175;
            var original = bends.Select(b => (b.StartPoint, b.EndPoint)).ToArray();
            var marks = entities.OfType<Line>().Where(IsMark).ToList();
            var cuts = entities.Where(IsCut).ToList();
            // ShapeBuilder may reverse/weld its inputs; isolate it from all source geometry.
            var shapes = ShapeBuilder.GetShapes(cuts.CloneAll(), tolerance);
            var boundaries = shapes.Where(s => s.IsClosed()).SelectMany(s => s.Entities).ToList();
            var polygons = shapes
                .Where(s => s.IsClosed())
                .Select(s => s.ToPolygonWithTolerance(tolerance / 10))
                .ToList();
            bool InMaterial(Vector point) => polygons.Count(p => p.ContainsPoint(point)) % 2 == 1;

            for (var i = 0; i < bends.Count; i++)
            {
                var bend = bends[i];
                var (start, end) = original[i];
                var reason = "";
                var status = "Skipped";
                if (!valid)
                    reason =
                        "Specify drawing units and a finite movement limit above 0.001 and at most 3.175 mm.";
                else if (!Finite(start) || !Finite(end) || start.DistanceTo(end) <= 2 * limit)
                    reason = "Invalid or too-short bend axis.";
                else
                {
                    var axis = (end - start) / start.DistanceTo(end);
                    var first = marks
                        .Where(m => Associated(m, start, end, tolerance, scale))
                        .ToList();
                    var last = marks
                        .Where(m => Associated(m, end, start, tolerance, scale))
                        .ToList();
                    if (first.Count != 1 || last.Count != 1 || ReferenceEquals(first[0], last[0]))
                        reason = "Missing or ambiguous collinear ticks at both original endpoints.";
                    else if (
                        original
                            .Where((_, j) => j != i)
                            .Any(b =>
                                Associated(first[0], b.StartPoint, b.EndPoint, tolerance, scale)
                                || Associated(first[0], b.EndPoint, b.StartPoint, tolerance, scale)
                                || Associated(last[0], b.StartPoint, b.EndPoint, tolerance, scale)
                                || Associated(last[0], b.EndPoint, b.StartPoint, tolerance, scale)
                            )
                    )
                        reason = "Tick is shared with another bend.";
                    else
                    {
                        var probe = new Line(start - axis * limit, end + axis * limit);
                        var hits = new List<Vector>();
                        var uncertain = shapes
                            .Where(s => !s.IsClosed())
                            .Any(s => s.Intersects(probe));
                        foreach (var edge in boundaries)
                        {
                            if (
                                edge is Line line
                                && OnAxis(line.StartPoint, start, axis, tolerance)
                                && OnAxis(line.EndPoint, start, axis, tolerance)
                                && System.Math.Max(
                                    Dot(line.StartPoint - start, axis),
                                    Dot(line.EndPoint - start, axis)
                                ) >= -limit
                                && System.Math.Min(
                                    Dot(line.StartPoint - start, axis),
                                    Dot(line.EndPoint - start, axis)
                                )
                                    <= bend.Length + limit
                            )
                                uncertain = true;
                            if (edge.Intersects(probe, out var points))
                                foreach (var p in points)
                                    if (Finite(p) && !hits.Any(h => h.DistanceTo(p) <= tolerance))
                                        hits.Add(p);
                        }
                        var nearStart = hits.Where(p => p.DistanceTo(start) <= limit).ToList();
                        var nearEnd = hits.Where(p => p.DistanceTo(end) <= limit).ToList();
                        if (
                            uncertain
                            || hits.Count != 2
                            || nearStart.Count != 1
                            || nearEnd.Count != 1
                        )
                            reason =
                                "Missing/ambiguous closed cut boundaries, interior crossing, or movement exceeds limit.";
                        else
                        {
                            // Project the intersection back onto the existing axis; never rotate a bend.
                            var newStart = start + axis * Dot(nearStart[0] - start, axis);
                            var newEnd = start + axis * Dot(nearEnd[0] - start, axis);
                            var sample = axis * (10 * tolerance);
                            if (
                                !InMaterial((newStart + newEnd) / 2)
                                || !InMaterial(newStart + sample)
                                || !InMaterial(newEnd - sample)
                                || InMaterial(newStart - sample)
                                || InMaterial(newEnd + sample)
                            )
                                reason =
                                    "Endpoints do not bound an unambiguous material interval (possible tangent).";
                            else if (
                                Dot(newEnd - newStart, axis)
                                <= first[0].Length + last[0].Length + tolerance
                            )
                                reason = "Repaired ticks would overlap or reverse the bend.";
                            else if (
                                newStart.DistanceTo(start) <= tolerance
                                && newEnd.DistanceTo(end) <= tolerance
                            )
                            {
                                status = "Unchanged";
                                reason = "Already on cut boundaries.";
                            }
                            else
                            {
                                // Prepare both replacements before committing either endpoint or entity list.
                                var a = (Line)first[0].Clone();
                                var b = (Line)last[0].Clone();
                                a.Offset(newStart - start);
                                b.Offset(newEnd - end);
                                var ai = entities.IndexOf(first[0]);
                                var bi = entities.IndexOf(last[0]);
                                if (ai < 0 || bi < 0)
                                    reason = "Tick was already consumed; unchanged.";
                                else
                                {
                                    entities[ai] = a;
                                    entities[bi] = b;
                                    bend.StartPoint = newStart;
                                    bend.EndPoint = newEnd;
                                    status = "Repaired";
                                    reason =
                                        "Both endpoints snapped along axis; only their two associated ticks replaced.";
                                }
                            }
                        }
                    }
                }
                reports.Add(
                    new BendRepairReport(
                        i,
                        status,
                        reason,
                        start,
                        end,
                        bend.StartPoint,
                        bend.EndPoint
                    )
                );
            }
            return reports;
        }

        private static bool Finite(Vector p) => double.IsFinite(p.X) && double.IsFinite(p.Y);

        private static double Dot(Vector a, Vector b) => a.X * b.X + a.Y * b.Y;

        private static bool OnAxis(Vector p, Vector origin, Vector axis, double tolerance) =>
            System.Math.Abs((p.X - origin.X) * axis.Y - (p.Y - origin.Y) * axis.X) <= tolerance;

        private static bool Continuous(Entity e) =>
            string.IsNullOrEmpty(e.LineTypeName)
            || string.Equals(e.LineTypeName, "Continuous", StringComparison.OrdinalIgnoreCase)
            || string.Equals(e.LineTypeName, "ByLayer", StringComparison.OrdinalIgnoreCase);

        private static bool IsMark(Entity e) =>
            Continuous(e)
            && (
                string.Equals(e.Layer?.Name, "ETCH", StringComparison.OrdinalIgnoreCase)
                || string.Equals(e.Layer?.Name, "SCRIBE", StringComparison.OrdinalIgnoreCase)
            );

        private static bool IsCut(Entity e) =>
            Continuous(e)
            && (
                e.Layer?.Name == "0"
                || string.Equals(e.Layer?.Name, "CUT", StringComparison.OrdinalIgnoreCase)
            )
            && (e is Line || e is Arc || e is Circle);

        private static bool Associated(
            Line mark,
            Vector endpoint,
            Vector other,
            double tolerance,
            double scale
        )
        {
            var length = endpoint.DistanceTo(other);
            if (
                !Finite(endpoint)
                || !Finite(other)
                || length <= tolerance
                || !Finite(mark.StartPoint)
                || !Finite(mark.EndPoint)
                || mark.Length <= tolerance
                || mark.Length > 25.4 * scale + tolerance
                || mark.Length >= length / 3
            )
                return false;
            var axis = (other - endpoint) / length;
            if (
                !OnAxis(mark.StartPoint, endpoint, axis, tolerance)
                || !OnAxis(mark.EndPoint, endpoint, axis, tolerance)
            )
                return false;
            var a = Dot(mark.StartPoint - endpoint, axis);
            var b = Dot(mark.EndPoint - endpoint, axis);
            return System.Math.Abs(System.Math.Min(a, b)) <= tolerance
                && System.Math.Max(a, b) > tolerance;
        }
    }
}
