using System.Collections.Generic;
using OpenNest.Math;

namespace OpenNest.Geometry
{
    /// <summary>
    /// Thins spacing outlines whose contour holds a dense run of tiny entities, such as a curve a
    /// CAD export wrote as hundreds of 0.005-inch lines. Every slide and fill query costs in
    /// proportion to the entities on both sides, so one such run can make Fill of a single part
    /// take minutes.
    ///
    /// The thinned outline only ever adds clearance. It is the contour offset by the spacing plus
    /// <see cref="Pad"/>, with each dense run replaced by chords through points on that offset that
    /// stay within <see cref="ChordTolerance"/> of it (arcs sampled within
    /// <see cref="SampleTolerance"/>). Every chord therefore lies outside the outline offset by the
    /// spacing alone, so a layout spaced on the thinned outline holds the true spacing, at most
    /// <c>2 * Pad</c> wider at a dense run. Contours without a dense run keep their exact offset.
    /// </summary>
    public static class DenseOutline
    {
        /// <summary>Entities shorter than this count toward a dense run.</summary>
        public const double ShortLength = 0.02;

        /// <summary>Fewest consecutive short entities that make a dense run.</summary>
        public const int MinRun = 16;

        /// <summary>Largest distance a chord may stray from the sampled offset.</summary>
        public const double ChordTolerance = 0.001;

        /// <summary>Largest distance an arc may bulge past the chords it is sampled into.</summary>
        public const double SampleTolerance = 0.0005;

        /// <summary>Extra offset a thinned outline takes so its chords never cut inside the
        /// outline at the true spacing.</summary>
        public const double Pad = ChordTolerance + SampleTolerance;

        /// <summary>Whether the closed contour holds a dense run (a run may wrap past its first
        /// entity).</summary>
        public static bool HasDenseRun(IReadOnlyList<Entity> contour)
        {
            if (contour.Count < MinRun)
                return false;
            var start = FirstLong(contour);
            if (start < 0)
                return true;
            var run = 0;
            for (var k = 1; k <= contour.Count; k++)
            {
                if (IsShort(contour[(start + k) % contour.Count]))
                {
                    if (++run >= MinRun)
                        return true;
                }
                else
                {
                    run = 0;
                }
            }
            return false;
        }

        /// <summary>
        /// The closed contour with each dense run replaced by chords within
        /// <see cref="ChordTolerance"/> of it; other entities are kept as they are. Callers pass a
        /// contour already offset by <see cref="Pad"/> beyond the clearance it must keep.
        /// </summary>
        public static List<Entity> Thin(IReadOnlyList<Entity> contour)
        {
            var result = new List<Entity>(contour.Count);
            var start = FirstLong(contour);
            if (start < 0)
            {
                // Every entity is short (a circle exported as lines): thin it as two halves so
                // each run has distinct ends.
                var half = contour.Count / 2;
                AddChords(contour, 0, half, result);
                AddChords(contour, half, contour.Count - half, result);
                return result;
            }

            // Walk once around the loop from a long entity, so no run is split at index 0.
            var k = 0;
            while (k < contour.Count)
            {
                var index = (start + k) % contour.Count;
                var entity = contour[index];
                if (!IsShort(entity))
                {
                    result.Add(entity);
                    k++;
                    continue;
                }
                var length = 0;
                while (k + length < contour.Count && IsShort(contour[(start + k + length) % contour.Count]))
                    length++;
                if (length >= MinRun)
                {
                    AddChords(contour, index, length, result);
                }
                else
                {
                    for (var i = 0; i < length; i++)
                        result.Add(contour[(index + i) % contour.Count]);
                }
                k += length;
            }
            return result;
        }

        private static bool IsShort(Entity entity) =>
            (entity is Line || entity is Arc) && entity.Length < ShortLength;

        private static int FirstLong(IReadOnlyList<Entity> contour)
        {
            for (var i = 0; i < contour.Count; i++)
                if (!IsShort(contour[i]))
                    return i;
            return -1;
        }

        /// <summary>Appends the run's chords: a Douglas-Peucker pass over its sampled points
        /// that keeps both ends exact.</summary>
        private static void AddChords(IReadOnlyList<Entity> contour, int first, int count, List<Entity> result)
        {
            var points = new List<Vector>();
            for (var i = 0; i < count; i++)
                Sample(contour[(first + i) % contour.Count], points);
            points.Add(End(contour[(first + count - 1) % contour.Count]));

            var keep = new bool[points.Count];
            keep[0] = true;
            keep[points.Count - 1] = true;
            var spans = new Stack<(int From, int To)>();
            spans.Push((0, points.Count - 1));
            while (spans.Count > 0)
            {
                var (from, to) = spans.Pop();
                var farthest = -1;
                var distance = ChordTolerance;
                for (var i = from + 1; i < to; i++)
                {
                    var d = DistanceToSegment(points[i], points[from], points[to]);
                    if (d > distance)
                    {
                        distance = d;
                        farthest = i;
                    }
                }
                if (farthest < 0)
                    continue;
                keep[farthest] = true;
                spans.Push((farthest, to));
                spans.Push((from, farthest));
            }

            var previous = points[0];
            for (var i = 1; i < points.Count; i++)
            {
                if (!keep[i])
                    continue;
                result.Add(new Line(previous, points[i]));
                previous = points[i];
            }
        }

        /// <summary>Adds the entity's points from its start up to, not including, its end.</summary>
        private static void Sample(Entity entity, List<Vector> points)
        {
            if (entity is Line line)
            {
                points.Add(line.StartPoint);
                return;
            }
            var arc = (Arc)entity;
            var sweep = arc.SweepAngle();
            var segments = 1;
            if (arc.Radius > SampleTolerance)
            {
                // A chord over angle a sags r(1 - cos(a/2)) below its arc.
                var step = 2 * PortableMath.Acos(1 - SampleTolerance / arc.Radius);
                segments = System.Math.Max(1, (int)System.Math.Ceiling(sweep / step));
            }
            var sampled = arc.ToPoints(segments);
            for (var i = 0; i < sampled.Count - 1; i++)
                points.Add(sampled[i]);
        }

        private static Vector End(Entity entity) =>
            entity is Line line ? line.EndPoint : ((Arc)entity).EndPoint();

        private static double DistanceToSegment(Vector p, Vector a, Vector b)
        {
            var dx = b.X - a.X;
            var dy = b.Y - a.Y;
            var lengthSquared = dx * dx + dy * dy;
            if (lengthSquared <= 0)
                return p.DistanceTo(a);
            var t = ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / lengthSquared;
            t = System.Math.Max(0, System.Math.Min(1, t));
            return p.DistanceTo(new Vector(a.X + t * dx, a.Y + t * dy));
        }
    }
}
