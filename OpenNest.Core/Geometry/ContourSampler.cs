using System;
using System.Collections.Generic;
using OpenNest.CNC;
using OpenNest.Math;

namespace OpenNest.Geometry
{
    /// <summary>
    /// One world-space point on a cut contour together with the cut direction there.
    /// Produced by <see cref="ContourSampler"/> for rendering (cut-direction arrows)
    /// and for measurement (contour alignment); neither caller may mutate it.
    /// </summary>
    public readonly struct ContourSample
    {
        /// <summary>Point on the contour in world coordinates.</summary>
        public Vector Position { get; }

        /// <summary>Unit vector pointing in the direction of travel along the contour.</summary>
        public Vector Direction { get; }

        /// <summary>
        /// World-frame tangent angle in radians (atan2 of <see cref="Direction"/>).
        /// Screen-space conversion is the renderer's job.
        /// </summary>
        public double Tangent { get; }

        /// <summary>
        /// Arclength of this sample from the start of the contour walk it came from:
        /// cumulative distance along non-rapid, non-suppressed moves for a program
        /// walk, and from the ring's first vertex for a ring walk.
        /// </summary>
        public double At { get; }

        public ContourSample(Vector position, Vector direction, double tangent, double at)
        {
            Position = position;
            Direction = direction;
            Tangent = tangent;
            At = at;
        }
    }

    /// <summary>
    /// Pure contour sampling math shared by the cut-direction arrow renderer and by
    /// contour alignment. All positions and tangents are world-space; no screen
    /// conversion, no arrowheads, and no view state appear here.
    /// <para>
    /// Two scheduling policies live here deliberately:
    /// <see cref="LineMoves"/> and <see cref="ArcMoves"/> implement the arrow
    /// renderer's established display policy (skip moves shorter than half the
    /// spacing, place <c>max(1, trunc(len/spacing))</c> arrows strictly inside each
    /// move, resetting per move), while <see cref="RingMoves"/> is a contour-wide
    /// arclength scheduler for measurement: distance is carried across segment
    /// boundaries, no segment is omitted, and the closing vertex of a ring is never
    /// duplicated. Alignment must use the ring policy; display zoom must never
    /// change an alignment result because alignment spacing comes from the model,
    /// not the view.
    /// </para>
    /// </summary>
    public static class ContourSampler
    {
        /// <summary>
        /// Samples one bounded line move using the arrow display policy: no samples
        /// when the move is shorter than half the spacing or degenerate; otherwise
        /// <c>max(1, (int)(length / spacing))</c> samples strictly between the
        /// endpoints at uniform spacing. Appends to <paramref name="output"/>.
        /// </summary>
        public static void LineMoves(
            Vector start,
            Vector end,
            double spacing,
            List<ContourSample> output
        )
        {
            var dx = end.X - start.X;
            var dy = end.Y - start.Y;
            var length = System.Math.Sqrt(dx * dx + dy * dy);
            if (length < spacing * 0.5)
                return;

            var dirX = dx / length;
            var dirY = dy / length;
            var tangent = System.Math.Atan2(dirY, dirX);

            var count = System.Math.Max(1, (int)(length / spacing));
            var step = length / (count + 1);

            for (var i = 1; i <= count; i++)
            {
                var t = step * i;
                var pt = new Vector(start.X + dirX * t, start.Y + dirY * t);
                output.Add(new ContourSample(pt, new Vector(dirX, dirY), tangent, t));
            }
        }

