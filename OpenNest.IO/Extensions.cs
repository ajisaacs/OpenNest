using ACadSharp.Entities;
using CSMath;
using OpenNest.Geometry;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace OpenNest.IO
{
    internal static class Extensions
    {
        public static Vector ToOpenNest(this XY v)
        {
            return new Vector(v.X, v.Y);
        }

        public static Vector ToOpenNest(this XYZ v)
        {
            return new Vector(v.X, v.Y);
        }

        public static Geometry.Arc ToOpenNest(this ACadSharp.Entities.Arc arc)
        {
            var result = new Geometry.Arc(
                arc.Center.X, arc.Center.Y, arc.Radius,
                arc.StartAngle,
                arc.EndAngle)
            {
                Layer = arc.Layer.ToOpenNest()
            };
            result.ApplyDxfProperties(arc);
            return result;
        }

        public static Geometry.Circle ToOpenNest(this ACadSharp.Entities.Circle circle)
        {
            var result = new Geometry.Circle(
                circle.Center.X, circle.Center.Y,
                circle.Radius)
            {
                Layer = circle.Layer.ToOpenNest()
            };
            result.ApplyDxfProperties(circle);
            return result;
        }

        public static Geometry.Line ToOpenNest(this ACadSharp.Entities.Line line)
        {
            var result = new Geometry.Line(
                line.StartPoint.X, line.StartPoint.Y,
                line.EndPoint.X, line.EndPoint.Y)
            {
                Layer = line.Layer.ToOpenNest()
            };
            result.ApplyDxfProperties(line);
            return result;
        }

        public static List<Geometry.Entity> ToOpenNest(this Spline spline, int precision)
        {
            var layer = spline.Layer.ToOpenNest();
            var color = spline.ResolveColor();
            var lineTypeName = spline.ResolveLineTypeName();

            // Evaluate actual points on the spline curve (not control points)
            List<XYZ> curvePoints;
            try
            {
                curvePoints = spline.PolygonalVertexes(precision > 0 ? precision : 200);
            }
            catch
            {
                curvePoints = null;
            }

            if (curvePoints == null || curvePoints.Count < 2)
            {
                // Fallback: use control points if evaluation fails
                curvePoints = new List<XYZ>(spline.ControlPoints);
                if (curvePoints.Count < 2)
                    return new List<Geometry.Entity>();
            }

            var points = new List<Vector>(curvePoints.Count);
            foreach (var pt in curvePoints)
                points.Add(pt.ToOpenNest());

            var entities = SplineConverter.Convert(points, spline.IsClosed, tolerance: 0.001);

            foreach (var entity in entities)
            {
                entity.Layer = layer;
                entity.Color = color;
                entity.LineTypeName = lineTypeName;
            }

            return entities;
        }

        public static List<Geometry.Line> ToOpenNest(this Polyline polyline)
        {
            var lines = new List<Geometry.Line>();

            if (polyline.Vertices.Count == 0)
                return lines;

            var layer = polyline.Layer.ToOpenNest();
            var color = polyline.ResolveColor();
            var lineTypeName = polyline.ResolveLineTypeName();
            var lastPoint = polyline.Vertices[0].Location.ToOpenNest();

            for (var i = 1; i < polyline.Vertices.Count; i++)
            {
                var nextPoint = polyline.Vertices[i].Location.ToOpenNest();

                lines.Add(new Geometry.Line(lastPoint, nextPoint)
                {
                    Layer = layer,
                    Color = color,
                    LineTypeName = lineTypeName
                });

                lastPoint = nextPoint;
            }

            var isClosed = (polyline.Flags & PolylineFlags.ClosedPolylineOrClosedPolygonMeshInM) != 0;

            if (isClosed)
                lines.Add(new Geometry.Line(lastPoint, polyline.Vertices[0].Location.ToOpenNest())
                {
                    Layer = layer,
                    Color = color,
                    LineTypeName = lineTypeName
                });

            return lines;
        }

        public static List<Geometry.Line> ToOpenNest(this LwPolyline polyline)
        {
            var lines = new List<Geometry.Line>();

            if (polyline.Vertices.Count == 0)
                return lines;

            var layer = polyline.Layer.ToOpenNest();
            var color = polyline.ResolveColor();
            var lineTypeName = polyline.ResolveLineTypeName();
            var lastPoint = polyline.Vertices[0].ToOpenNest();

            for (var i = 1; i < polyline.Vertices.Count; i++)
            {
                var nextPoint = polyline.Vertices[i].ToOpenNest();

                lines.Add(new Geometry.Line(lastPoint, nextPoint)
                {
                    Layer = layer,
                    Color = color,
                    LineTypeName = lineTypeName
                });

                lastPoint = nextPoint;
            }

            var isClosed = (polyline.Flags & LwPolylineFlags.Closed) != 0;

            if (isClosed)
                lines.Add(new Geometry.Line(lastPoint, polyline.Vertices[0].ToOpenNest())
                {
                    Layer = layer,
                    Color = color,
                    LineTypeName = lineTypeName
                });

            return lines;
        }

        public static List<Geometry.Entity> ToOpenNest(this ACadSharp.Entities.Ellipse ellipse, double tolerance = 0.001)
        {
            var center = new Vector(ellipse.Center.X, ellipse.Center.Y);
            var majorAxis = new Vector(ellipse.MajorAxisEndPoint.X, ellipse.MajorAxisEndPoint.Y);
            var semiMajor = System.Math.Sqrt(majorAxis.X * majorAxis.X + majorAxis.Y * majorAxis.Y);
            var semiMinor = semiMajor * ellipse.RadiusRatio;
            var rotation = System.Math.Atan2(majorAxis.Y, majorAxis.X);

            var startParam = ellipse.StartParameter;
            var endParam = ellipse.EndParameter;

            var layer = ellipse.Layer.ToOpenNest();
            var color = ellipse.ResolveColor();
            var lineTypeName = ellipse.ResolveLineTypeName();

            var entities = EllipseConverter.Convert(center, semiMajor, semiMinor, rotation,
                startParam, endParam, tolerance);

            foreach (var entity in entities)
            {
                entity.Layer = layer;
                entity.Color = color;
                entity.LineTypeName = lineTypeName;
            }

            return entities;
        }

        public static Geometry.Layer ToOpenNest(this ACadSharp.Tables.Layer layer)
        {
            return new Geometry.Layer(layer.Name)
            {
                Color = Color.FromArgb(layer.Color.R, layer.Color.G, layer.Color.B),
                IsVisible = layer.IsOn,
                LineTypeName = layer.LineType?.Name
            };
        }

        public static Color ResolveColor(this ACadSharp.Entities.Entity entity)
        {
            var color = entity.Color;

            if (color.IsByLayer)
                color = entity.Layer.Color;

            return Color.FromArgb(color.R, color.G, color.B);
        }

        public static string ResolveLineTypeName(this ACadSharp.Entities.Entity entity)
        {
            var lt = entity.LineType;

            if (lt == null || string.Equals(lt.Name, "ByLayer", System.StringComparison.OrdinalIgnoreCase))
                return entity.Layer.LineType?.Name ?? "Continuous";

            return lt.Name;
        }

        public static void ApplyDxfProperties(this Geometry.Entity target, ACadSharp.Entities.Entity source)
        {
            target.Color = source.ResolveColor();
            target.LineTypeName = source.ResolveLineTypeName();
        }

        public static Vector ToOpenNest(this LwPolyline.Vertex v)
        {
            return new Vector(v.Location.X, v.Location.Y);
        }

        public static XY ToAcad(this Vector v)
        {
            return new XY(v.X, v.Y);
        }

        public static XYZ ToAcadXYZ(this Vector v)
        {
            return new XYZ(v.X, v.Y, 0);
        }
    }
}
