using System;
using System.Collections.Generic;
using System.Linq;
using OpenNest.CNC;
using OpenNest.Geometry;

namespace OpenNest.Posts.GravographIS
{
    /// <summary>
    /// A polyline together with the <see cref="LayerType"/> of the moves that
    /// produced it. The Gravograph post groups by layer to emit separate engrave
    /// and cut passes (with a tool-change pause between them).
    /// </summary>
    public sealed class LayeredPolyline
    {
        public LayeredPolyline(List<Vector> points, LayerType layer)
        {
            Points = points;
            Layer = layer;
        }

        public List<Vector> Points { get; }

        public LayerType Layer { get; }
    }

    /// <summary>
    /// Lifts polylines out of an OpenNest <see cref="Nest"/> for the Gravograph
    /// backend. Walks each <see cref="Part"/>'s <see cref="Program"/>, breaks
    /// polylines at rapid moves and at <see cref="LayerType"/> changes, and
    /// tessellates arcs to a chord-deviation tolerance (the wire format takes
    /// line segments only).
    /// </summary>
    public sealed class NestPolylineExtractor
    {
        public double ArcChordToleranceInches { get; set; } = 0.001;

        /// <summary>
        /// Extracts polylines from every non-cutoff part in every plate of the nest,
        /// returning them in plate coordinates (inches). Layer information is dropped;
        /// use <see cref="ExtractLayered(Nest)"/> to keep it.
        /// </summary>
        public List<List<Vector>> Extract(Nest nest)
        {
            return ExtractLayered(nest).Select(p => p.Points).ToList();
        }

        /// <summary>
        /// Extracts polylines for a single part without layer information.
        /// </summary>
        public List<List<Vector>> ExtractPart(Part part)
        {
            return ExtractPartLayered(part).Select(p => p.Points).ToList();
        }

        /// <summary>
        /// Extracts layer-tagged polylines from every non-cutoff part in every plate,
        /// in plate coordinates (inches). Each polyline is layer-uniform.
        /// </summary>
        public List<LayeredPolyline> ExtractLayered(Nest nest)
        {
            if (nest == null) throw new ArgumentNullException(nameof(nest));

            var result = new List<LayeredPolyline>();

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
        /// Extracts layer-tagged polylines for a single part. Public so callers
        /// driving the writer directly (e.g. from a console one-off) can use it.
        /// </summary>
        public List<LayeredPolyline> ExtractPartLayered(Part part)
        {
            var list = new List<LayeredPolyline>();
            ExtractPart(part, list);
            return list;
        }

        private void ExtractPart(Part part, List<LayeredPolyline> sink)
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
            var currentLayer = LayerType.Cut;

            foreach (var code in program.Codes)
            {
                if (code is Motion m && m.Suppressed)
                    continue;

                switch (code)
                {
                    case RapidMove rapid:
                    {
                        FlushCurrent(sink, ref current, currentLayer);
                        pos = rapid.EndPoint;
                        break;
                    }

                    case LinearMove linear:
                    {
                        StartOrSplit(sink, ref current, ref currentLayer, linear.Layer, pos + offset);
                        var end = linear.EndPoint;
                        current.Add(end + offset);
                        pos = end;
                        break;
                    }

                    case ArcMove arc:
                    {
                        StartOrSplit(sink, ref current, ref currentLayer, arc.Layer, pos + offset);
                        TessellateArc(pos, arc, offset, ArcChordToleranceInches, current);
                        pos = arc.EndPoint;
                        break;
                    }
                }
            }

            FlushCurrent(sink, ref current, currentLayer);
        }

        // Ensures `current` is an open polyline whose layer matches `moveLayer`,
        // seeded at `seed` (the current pen position). When the layer changes
        // mid-chain the previous polyline is flushed and a new one begins at the
        // shared seam vertex so engrave and cut passes stay geometrically continuous.
        private static void StartOrSplit(List<LayeredPolyline> sink, ref List<Vector> current,
            ref LayerType currentLayer, LayerType moveLayer, Vector seed)
        {
            if (current == null)
            {
                current = new List<Vector> { seed };
                currentLayer = moveLayer;
            }
            else if (moveLayer != currentLayer)
            {
                FlushCurrent(sink, ref current, currentLayer);
                current = new List<Vector> { seed };
                currentLayer = moveLayer;
            }
        }

        private static void FlushCurrent(List<LayeredPolyline> sink, ref List<Vector> current, LayerType layer)
        {
            if (current != null && current.Count >= 2)
                sink.Add(new LayeredPolyline(current, layer));
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
