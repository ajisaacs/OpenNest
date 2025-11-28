using System.Collections.Generic;
using OpenNest.CNC;
using OpenNest.Geometry;

namespace OpenNest
{
    public static class ConvertGeometry
    {
        public static Program ToProgram(IList<Entity> geometry)
        {
            var shapes = Helper.GetShapes(geometry);

            if (shapes.Count == 0)
                return null;

            var perimeter = shapes[0];
            var area = perimeter.BoundingBox.Area();
            var index = 0;

            for (int i = 1; i < shapes.Count; ++i)
            {
                var program = shapes[i];
                var area2 = program.BoundingBox.Area();
                if (area2 > area)
                {
                    perimeter = program;
                    area = area2;
                    index = i;
                }
            }

            shapes.RemoveAt(index);

            var pgm = new Program();

            foreach (var shape in shapes)
            {
                var subpgm = ToProgram(shape);
                pgm.Merge(subpgm);
            }

            pgm.Merge(ToProgram(perimeter));
            pgm.Mode = Mode.Incremental;

            return pgm;
        }

        public static Program ToProgram(Shape shape)
        {
            var pgm = new Program();
            var lastpt = new Vector();

            for (int i = 0; i < shape.Entities.Count; i++)
                lastpt = AddEntity(pgm, lastpt, shape.Entities[i]);

            return pgm;
        }

        private static Vector AddEntity(Program pgm, Vector lastpt, Entity geo)
        {
            switch (geo.Type)
            {
                case EntityType.Arc:
                    lastpt = AddArc(pgm, lastpt, (Arc)geo);
                    break;

                case EntityType.Circle:
                    lastpt = AddCircle(pgm, lastpt, (Circle)geo);
                    break;

                case EntityType.Line:
                    lastpt = AddLine(pgm, lastpt, (Line)geo);
                    break;
            }

            return lastpt;
        }

        private static Vector AddArc(Program pgm, Vector lastpt, Arc arc)
        {
            var startpt = arc.StartPoint();
            var endpt = arc.EndPoint();

            if (startpt != lastpt)
                pgm.MoveTo(startpt);

            lastpt = endpt;

            pgm.ArcTo(endpt, arc.Center, arc.IsReversed ? RotationType.CW : RotationType.CCW);
            return lastpt;
        }

        private static Vector AddCircle(Program pgm, Vector lastpt, Circle circle)
        {
            var startpt = new Vector(circle.Center.X + circle.Radius, circle.Center.Y);

            if (startpt != lastpt)
                pgm.MoveTo(startpt);

            pgm.ArcTo(startpt, circle.Center, RotationType.CCW);

            lastpt = startpt;
            return lastpt;
        }

        private static Vector AddLine(Program pgm, Vector lastpt, Line line)
        {
            if (line.StartPoint != lastpt)
                pgm.MoveTo(line.StartPoint);

            pgm.LineTo(line.EndPoint);

            lastpt = line.EndPoint;
            return lastpt;
        }
    }
}
