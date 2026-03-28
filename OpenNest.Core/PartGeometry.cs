using OpenNest.Converters;
using OpenNest.Geometry;
using System.Collections.Generic;
using System.Linq;

namespace OpenNest
{
    public static class PartGeometry
    {
        public static List<Line> GetPartLines(Part part, double chordTolerance = 0.001)
        {
            var entities = ConvertProgram.ToGeometry(part.Program);
            var shapes = ShapeBuilder.GetShapes(entities.Where(e => e.Layer != SpecialLayers.Rapid));
            var lines = new List<Line>();

            foreach (var shape in shapes)
            {
                var polygon = shape.ToPolygonWithTolerance(chordTolerance);
                polygon.Offset(part.Location);
                lines.AddRange(polygon.ToLines());
            }

            return lines;
        }

        public static List<Line> GetPartLines(Part part, PushDirection facingDirection, double chordTolerance = 0.001)
        {
            var entities = ConvertProgram.ToGeometry(part.Program);
            var shapes = ShapeBuilder.GetShapes(entities.Where(e => e.Layer != SpecialLayers.Rapid));
            var lines = new List<Line>();

            foreach (var shape in shapes)
            {
                var polygon = shape.ToPolygonWithTolerance(chordTolerance);
                polygon.Offset(part.Location);
                lines.AddRange(GetDirectionalLines(polygon, facingDirection));
            }

            return lines;
        }

        public static List<Line> GetOffsetPartLines(Part part, double spacing, double chordTolerance = 0.001)
        {
            var entities = ConvertProgram.ToGeometry(part.Program);
            var profile = new ShapeProfile(
                entities.Where(e => e.Layer != SpecialLayers.Rapid).ToList());
            var lines = new List<Line>();
            var totalSpacing = spacing;

            AddOffsetLines(lines, profile.Perimeter.OffsetOutward(totalSpacing),
                chordTolerance, part.Location);

            foreach (var cutout in profile.Cutouts)
                AddOffsetLines(lines, cutout.OffsetInward(totalSpacing),
                    chordTolerance, part.Location);

            return lines;
        }

        public static List<Line> GetOffsetPartLines(Part part, double spacing, PushDirection facingDirection, double chordTolerance = 0.001)
        {
            var entities = ConvertProgram.ToGeometry(part.Program);
            var profile = new ShapeProfile(
                entities.Where(e => e.Layer != SpecialLayers.Rapid).ToList());
            var lines = new List<Line>();
            var totalSpacing = spacing;

            AddOffsetDirectionalLines(lines, profile.Perimeter.OffsetOutward(totalSpacing),
                chordTolerance, part.Location, facingDirection);

            foreach (var cutout in profile.Cutouts)
                AddOffsetDirectionalLines(lines, cutout.OffsetInward(totalSpacing),
                    chordTolerance, part.Location, facingDirection);

            return lines;
        }

        public static List<Line> GetPartLines(Part part, Vector facingDirection, double chordTolerance = 0.001)
        {
            var entities = ConvertProgram.ToGeometry(part.Program);
            var shapes = ShapeBuilder.GetShapes(entities.Where(e => e.Layer != SpecialLayers.Rapid));
            var lines = new List<Line>();

            foreach (var shape in shapes)
            {
                var polygon = shape.ToPolygonWithTolerance(chordTolerance);
                polygon.Offset(part.Location);
                lines.AddRange(GetDirectionalLines(polygon, facingDirection));
            }

            return lines;
        }

        public static List<Line> GetOffsetPartLines(Part part, double spacing, Vector facingDirection, double chordTolerance = 0.001)
        {
            var entities = ConvertProgram.ToGeometry(part.Program);
            var profile = new ShapeProfile(
                entities.Where(e => e.Layer != SpecialLayers.Rapid).ToList());
            var lines = new List<Line>();
            var totalSpacing = spacing;

            AddOffsetDirectionalLines(lines, profile.Perimeter.OffsetOutward(totalSpacing),
                chordTolerance, part.Location, facingDirection);

            foreach (var cutout in profile.Cutouts)
                AddOffsetDirectionalLines(lines, cutout.OffsetInward(totalSpacing),
                    chordTolerance, part.Location, facingDirection);

            return lines;
        }

        /// <summary>
        /// Returns only polygon edges whose outward normal faces the specified direction vector.
        /// </summary>
        private static List<Line> GetDirectionalLines(Polygon polygon, Vector direction)
        {
            if (polygon.Vertices.Count < 3)
                return polygon.ToLines();

            var sign = polygon.RotationDirection() == RotationType.CCW ? 1.0 : -1.0;
            var lines = new List<Line>();
            var last = polygon.Vertices[0];

            for (var i = 1; i < polygon.Vertices.Count; i++)
            {
                var current = polygon.Vertices[i];
                var edx = current.X - last.X;
                var edy = current.Y - last.Y;

                var keep = sign * (edy * direction.X - edx * direction.Y) > 0;

                if (keep)
                    lines.Add(new Line(last, current));

                last = current;
            }

            return lines;
        }

        /// <summary>
        /// Returns only polygon edges whose outward normal faces the specified direction.
        /// </summary>
        private static List<Line> GetDirectionalLines(Polygon polygon, PushDirection facingDirection)
        {
            if (polygon.Vertices.Count < 3)
                return polygon.ToLines();

            var sign = polygon.RotationDirection() == RotationType.CCW ? 1.0 : -1.0;
            var lines = new List<Line>();
            var last = polygon.Vertices[0];

            for (int i = 1; i < polygon.Vertices.Count; i++)
            {
                var current = polygon.Vertices[i];
                var dx = current.X - last.X;
                var dy = current.Y - last.Y;

                bool keep;

                switch (facingDirection)
                {
                    case PushDirection.Left: keep = -sign * dy > 0; break;
                    case PushDirection.Right: keep = sign * dy > 0; break;
                    case PushDirection.Up: keep = -sign * dx > 0; break;
                    case PushDirection.Down: keep = sign * dx > 0; break;
                    default: keep = true; break;
                }

                if (keep)
                    lines.Add(new Line(last, current));

                last = current;
            }

            return lines;
        }

        private static void AddOffsetLines(List<Line> lines, Shape offsetEntity,
            double chordTolerance, Vector location)
        {
            if (offsetEntity == null)
                return;

            var polygon = offsetEntity.ToPolygonWithTolerance(chordTolerance);
            polygon.RemoveSelfIntersections();
            polygon.Offset(location);
            lines.AddRange(polygon.ToLines());
        }

        private static void AddOffsetDirectionalLines(List<Line> lines, Shape offsetEntity,
            double chordTolerance, Vector location, PushDirection facingDirection)
        {
            if (offsetEntity == null)
                return;

            var polygon = offsetEntity.ToPolygonWithTolerance(chordTolerance);
            polygon.RemoveSelfIntersections();
            polygon.Offset(location);
            lines.AddRange(GetDirectionalLines(polygon, facingDirection));
        }

        private static void AddOffsetDirectionalLines(List<Line> lines, Shape offsetEntity,
            double chordTolerance, Vector location, Vector facingDirection)
        {
            if (offsetEntity == null)
                return;

            var polygon = offsetEntity.ToPolygonWithTolerance(chordTolerance);
            polygon.RemoveSelfIntersections();
            polygon.Offset(location);
            lines.AddRange(GetDirectionalLines(polygon, facingDirection));
        }
    }
}
