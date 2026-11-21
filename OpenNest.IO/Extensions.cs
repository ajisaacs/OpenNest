using System;
using System.Collections.Generic;
using System.Drawing;
using ACadSharp.Entities;
using CSMath;
using OpenNest.Geometry;
using OpenNest.Math;

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
                arc.Center.X,
                arc.Center.Y,
                arc.Radius,
                arc.StartAngle,
                arc.EndAngle
            )
            {
                Layer = arc.Layer.ToOpenNest(),
            };
            result.ApplyDxfProperties(arc);
            return result;
        }

        public static Geometry.Circle ToOpenNest(this ACadSharp.Entities.Circle circle)
        {
            var result = new Geometry.Circle(circle.Center.X, circle.Center.Y, circle.Radius)
            {
                Layer = circle.Layer.ToOpenNest(),
            };
            result.ApplyDxfProperties(circle);
            return result;
        }

        public static Geometry.Line ToOpenNest(this ACadSharp.Entities.Line line)
        {
            var result = new Geometry.Line(
                line.StartPoint.X,
                line.StartPoint.Y,
                line.EndPoint.X,
                line.EndPoint.Y
            )
            {
                Layer = line.Layer.ToOpenNest(),
            };
            result.ApplyDxfProperties(line);
            return result;
        }

        public static List<Geometry.Entity> ToOpenNest(this Spline spline)
        {
            var layer = spline.Layer.ToOpenNest();
            var color = spline.ResolveColor();
            var lineTypeName = spline.ResolveLineTypeName();

            // Evaluate actual points on the spline curve (not control points)
            List<XYZ> curvePoints;
            try
            {
                curvePoints = spline.PolygonalVertexes(200);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Spline curve evaluation failed: {ex.Message}");
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
            var isClosed =
                (polyline.Flags & PolylineFlags.ClosedPolylineOrClosedPolygonMeshInM) != 0;

            return ToLines(
                polyline,
                polyline.Vertices.Count,
                i => polyline.Vertices[i].Location.ToOpenNest(),
                isClosed
            );
        }

        public static List<Geometry.Line> ToOpenNest(this LwPolyline polyline)
        {
            var isClosed = (polyline.Flags & LwPolylineFlags.Closed) != 0;

            return ToLines(
                polyline,
                polyline.Vertices.Count,
                i => polyline.Vertices[i].ToOpenNest(),
                isClosed
            );
        }

        // Both polyline kinds become the same chain of lines; only how a vertex is read and
        // which flag marks the chain closed differ, so the callers supply those.
        private static List<Geometry.Line> ToLines(
            ACadSharp.Entities.Entity polyline,
            int vertexCount,
            Func<int, Vector> vertexAt,
            bool isClosed
        )
        {
            var lines = new List<Geometry.Line>();

            if (vertexCount == 0)
                return lines;

            var layer = polyline.Layer.ToOpenNest();
            var color = polyline.ResolveColor();
            var lineTypeName = polyline.ResolveLineTypeName();
            var lastPoint = vertexAt(0);

            for (var i = 1; i < vertexCount; i++)
            {
                var nextPoint = vertexAt(i);

                lines.Add(
                    new Geometry.Line(lastPoint, nextPoint)
                    {
                        Layer = layer,
                        Color = color,
                        LineTypeName = lineTypeName,
                    }
                );

                lastPoint = nextPoint;
            }

            if (isClosed)
                lines.Add(
                    new Geometry.Line(lastPoint, vertexAt(0))
                    {
                        Layer = layer,
                        Color = color,
                        LineTypeName = lineTypeName,
                    }
                );

            return lines;
        }

        public static List<Geometry.Entity> ToOpenNest(
            this ACadSharp.Entities.Ellipse ellipse,
            double tolerance = 0.001
        )
        {
            var center = new Vector(ellipse.Center.X, ellipse.Center.Y);
            var majorAxis = new Vector(ellipse.MajorAxisEndPoint.X, ellipse.MajorAxisEndPoint.Y);

            var startParam = ellipse.StartParameter;
            var endParam = ellipse.EndParameter;

            if (ellipse.Normal.Z < 0)
            {
                var newStart = OpenNest.Math.Angle.TwoPI - endParam;
                var newEnd = OpenNest.Math.Angle.TwoPI - startParam;
                startParam = newStart;
                endParam = newEnd;
            }

            var semiMajor = System.Math.Sqrt(majorAxis.X * majorAxis.X + majorAxis.Y * majorAxis.Y);
            var semiMinor = semiMajor * ellipse.RadiusRatio;
            var rotation = PortableMath.Atan2(majorAxis.Y, majorAxis.X);

            var layer = ellipse.Layer.ToOpenNest();
            var color = ellipse.ResolveColor();
            var lineTypeName = ellipse.ResolveLineTypeName();

            var entities = EllipseConverter.Convert(
                center,
                semiMajor,
                semiMinor,
                rotation,
                startParam,
                endParam,
                tolerance
            );

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
                LineTypeName = layer.LineType?.Name,
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

            if (
                lt == null
                || string.Equals(lt.Name, "ByLayer", System.StringComparison.OrdinalIgnoreCase)
            )
                return entity.Layer.LineType?.Name ?? "Continuous";

            return lt.Name;
        }

        public static void ApplyDxfProperties(
            this Geometry.Entity target,
            ACadSharp.Entities.Entity source
        )
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
