using System;
using System.Collections.Generic;
using System.Linq;
using OpenNest.CNC;
using OpenNest.Geometry;

namespace OpenNest.Converters
{
    /// <summary>
    /// Restores the Scribe layer on program moves that were saved as cuts. Programs built from
    /// stored entities before ConvertGeometry recognized the SCRIBE layer name turned etch marks
    /// into Cut moves, which nesting then treated as open cut geometry. The drawing's source
    /// entities still carry the mark layer, so matching moves are reclassified from them.
    /// </summary>
    public static class ScribeLayerRepair
    {
        private const double MatchTolerance = 0.001;

        /// <summary>
        /// Reclassifies Cut moves in <paramref name="program"/> that lie on a mark entity in
        /// <paramref name="sourceEntities"/>. Program coordinates are source coordinates shifted
        /// by -<paramref name="sourceOffset"/>. Returns the number of moves reclassified.
        /// </summary>
        public static int Apply(Program program, IEnumerable<Entity> sourceEntities, Vector sourceOffset)
        {
            if (program == null || sourceEntities == null)
                return 0;

            var marks = sourceEntities.Where(e => IsMarkLayer(e.Layer)).ToList();
            if (marks.Count == 0 || program.Codes.Any(c => c is SubProgramCall))
                return 0;

            // ToGeometry emits exactly one entity per rapid/linear/arc move of a flat program.
            var motions = program.Codes.Where(c => c is RapidMove or LinearMove or ArcMove).ToList();
            var geometry = ConvertProgram.ToGeometry(program);
            if (geometry.Count != motions.Count)
                return 0;

            var repaired = 0;
            for (var i = 0; i < motions.Count; i++)
            {
                var source = Translate(geometry[i], sourceOffset);
                switch (motions[i])
                {
                    case LinearMove line when line.Layer == LayerType.Cut && IsOnMark(source, marks):
                        line.Layer = LayerType.Scribe;
                        repaired++;
                        break;
                    case ArcMove arc when arc.Layer == LayerType.Cut && IsOnMark(source, marks):
                        arc.Layer = LayerType.Scribe;
                        repaired++;
                        break;
                }
            }
            return repaired;
        }

        public static bool IsMarkLayer(Layer layer) =>
            layer != null
            && (
                layer == SpecialLayers.Scribe
                || string.Equals(layer.Name, SpecialLayers.Scribe.Name, StringComparison.OrdinalIgnoreCase)
                || string.Equals(layer.Name, "ETCH", StringComparison.OrdinalIgnoreCase)
                || string.Equals(layer.Name, "ENGRAVE", StringComparison.OrdinalIgnoreCase)
            );

        private static List<Vector> Translate(Entity entity, Vector offset)
        {
            var points = entity switch
            {
                Line l => new List<Vector>
                {
                    l.StartPoint,
                    l.EndPoint,
                    new Vector((l.StartPoint.X + l.EndPoint.X) / 2, (l.StartPoint.Y + l.EndPoint.Y) / 2),
                },
                Arc a => new List<Vector> { a.StartPoint(), a.EndPoint(), a.MidPoint() },
                Circle c => new List<Vector>
                {
                    new Vector(c.Center.X + c.Radius, c.Center.Y),
                    new Vector(c.Center.X - c.Radius, c.Center.Y),
                    new Vector(c.Center.X, c.Center.Y + c.Radius),
                },
                _ => new List<Vector>(),
            };
            return points.ConvertAll(p => new Vector(p.X + offset.X, p.Y + offset.Y));
        }

        // A move is a mark when every sample point lies on one single mark entity.
        private static bool IsOnMark(List<Vector> points, List<Entity> marks) =>
            points.Count > 0
            && marks.Any(m => points.All(p => m.ClosestPointTo(p).DistanceTo(p) <= MatchTolerance));
    }
}
