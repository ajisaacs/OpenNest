using System;
using System.Collections.Generic;
using OpenNest.Geometry;
using OpenNest.Math;

namespace OpenNest.Labels
{
    /// <summary>
    /// One segment of a <see cref="Stroke"/>: a straight line or an arc to <see cref="End"/>.
    /// An arc turns about <see cref="Center"/>, clockwise when <see cref="Clockwise"/> is set,
    /// and is always less than a full turn.
    /// </summary>
    public readonly record struct StrokeSegment(Vector End, bool IsArc = false, Vector Center = default,
        bool Clockwise = false);

    /// <summary>A pen-down path of a single-stroke label: a start point and its segments.</summary>
    public sealed class Stroke
    {
        // Arcs are sampled at most this many radians apart by Points.
        private const double SampleStep = System.Math.PI / 36;

        public Stroke(Vector start) => Start = start;

        public Vector Start { get; }

        public List<StrokeSegment> Segments { get; } = new();

        /// <summary>Where the stroke ends: its last segment's end, or its start without segments.</summary>
        public Vector End => Segments.Count == 0 ? Start : Segments[Segments.Count - 1].End;

        /// <summary>
        /// The stroke with every point moved by <paramref name="map"/>, which must be a rigid
        /// motion (a turn and a shift, never a mirror) or arcs keep the wrong direction.
        /// </summary>
        public Stroke Map(Func<Vector, Vector> map)
        {
            var mapped = new Stroke(map(Start));
            foreach (var segment in Segments)
                mapped.Segments.Add(segment with
                {
                    End = map(segment.End),
                    Center = segment.IsArc ? map(segment.Center) : segment.Center,
                });
            return mapped;
        }

        /// <summary>
        /// The stroke's points, with arcs sampled finely enough to bound it: for measuring and
        /// drawing, not for etching.
        /// </summary>
        public IEnumerable<Vector> Points()
        {
            yield return Start;
            var position = Start;
            foreach (var segment in Segments)
            {
                if (segment.IsArc)
                {
                    var radius = segment.Center.DistanceTo(position);
                    var from = PortableMath.Atan2(position.Y - segment.Center.Y, position.X - segment.Center.X);
                    var sweep = Sweep(position, segment);
                    var steps = System.Math.Max(1, (int)System.Math.Ceiling(System.Math.Abs(sweep) / SampleStep));
                    for (var step = 1; step < steps; step++)
                    {
                        var angle = from + sweep * step / steps;
                        yield return new Vector(segment.Center.X + radius * PortableMath.Cos(angle),
                            segment.Center.Y + radius * PortableMath.Sin(angle));
                    }
                }
                yield return segment.End;
                position = segment.End;
            }
        }

        // The signed sweep of an arc from start to its end: negative clockwise, never zero.
        private static double Sweep(Vector start, StrokeSegment arc)
        {
            var from = PortableMath.Atan2(start.Y - arc.Center.Y, start.X - arc.Center.X);
            var to = PortableMath.Atan2(arc.End.Y - arc.Center.Y, arc.End.X - arc.Center.X);
            var sweep = to - from;
            if (arc.Clockwise)
            {
                while (sweep >= -Tolerance.Epsilon)
                    sweep -= Angle.TwoPI;
            }
            else
            {
                while (sweep <= Tolerance.Epsilon)
                    sweep += Angle.TwoPI;
            }
            return sweep;
        }
    }
}
