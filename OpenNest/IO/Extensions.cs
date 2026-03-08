using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using ACadSharp.Entities;
using CSMath;
using OpenNest.Geometry;

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

        public static List<Geometry.Line> ToOpenNest(this Spline spline)
        {
            var lines = new List<Geometry.Line>();
            var pts = spline.ControlPoints;

            if (pts.Count == 0)
                return lines;

            var layer = spline.Layer.ToOpenNest();
            var color = spline.ResolveColor();
            var lineTypeName = spline.ResolveLineTypeName();
            var lastPoint = pts[0].ToOpenNest();

            for (var i = 1; i < pts.Count; i++)
            {
                var nextPoint = pts[i].ToOpenNest();

                lines.Add(new Geometry.Line(lastPoint, nextPoint)
                {
                    Layer = layer, Color = color, LineTypeName = lineTypeName
                });

                lastPoint = nextPoint;
            }

            if (spline.IsClosed)
                lines.Add(new Geometry.Line(lastPoint, pts[0].ToOpenNest())
                {
                    Layer = layer, Color = color, LineTypeName = lineTypeName
                });

            return lines;
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
                    Layer = layer, Color = color, LineTypeName = lineTypeName
                });

                lastPoint = nextPoint;
            }

            var isClosed = (polyline.Flags & PolylineFlags.ClosedPolylineOrClosedPolygonMeshInM) != 0;

            if (isClosed)
                lines.Add(new Geometry.Line(lastPoint, polyline.Vertices[0].Location.ToOpenNest())
                {
                    Layer = layer, Color = color, LineTypeName = lineTypeName
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
                    Layer = layer, Color = color, LineTypeName = lineTypeName
                });

                lastPoint = nextPoint;
            }

            var isClosed = (polyline.Flags & LwPolylineFlags.Closed) != 0;

            if (isClosed)
                lines.Add(new Geometry.Line(lastPoint, polyline.Vertices[0].ToOpenNest())
                {
                    Layer = layer, Color = color, LineTypeName = lineTypeName
                });

            return lines;
        }

        public static List<Geometry.Line> ToOpenNest(this ACadSharp.Entities.Ellipse ellipse, int precision = 200)
        {
            var lines = new List<Geometry.Line>();

            var center = new Vector(ellipse.Center.X, ellipse.Center.Y);
            var majorAxis = new Vector(ellipse.MajorAxisEndPoint.X, ellipse.MajorAxisEndPoint.Y);
            var majorLength = System.Math.Sqrt(majorAxis.X * majorAxis.X + majorAxis.Y * majorAxis.Y);
            var minorLength = majorLength * ellipse.RadiusRatio;
            var rotation = System.Math.Atan2(majorAxis.Y, majorAxis.X);

            var startParam = ellipse.StartParameter;
            var endParam = ellipse.EndParameter;

            if (endParam <= startParam)
                endParam += System.Math.PI * 2.0;

            var step = (endParam - startParam) / precision;

            var points = new List<Vector>();

            for (var i = 0; i <= precision; i++)
            {
                var t = startParam + step * i;
                var x = majorLength * System.Math.Cos(t);
                var y = minorLength * System.Math.Sin(t);

                // Rotate by the major axis angle and translate to center
                var cos = System.Math.Cos(rotation);
                var sin = System.Math.Sin(rotation);
                var px = center.X + x * cos - y * sin;
                var py = center.Y + x * sin + y * cos;

                points.Add(new Vector(px, py));
            }

            var layer = ellipse.Layer.ToOpenNest();
            var color = ellipse.ResolveColor();
            var lineTypeName = ellipse.ResolveLineTypeName();

            for (var i = 0; i < points.Count - 1; i++)
            {
                lines.Add(new Geometry.Line(points[i], points[i + 1])
                {
                    Layer = layer, Color = color, LineTypeName = lineTypeName
                });
            }

            // Close the ellipse if it's a full ellipse
            if (lines.Count >= 2)
            {
                var first = lines.First();
                var last = lines.Last();
                lines.Add(new Geometry.Line(last.EndPoint, first.StartPoint)
                {
                    Layer = layer, Color = color, LineTypeName = lineTypeName
                });
            }

            return lines;
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
