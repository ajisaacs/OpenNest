using System.Collections.Generic;
using System.Linq;
using OpenNest.Geometry;

namespace OpenNest.IO
{
    internal static class Extensions
    {
        public static Vector ToOpenNest(this netDxf.Vector2 v)
        {
            return new Vector(v.X, v.Y);
        }

        public static Vector ToOpenNest(this netDxf.Vector3 v)
        {
            return new Vector(v.X, v.Y);
        }

        public static Vector ToOpenNest(this netDxf.Entities.Polyline2DVertex v)
        {
            return new Vector(v.Position.X, v.Position.Y);
        }

        public static Arc ToOpenNest(this netDxf.Entities.Arc arc)
        {
            return new Arc(
                arc.Center.X, arc.Center.Y, arc.Radius,
                Angle.ToRadians(arc.StartAngle),
                Angle.ToRadians(arc.EndAngle))
                {
                    Layer = arc.Layer.ToOpenNest()
                };
        }

        public static Circle ToOpenNest(this netDxf.Entities.Circle circle)
        {
            return new Circle(
                circle.Center.X, circle.Center.Y,
                circle.Radius)
                {
                    Layer = circle.Layer.ToOpenNest()
                };
        }

        public static Line ToOpenNest(this netDxf.Entities.Line line)
        {
            return new Line(
                line.StartPoint.X, line.StartPoint.Y,
                line.EndPoint.X, line.EndPoint.Y)
                {
                    Layer = line.Layer.ToOpenNest()
                };
        }

        public static List<Line> ToOpenNest(this netDxf.Entities.Spline spline, int precision = 200)
        {
            var lines = new List<Line>();
            var pts = spline.PolygonalVertexes(precision);

            if (pts.Count == 0)
                return lines;

            var lastPoint = pts[0].ToOpenNest();

            for (int i = 1; i < pts.Count; i++)
            {
                var nextPoint = pts[i].ToOpenNest();

                lines.Add(new Line(
                    lastPoint,
                    nextPoint) { Layer = spline.Layer.ToOpenNest() });

                lastPoint = nextPoint;
            }

            if (spline.IsClosed)
                lines.Add(new Line(lastPoint, pts[0].ToOpenNest()) { Layer = spline.Layer.ToOpenNest() });

            return lines;
        }

        public static List<Line> ToOpenNest(this netDxf.Entities.Polyline3D polyline)
        {
            var lines = new List<Line>();

            if (polyline.Vertexes.Count == 0)
                return lines;

            var lastPoint = polyline.Vertexes[0].ToOpenNest();

            for (int i = 1; i < polyline.Vertexes.Count; i++)
            {
                var nextPoint = polyline.Vertexes[i].ToOpenNest();

                lines.Add(new Line(
                    lastPoint,
                    nextPoint) { Layer = polyline.Layer.ToOpenNest() });

                lastPoint = nextPoint;
            }

            if (polyline.IsClosed)
                lines.Add(new Line(lastPoint, polyline.Vertexes[0].ToOpenNest()) { Layer = polyline.Layer.ToOpenNest() });

            return lines;
        }

        public static List<Line> ToOpenNest(this netDxf.Entities.Polyline2D polyline)
        {
            var lines = new List<Line>();

            if (polyline.Vertexes.Count == 0)
                return lines;

            var lastPoint = polyline.Vertexes[0].ToOpenNest();

            for (int i = 1; i < polyline.Vertexes.Count; i++)
            {
                var nextPoint = polyline.Vertexes[i].ToOpenNest();

                lines.Add(new Line(
                    lastPoint,
                    nextPoint) { Layer = polyline.Layer.ToOpenNest() });

                lastPoint = nextPoint;
            }

            if (polyline.IsClosed)
                lines.Add(new Line(lastPoint, polyline.Vertexes[0].ToOpenNest()) { Layer = polyline.Layer.ToOpenNest() });

            return lines;
        }

        public static List<Line> ToOpenNest(this netDxf.Entities.Ellipse ellipse, int precision = 200)
        {
            var lines = ellipse.ToPolyline2D(precision).ToOpenNest();

            if (lines.Count < 2)
                return lines;

            var first = lines.First();
            var last = lines.Last();

            // workaround for ellipse.ToPolyline not connecting the last and first point.
            lines.Add(new Line(last.EndPoint, first.StartPoint) { Layer = ellipse.Layer.ToOpenNest() });

            return lines;
        }

        public static Layer ToOpenNest(this netDxf.Tables.Layer layer)
        {
            return new Layer(layer.Name)
            {
                Color = layer.Color.ToColor(),
                IsVisible = layer.IsVisible
            };
        }

        public static netDxf.Vector2 ToNetDxf(this Vector v)
        {
            return new netDxf.Vector2(v.X, v.Y);
        }
    }
}