        /// <summary>
        /// Samples one bounded arc move using the arrow display policy. The sweep is
        /// taken in the requested rotation direction and always in (0, 2*PI], so a
        /// full circle (equal endpoints) yields a full turn. No samples when the arc
        /// is shorter than half the spacing or the radius is degenerate. Tangents
        /// follow the direction of travel: +90 degrees from the radius for CCW,
        /// -90 degrees for CW. Appends to <paramref name="output"/>.
        /// </summary>
        public static void ArcMoves(
            Vector start,
            Vector end,
            Vector center,
            RotationType rotation,
            double spacing,
            List<ContourSample> output
        )
        {
            var radius = center.DistanceTo(start);
            if (radius < Tolerance.Epsilon)
                return;

            var startAngle = System.Math.Atan2(start.Y - center.Y, start.X - center.X);
            var endAngle = System.Math.Atan2(end.Y - center.Y, end.X - center.X);

            double sweep;
            if (rotation == RotationType.CCW)
            {
                sweep = endAngle - startAngle;
                if (sweep <= 0)
                    sweep += 2 * System.Math.PI;
            }
            else
            {
                sweep = startAngle - endAngle;
                if (sweep <= 0)
                    sweep += 2 * System.Math.PI;
            }

            var arcLength = radius * System.Math.Abs(sweep);
            if (arcLength < spacing * 0.5)
                return;

            var count = System.Math.Max(1, (int)(arcLength / spacing));
            var stepAngle = sweep / (count + 1);

            for (var i = 1; i <= count; i++)
            {
                double angle;
                if (rotation == RotationType.CCW)
                    angle = startAngle + stepAngle * i;
                else
                    angle = startAngle - stepAngle * i;

                var pt = new Vector(
                    center.X + radius * System.Math.Cos(angle),
                    center.Y + radius * System.Math.Sin(angle)
                );

                double tangent;
                if (rotation == RotationType.CCW)
                    tangent = angle + System.Math.PI / 2;
                else
                    tangent = angle - System.Math.PI / 2;

                var dir = new Vector(System.Math.Cos(tangent), System.Math.Sin(tangent));
                var at = radius * System.Math.Abs(stepAngle * i);
                output.Add(new ContourSample(pt, dir, tangent, at));
            }
        }

        /// <summary>
        /// Walks a CNC program in world space with the same traversal policy the cut
        /// direction renderer has always used: absolute endpoints are relative to
        /// <paramref name="basePos"/>, incremental endpoints and arc centers are
        /// relative to the current position, suppressed moves and rapids advance the
        /// pen but produce no samples, and each sub-program call executes at
        /// <c>basePos + Offset</c> against a shared program (callers own the shared
        /// program; this method only reads it). Suppressed sub-program content is
        /// filtered inside the sub-program itself.
        /// </summary>
        /// <returns>The pen position after the program, so callers keep the
        /// reference semantics of the renderer's by-ref position.</returns>
        public static Vector ProgramMoves(
            Program pgm,
            Vector basePos,
            Vector pos,
            double spacing,
            List<ContourSample> output
        )
        {
            var at = 0.0;
            WalkProgram(pgm, basePos, ref pos, spacing, output, ref at);
            return pos;
        }

        private static void WalkProgram(
            Program pgm,
            Vector basePos,
            ref Vector pos,
            double spacing,
            List<ContourSample> output,
            ref double at
        )
        {
            for (var i = 0; i < pgm.Length; ++i)
            {
                var code = pgm[i];

                if (code.Type == CodeType.SubProgramCall)
                {
                    var subpgm = (SubProgramCall)code;
                    if (subpgm.Program != null)
                    {
                        var holeBase = basePos + subpgm.Offset;
                        pos = holeBase;
                        WalkProgram(
                            subpgm.Program,
                            holeBase,
                            ref pos,
                            spacing,
                            output,
                            ref at
                        );
                    }
                    continue;
                }

                if (code is not Motion motion)
                    continue;

                var endpt =
                    pgm.Mode == Mode.Incremental
                        ? motion.EndPoint + pos
                        : motion.EndPoint + basePos;

                if (code.Type == CodeType.LinearMove)
                {
                    var line = (LinearMove)code;
                    if (!line.Suppressed)
                    {
                        var before = output.Count;
                        LineMoves(pos, endpt, spacing, output);
                        Relocate(output, before, at);
                        at += Distance(pos, endpt);
                    }
                }
                else if (code.Type == CodeType.ArcMove)
                {
                    var arc = (ArcMove)code;
                    if (!arc.Suppressed)
                    {
                        var center =
                            pgm.Mode == Mode.Incremental
                                ? arc.CenterPoint + pos
                                : arc.CenterPoint + basePos;
                        var before = output.Count;
                        ArcMoves(pos, endpt, center, arc.Rotation, spacing, output);
                        Relocate(output, before, at);
                        at += ArcDistance(pos, endpt, center, arc.Rotation);
                    }
                }

                pos = endpt;
            }
        }

