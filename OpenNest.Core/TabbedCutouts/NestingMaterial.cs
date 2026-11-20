using System.Collections.Generic;
using System.Linq;
using OpenNest.Geometry;
using OpenNest.Math;

namespace OpenNest
{
    public static class NestingMaterial
    {
        /// <summary>
        /// The material entities nesting and layout validation place parts against: those
        /// <see cref="SpecialLayers.IsMaterial"/> keeps, except that a tabbed cutout's slug stays
        /// attached while the sheet is cut, so a closed hole with <see cref="SpecialLayers.Tab"/>
        /// spans is dropped and its slug counts as part. Area, overlap diagnostics and cutting
        /// keep the hole. Entities without tab spans pass through unchanged.
        /// </summary>
        public static List<Entity> ForNesting(this IEnumerable<Entity> entities)
        {
            var material = entities.Where(e => SpecialLayers.IsMaterial(e.Layer)).ToList();
            if (!material.Exists(e => e.Layer == SpecialLayers.Tab))
                return material;

            var kept = new List<Entity>();
            foreach (var shape in ShapeBuilder.GetShapes(material))
                if (!shape.Entities.Exists(e => e.Layer == SpecialLayers.Tab))
                    kept.AddRange(shape.Entities);
            return kept;
        }

        /// <summary>
        /// <paramref name="material"/> with a tabbed contour closed across its tab. A perimeter
        /// tab stops the cut short of its entry, so a planned outside is an open chain, yet the
        /// part's material still reaches across the gap. Offsetting the open chain gives a
        /// contour that does not close, and the slide kernel blocks every contact against a
        /// boundary it cannot close, freezing the part and its neighbors where they touch.
        /// The trimmed last cut is restored along its own line or arc when the entry lies on
        /// it, which is exact; when the tab swallowed whole entities the gap is bridged by its
        /// chord, as flattening the contour to a polygon closes it. Only a single gap is
        /// closed: material with no open chain is returned unchanged, and so is material with
        /// several gaps (a contour cut in pieces) or a lone line.
        /// </summary>
        internal static List<Entity> ClosedAcrossTab(this List<Entity> material)
        {
            const double tolerance = Tolerance.ChainTolerance;

            // Runs of entities chained end to start in list order. A planned contour is one
            // run; regrouping by shape can split it where its first entity is an arc.
            List<(int First, int Last, Vector Start, Vector End)> open = null;
            for (var i = 0; i < material.Count; i++)
            {
                if (!EntityEndpoints.TryGet(material[i], out var start, out var end))
                    continue;

                var first = i;
                while (end.DistanceTo(start) > tolerance
                    && i + 1 < material.Count
                    && EntityEndpoints.TryGet(material[i + 1], out var nextStart, out var nextEnd)
                    && nextStart.DistanceTo(end) <= tolerance)
                {
                    end = nextEnd;
                    i++;
                }

                if (end.DistanceTo(start) > tolerance)
                    (open ??= new List<(int, int, Vector, Vector)>()).Add((first, i, start, end));
            }

            if (open == null)
                return material;

            // The tab gap runs from the one end no open run starts at to the one start no
            // open run ends at; both must belong to the same chain of runs.
            var tail = -1;
            var head = -1;
            for (var r = 0; r < open.Count; r++)
            {
                var run = open[r];
                if (!open.Exists(other => other.Start.DistanceTo(run.End) <= tolerance))
                {
                    if (tail >= 0)
                        return material;
                    tail = r;
                }

                if (!open.Exists(other => other.End.DistanceTo(run.Start) <= tolerance))
                {
                    if (head >= 0)
                        return material;
                    head = r;
                }
            }

            if (tail < 0 || head < 0)
                return material;

            var at = head;
            for (var steps = 0; at != tail; steps++)
            {
                var from = open[at].End;
                at = open.FindIndex(other => other.Start.DistanceTo(from) <= tolerance);
                if (at < 0 || steps >= open.Count)
                    return material;
            }

            var index = open[tail].Last;
            var last = material[index];
            var gapStart = open[tail].End;
            var gapEnd = open[head].Start;
            var alone = head == tail && open[head].First == index;

            switch (last)
            {
                case Line when alone:
                    return material;

                // A round outside cut as one arc that stops short of its own start.
                case Arc arc when alone && OnCircle(arc, gapEnd, tolerance):
                    material[index] = new Circle(arc.Center, arc.Radius)
                    {
                        Rotation = arc.IsReversed ? RotationType.CW : RotationType.CCW,
                        Layer = arc.Layer,
                    };
                    return material;

                case Arc arc when OnCircle(arc, gapEnd, tolerance):
                    var extended = new Arc(arc.Center, arc.Radius, arc.StartAngle,
                        arc.Center.AngleTo(gapEnd), arc.IsReversed)
                    {
                        Layer = arc.Layer,
                    };
                    var added = extended.SweepAngle() - arc.SweepAngle();
                    if (added > 0 && added < System.Math.PI)
                    {
                        material[index] = extended;
                        return material;
                    }

                    break;

                case Line line when Continues(line, gapEnd, tolerance):
                    material[index] = new Line(line.StartPoint, gapEnd) { Layer = line.Layer };
                    return material;
            }

            material.Insert(index + 1, new Line(gapStart, gapEnd) { Layer = last.Layer });
            return material;
        }

        private static bool OnCircle(Arc arc, Vector point, double tolerance) =>
            System.Math.Abs(arc.Center.DistanceTo(point) - arc.Radius) <= tolerance;

        /// <summary>True when <paramref name="point"/> lies on the extension of
        /// <paramref name="line"/> past its end.</summary>
        private static bool Continues(Line line, Vector point, double tolerance)
        {
            var dx = line.EndPoint.X - line.StartPoint.X;
            var dy = line.EndPoint.Y - line.StartPoint.Y;
            var length = System.Math.Sqrt(dx * dx + dy * dy);
            if (length <= tolerance)
                return false;

            var px = point.X - line.EndPoint.X;
            var py = point.Y - line.EndPoint.Y;
            return System.Math.Abs(dx * py - dy * px) / length <= tolerance
                && dx * px + dy * py > 0;
        }
    }
}
