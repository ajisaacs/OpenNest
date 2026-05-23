using System;
using System.Collections.Generic;
using OpenNest.CNC;
using OpenNest.Geometry;

namespace OpenNest.Posts.GravographIS
{
    /// <summary>
    /// Lifts polylines out of an OpenNest <see cref="Nest"/> for the Gravograph
    /// backend. Walks each <see cref="Part"/>'s <see cref="Program"/>, breaks
    /// polylines at rapid moves, and tessellates arcs to a chord-deviation
    /// tolerance (the wire format takes line segments only).
    /// </summary>
    public sealed class NestPolylineExtractor
    {
        public double ArcChordToleranceInches { get; set; } = 0.001;

        /// <summary>
        /// Extracts polylines from every non-cutoff part in every plate of the nest,
        /// returning them in plate coordinates (inches).
        /// </summary>
        public List<List<Vector>> Extract(Nest nest)
        {
            if (nest == null) throw new ArgumentNullException(nameof(nest));

            var result = new List<List<Vector>>();

            foreach (var plate in nest.Plates)
            {
                foreach (var part in plate.Parts)
                {
                    if (part.BaseDrawing != null && part.BaseDrawing.IsCutOff)
                        continue;

                    ExtractPart(part, result);
                }
            }

            return result;
        }

        /// <summary>
        /// Extracts polylines for a single part. Public so callers driving the
        /// writer directly (e.g. from a console one-off) can use it.
        /// </summary>
        public List<List<Vector>> ExtractPart(Part part)
        {
            var list = new List<List<Vector>>();
            ExtractPart(part, list);
            return list;
        }

        private void ExtractPart(Part part, List<List<Vector>> sink)
        {
            var program = part.Program;
            if (program == null) return;

            // The walk below treats Motion.EndPoint as absolute. Convert a working
            // copy to absolute mode so G91 programs (the form OpenNest's UI writes)
            // produce correct geometry. Cloning keeps part.Program untouched.
            if (program.Mode == Mode.Incremental)
            {
                program = (Program)program.Clone();
                program.Mode = Mode.Absolute;
            }

            var offset = part.Location;
            var pos = new Vector(0, 0);
            List<Vector> current = null;

            foreach (var code in program.Codes)
            {
                if (code is Motion m && m.Suppressed)
                    continue;

                switch (code)
                {
                    case RapidMove rapid:
                    {
                        FlushCurrent(sink, ref current);
                        pos = rapid.EndPoint;
                        break;
                    }

                    case LinearMove linear:
                    {
                        if (current == null)
                        {
                            current = new List<Vector> { pos + offset };
                        }
                        var end = linear.EndPoint;
                        current.Add(end + offset);
                        pos = end;
                        break;
                    }

                    case ArcMove arc:
                    {
                        if (current == null)
                        {
                            current = new List<Vector> { pos + offset };
                        }
                        TessellateArc(pos, arc, offset, ArcChordToleranceInches, current);
                        pos = arc.EndPoint;
                        break;
                    }
                }
            }

            FlushCurrent(sink, ref current);
        }

        private static void FlushCurrent(List<List<Vector>> sink, ref List<Vector> current)
        {
            if (current != null && current.Count >= 2)
                sink.Add(current);
            current = null;
        }

        // Sample points along an arc to within chordTol of the true curve. start is
        // the arc's start point (current pen position), arc.CenterPoint is absolute
        // (G-code I/J in this codebase are stored as the absolute center), arc.EndPoint
        // is absolute end. The starting point is assumed to already be in the polyline;
        // intermediate samples and the endpoint are appended.
        private static void TessellateArc(Vector start, ArcMove arc, Vector offset,
            double chordTol, List<Vector> sink)
        {
            var c = arc.CenterPoint;
            var r = c.DistanceTo(start);
            if (r < 1e-9)
            {
                sink.Add(arc.EndPoint + offset);
                return;
            }

            var a0 = System.Math.Atan2(start.Y - c.Y, start.X - c.X);
            var a1 = System.Math.Atan2(arc.EndPoint.Y - c.Y, arc.EndPoint.X - c.X);

            double sweep;
            if (arc.Rotation == RotationType.CW)
            {
                sweep = a0 - a1;
                if (sweep <= 0) sweep += 2 * System.Math.PI;
            }
            else
            {
                sweep = a1 - a0;
                if (sweep <= 0) sweep += 2 * System.Math.PI;
            }

            // Treat a near-zero sweep with coincident start/end as a full circle.
            if (sweep < 1e-9 &&
                System.Math.Abs(start.X - arc.EndPoint.X) < 1e-9 &&
                System.Math.Abs(start.Y - arc.EndPoint.Y) < 1e-9)
            {
                sweep = 2 * System.Math.PI;
            }

            // Max angle step from chord-deviation tolerance: dev = r * (1 - cos(t/2)).
            var maxAngleStep = 2.0 * System.Math.Acos(System.Math.Max(0.0, 1.0 - chordTol / r));
            if (double.IsNaN(maxAngleStep) || maxAngleStep <= 0)
                maxAngleStep = System.Math.PI / 32;

            var steps = (int)System.Math.Ceiling(sweep / maxAngleStep);
            if (steps < 1) steps = 1;

            var direction = arc.Rotation == RotationType.CW ? -1.0 : 1.0;
            for (int i = 1; i < steps; i++)
            {
                var t = sweep * (i / (double)steps);
                var ang = a0 + direction * t;
                var pt = new Vector(c.X + r * System.Math.Cos(ang), c.Y + r * System.Math.Sin(ang));
                sink.Add(pt + offset);
            }

            sink.Add(arc.EndPoint + offset);
        }
    }
}