        /// <summary>
        /// Resamples a closed ring at near-uniform arclength for measurement. The
        /// distance counter is carried across segment boundaries, no segment is
        /// omitted, and the closing vertex is not duplicated: samples sit at
        /// arclength <c>i * step</c> for <c>i in [0, count)</c> where
        /// <c>step = perimeter / count</c> divides the perimeter exactly, so the
        /// sample set is invariant to where the ring's start vertex sits as long as
        /// the caller quantizes consistently. A duplicated explicit closing vertex
        /// is accepted and ignored.
        /// </summary>
        /// <exception cref="ArgumentException">
        /// The ring has fewer than three distinct vertices, nonfinite coordinates,
        /// or a zero perimeter.
        /// </exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="spacing"/> is not finite or not positive.</exception>
        public static void RingMoves(IList<Vector> ring, double spacing, List<ContourSample> output)
        {
            if (ring == null)
                throw new ArgumentNullException(nameof(ring));
            if (!(spacing > 0) || double.IsInfinity(spacing) || double.IsNaN(spacing))
                throw new ArgumentOutOfRangeException(nameof(spacing));

            var n = ring.Count;
            if (n > 1 && ring[0] == ring[n - 1])
                n--; // ignore an explicit closing vertex; the ring closes implicitly
            if (n < 3)
                throw new ArgumentException("Ring needs at least 3 distinct vertices.", nameof(ring));

            var perimeter = 0.0;
            for (var i = 0; i < n; i++)
            {
                var a = ring[i];
                var b = ring[(i + 1) % n];
                if (double.IsNaN(a.X) || double.IsNaN(a.Y) || double.IsNaN(b.X) || double.IsNaN(b.Y))
                    throw new ArgumentException("Ring contains nonfinite coordinates.", nameof(ring));
                perimeter += Distance(a, b);
            }
            if (!(perimeter > Tolerance.Epsilon))
                throw new ArgumentException("Ring has zero perimeter.", nameof(ring));

            var count = System.Math.Max(1, (int)System.Math.Round(perimeter / spacing));
            var step = perimeter / count;

            var seg = 0;
            var segStart = 0.0; // cumulative arclength at the start of segment seg
            for (var k = 0; k < count; k++)
            {
                var s = step * k;

                // Carry the walk across segment boundaries; short segments advance
                // the arclength counter without ever being skipped.
                var a = ring[seg];
                var b = ring[(seg + 1) % n];
                var segLen = Distance(a, b);
                while (s > segStart + segLen && seg + 1 < n)
                {
                    segStart += segLen;
                    seg++;
                    a = ring[seg];
                    b = ring[(seg + 1) % n];
                    segLen = Distance(a, b);
                }

                var local = segLen > 0 ? (s - segStart) / segLen : 0.0;
                var dir = SegmentDirection(a, b);
                var pt = new Vector(a.X + (b.X - a.X) * local, a.Y + (b.Y - a.Y) * local);
                output.Add(new ContourSample(pt, dir, System.Math.Atan2(dir.Y, dir.X), s));
            }
        }

        private static Vector SegmentDirection(Vector a, Vector b)
        {
            var dx = b.X - a.X;
            var dy = b.Y - a.Y;
            var len = System.Math.Sqrt(dx * dx + dy * dy);
            return len > 0 ? new Vector(dx / len, dy / len) : new Vector(1, 0);
        }

        private static double Distance(Vector a, Vector b)
        {
            var dx = b.X - a.X;
            var dy = b.Y - a.Y;
            return System.Math.Sqrt(dx * dx + dy * dy);
        }

        private static double ArcDistance(
            Vector start,
            Vector end,
            Vector center,
            RotationType rotation
        )
        {
            var radius = center.DistanceTo(start);
            if (radius < Tolerance.Epsilon)
                return 0.0;

            // Same sweep convention as ArcMoves: always in (0, 2*PI], so a full
            // circle counts its whole circumference toward the walk's arclength.
            var startAngle = System.Math.Atan2(start.Y - center.Y, start.X - center.X);
            var endAngle = System.Math.Atan2(end.Y - center.Y, end.X - center.X);
            var sweep =
                rotation == RotationType.CCW ? endAngle - startAngle : startAngle - endAngle;
            if (sweep <= 0)
                sweep += 2 * System.Math.PI;
            return radius * sweep;
        }

        private static void Relocate(List<ContourSample> output, int from, double baseAt)
        {
            if (baseAt == 0.0)
                return;
            for (var i = from; i < output.Count; i++)
            {
                var s = output[i];
                output[i] = new ContourSample(
                    s.Position,
                    s.Direction,
                    s.Tangent,
                    baseAt + s.At
                );
            }
        }
    }
}
