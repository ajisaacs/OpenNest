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
            return new Geometry.Arc(
                arc.Center.X, arc.Center.Y, arc.Radius,
                arc.StartAngle,
                arc.EndAngle)
                {
                    Layer = arc.Layer.ToOpenNest()
                };
        }

        public static Geometry.Circle ToOpenNest(this ACadSharp.Entities.Circle circle)
        {
            return new Geometry.Circle(
                circle.Center.X, circle.Center.Y,
                circle.Radius)
                {
                    Layer = circle.Layer.ToOpenNest()
                };
        }

        public static Geometry.Line ToOpenNest(this ACadSharp.Entities.Line line)
        {
            return new Geometry.Line(
                line.StartPoint.X, line.StartPoint.Y,
                line.EndPoint.X, line.EndPoint.Y)
                {
                    Layer = line.Layer.ToOpenNest()
                };
        }

        public static List<Geometry.Line> ToOpenNest(this Spline spline)
        {
            var lines = new List<Geometry.Line>();
            var pts = spline.ControlPoints;

            if (pts.Count == 0)
                return lines;

            var lastPoint = pts[0].ToOpenNest();

            for (var i = 1; i < pts.Count; i++)
            {
                var nextPoint = pts[i].ToOpenNest();

                lines.Add(new Geometry.Line(
                    lastPoint,
                    nextPoint) { Layer = spline.Layer.ToOpenNest() });

                lastPoint = nextPoint;
            }

            if (spline.IsClosed)
                lines.Add(new Geometry.Line(lastPoint, pts[0].ToOpenNest()) { Layer = spline.Layer.ToOpenNest() });

            return lines;
        }

        public static List<Geometry.Line> ToOpenNest(this Polyline polyline)
        {
            var lines = new List<Geometry.Line>();

            if (polyline.Vertices.Count == 0)
                return lines;

            var lastPoint = polyline.Vertices[0].Location.ToOpenNest();

            for (var i = 1; i < polyline.Vertices.Count; i++)
            {
                var nextPoint = polyline.Vertices[i].Location.ToOpenNest();

                lines.Add(new Geometry.Line(
                    lastPoint,
                    nextPoint) { Layer = polyline.Layer.ToOpenNest() });

                lastPoint = nextPoint;
            }

            var isClosed = (polyline.Flags & PolylineFlags.ClosedPolylineOrClosedPolygonMeshInM) != 0;

            if (isClosed)
                lines.Add(new Geometry.Line(lastPoint, polyline.Vertices[0].Location.ToOpenNest()) { Layer = polyline.Layer.ToOpenNest() });

            return lines;
        }

        public static List<Geometry.Line> ToOpenNest(this LwPolyline polyline)
        {
            var lines = new List<Geometry.Line>();

            if (polyline.Vertices.Count == 0)
                return lines;

            var lastPoint = polyline.Vertices[0].ToOpenNest();

            for (var i = 1; i < polyline.Vertices.Count; i++)
            {
                var nextPoint = polyline.Vertices[i].ToOpenNest();

                lines.Add(new Geometry.Line(
                    lastPoint,
                    nextPoint) { Layer = polyline.Layer.ToOpenNest() });

                lastPoint = nextPoint;
            }

            var isClosed = (polyline.Flags & LwPolylineFlags.Closed) != 0;

            if (isClosed)
                lines.Add(new Geometry.Line(lastPoint, polyline.Vertices[0].ToOpenNest()) { Layer = polyline.Layer.ToOpenNest() });

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

            for (var i = 0; i < points.Count - 1; i++)
            {
                lines.Add(new Geometry.Line(points[i], points[i + 1]) { Layer = layer });
            }

            // Close the ellipse if it's a full ellipse
            if (lines.Count >= 2)
            {
                var first = lines.First();
                var last = lines.Last();
                lines.Add(new Geometry.Line(last.EndPoint, first.StartPoint) { Layer = layer });
            }

            return lines;
        }

        public static Geometry.Layer ToOpenNest(this ACadSharp.Tables.Layer layer)
        {
            return new Geometry.Layer(layer.Name)
            {
                Color = Color.FromArgb(layer.Color.R, layer.Color.G, layer.Color.B),
                IsVisible = layer.IsOn
            };
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
