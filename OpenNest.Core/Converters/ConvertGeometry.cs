using OpenNest.CNC;
using OpenNest.Geometry;
using OpenNest.Math;
using System.Collections.Generic;

namespace OpenNest.Converters
{
    public static class ConvertGeometry
    {
        public static Program ToProgram(IList<Entity> geometry)
        {
            var shapes = ShapeBuilder.GetShapes(geometry);

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

            if (startpt.DistanceTo(lastpt) > Tolerance.ChainTolerance)
                pgm.MoveTo(startpt);

            lastpt = endpt;

            var layer = ClassifyLayer(arc);
            var sweep = System.Math.Abs(arc.SweepAngle());
            if (sweep < Tolerance.Epsilon || sweep.IsEqualTo(Angle.TwoPI))
            {
                pgm.Codes.Add(new LinearMove(endpt) { Layer = layer });
            }
            else
            {
                pgm.Codes.Add(new ArcMove(endpt, arc.Center, arc.IsReversed ? RotationType.CW : RotationType.CCW) { Layer = layer });
            }

            return lastpt;
        }

        private static Vector AddCircle(Program pgm, Vector lastpt, Circle circle)
        {
            var startpt = new Vector(circle.Center.X + circle.Radius, circle.Center.Y);

            if (startpt.DistanceTo(lastpt) > Tolerance.ChainTolerance)
                pgm.MoveTo(startpt);

            pgm.Codes.Add(new ArcMove(startpt, circle.Center, circle.Rotation) { Layer = ClassifyLayer(circle) });

            lastpt = startpt;
            return lastpt;
        }

        private static Vector AddLine(Program pgm, Vector lastpt, Line line)
        {
            if (line.StartPoint.DistanceTo(lastpt) > Tolerance.ChainTolerance)
                pgm.MoveTo(line.StartPoint);

            pgm.Codes.Add(new LinearMove(line.EndPoint) { Layer = ClassifyLayer(line) });

            lastpt = line.EndPoint;
            return lastpt;
        }

        // Engrave/etch geometry maps to Scribe so the post processor can treat it as a
        // separate tool pass; everything else keeps the move's default Cut layer.
        private static LayerType ClassifyLayer(Entity geo)
        {
            var name = geo.Layer?.Name;
            if (string.Equals(name, "ENGRAVE", System.StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, "ETCH", System.StringComparison.OrdinalIgnoreCase))
                return LayerType.Scribe;

            return LayerType.Cut;
        }
    }
}
