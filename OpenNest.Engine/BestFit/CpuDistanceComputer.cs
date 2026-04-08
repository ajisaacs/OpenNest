using OpenNest.Geometry;
using OpenNest.Math;
using System.Collections.Generic;
using System.Linq;

namespace OpenNest.Engine.BestFit
{
    public class CpuDistanceComputer : IDistanceComputer
    {
        public double[] ComputeDistances(
            List<Line> stationaryLines,
            List<Line> movingTemplateLines,
            SlideOffset[] offsets)
        {
            var count = offsets.Length;
            var results = new double[count];

            var allMovingVerts = ExtractUniqueVertices(movingTemplateLines);
            var allStationaryVerts = ExtractUniqueVertices(stationaryLines);

            var vertexCache = new Dictionary<(double, double), (Vector[] leading, Vector[] facing)>();

            foreach (var offset in offsets)
            {
                var key = (offset.DirX, offset.DirY);
                if (vertexCache.ContainsKey(key))
                    continue;

                var leading = FilterVerticesByProjection(allMovingVerts, offset.DirX, offset.DirY, keepHigh: true);
                var facing = FilterVerticesByProjection(allStationaryVerts, offset.DirX, offset.DirY, keepHigh: false);
                vertexCache[key] = (leading, facing);
            }

            System.Threading.Tasks.Parallel.For(0, count, i =>
            {
                var offset = offsets[i];
                var dirX = offset.DirX;
                var dirY = offset.DirY;
                var oppX = -dirX;
                var oppY = -dirY;

                var (leadingMoving, facingStationary) = vertexCache[(dirX, dirY)];

                var minDist = double.MaxValue;

                for (var v = 0; v < leadingMoving.Length; v++)
                {
                    var vx = leadingMoving[v].X + offset.Dx;
                    var vy = leadingMoving[v].Y + offset.Dy;

                    for (var j = 0; j < stationaryLines.Count; j++)
                    {
                        var e = stationaryLines[j];
                        var d = SpatialQuery.RayEdgeDistance(
                            vx, vy,
                            e.StartPoint.X, e.StartPoint.Y,
                            e.EndPoint.X, e.EndPoint.Y,
                            dirX, dirY);

                        if (d < minDist)
                        {
                            minDist = d;
                            if (d <= 0) { results[i] = 0; return; }
                        }
                    }
                }

                for (var v = 0; v < facingStationary.Length; v++)
                {
                    var svx = facingStationary[v].X;
                    var svy = facingStationary[v].Y;

                    for (var j = 0; j < movingTemplateLines.Count; j++)
                    {
                        var e = movingTemplateLines[j];
                        var d = SpatialQuery.RayEdgeDistance(
                            svx, svy,
                            e.StartPoint.X + offset.Dx, e.StartPoint.Y + offset.Dy,
                            e.EndPoint.X + offset.Dx, e.EndPoint.Y + offset.Dy,
                            oppX, oppY);

                        if (d < minDist)
                        {
                            minDist = d;
                            if (d <= 0) { results[i] = 0; return; }
                        }
                    }
                }

                results[i] = minDist;
            });

            return results;
        }

        public double[] ComputeDistances(
            List<Entity> stationaryEntities,
            List<Entity> movingEntities,
            SlideOffset[] offsets)
        {
            var count = offsets.Length;
            var results = new double[count];

            var allMovingVerts = ExtractVerticesFromEntities(movingEntities);
            var allStationaryVerts = ExtractVerticesFromEntities(stationaryEntities);

            var movingCurves = ExtractCurveParams(movingEntities);
            var stationaryCurves = ExtractCurveParams(stationaryEntities);

            var vertexCache = new Dictionary<(double, double), (Vector[] leading, Vector[] facing)>();

            foreach (var offset in offsets)
            {
                var key = (offset.DirX, offset.DirY);
                if (vertexCache.ContainsKey(key))
                    continue;

                var leading = FilterVerticesByProjection(allMovingVerts, offset.DirX, offset.DirY, keepHigh: true);
                var facing = FilterVerticesByProjection(allStationaryVerts, offset.DirX, offset.DirY, keepHigh: false);
                vertexCache[key] = (leading, facing);
            }

            System.Threading.Tasks.Parallel.For(0, count, i =>
            {
                var offset = offsets[i];
                var dirX = offset.DirX;
                var dirY = offset.DirY;
                var oppX = -dirX;
                var oppY = -dirY;

                var (leadingMoving, facingStationary) = vertexCache[(dirX, dirY)];

                var minDist = double.MaxValue;

                // Case 1: Leading moving vertices → stationary entities
                for (var v = 0; v < leadingMoving.Length; v++)
                {
                    var vx = leadingMoving[v].X + offset.Dx;
                    var vy = leadingMoving[v].Y + offset.Dy;

                    for (var j = 0; j < stationaryEntities.Count; j++)
                    {
                        var d = RayEntityDistance(vx, vy, stationaryEntities[j], 0, 0, dirX, dirY);

                        if (d < minDist)
                        {
                            minDist = d;
                            if (d <= 0) { results[i] = 0; return; }
                        }
                    }
                }

                // Case 2: Facing stationary vertices → moving entities (opposite direction)
                for (var v = 0; v < facingStationary.Length; v++)
                {
                    var svx = facingStationary[v].X;
                    var svy = facingStationary[v].Y;

                    for (var j = 0; j < movingEntities.Count; j++)
                    {
                        var d = RayEntityDistance(svx, svy, movingEntities[j], offset.Dx, offset.Dy, oppX, oppY);

                        if (d < minDist)
                        {
                            minDist = d;
                            if (d <= 0) { results[i] = 0; return; }
                        }
                    }
                }

                // Phase 3: Curve-to-curve direct distance.
                // Vertex sampling misses the true contact between two curved entities
                // when the approach angle doesn't align with a sampled vertex.
                for (var m = 0; m < movingCurves.Length; m++)
                {
                    var mc = movingCurves[m];
                    var mcx = mc.Cx + offset.Dx;
                    var mcy = mc.Cy + offset.Dy;

                    for (var s = 0; s < stationaryCurves.Length; s++)
                    {
                        var sc = stationaryCurves[s];
                        var d = SpatialQuery.RayCircleDistance(
                            mcx, mcy, sc.Cx, sc.Cy, mc.Radius + sc.Radius, dirX, dirY);

                        if (d >= minDist || d == double.MaxValue)
                            continue;

                        if (mc.Entity is Arc || sc.Entity is Arc)
                        {
                            var mx = mcx + d * dirX;
                            var my = mcy + d * dirY;
                            var toCx = sc.Cx - mx;
                            var toCy = sc.Cy - my;

                            if (mc.Entity is Arc mArc)
                            {
                                var angle = Angle.NormalizeRad(System.Math.Atan2(toCy, toCx));
                                if (!Angle.IsBetweenRad(angle, mArc.StartAngle, mArc.EndAngle, mArc.IsReversed))
                                    continue;
                            }

                            if (sc.Entity is Arc sArc)
                            {
                                var angle = Angle.NormalizeRad(System.Math.Atan2(-toCy, -toCx));
                                if (!Angle.IsBetweenRad(angle, sArc.StartAngle, sArc.EndAngle, sArc.IsReversed))
                                    continue;
                            }
                        }

                        minDist = d;
                        if (d <= 0) { results[i] = 0; return; }
                    }
                }

                results[i] = minDist;
            });

            return results;
        }

        private readonly struct CurveParams
        {
            public readonly Entity Entity;
            public readonly double Cx, Cy, Radius;

            public CurveParams(Entity entity, double cx, double cy, double radius)
            {
                Entity = entity;
                Cx = cx;
                Cy = cy;
                Radius = radius;
            }
        }

        private static CurveParams[] ExtractCurveParams(List<Entity> entities)
        {
            var curves = new List<CurveParams>();
            for (var i = 0; i < entities.Count; i++)
            {
                if (entities[i] is Circle circle)
                    curves.Add(new CurveParams(circle, circle.Center.X, circle.Center.Y, circle.Radius));
                else if (entities[i] is Arc arc)
                    curves.Add(new CurveParams(arc, arc.Center.X, arc.Center.Y, arc.Radius));
            }
            return curves.ToArray();
        }

        private static double RayEntityDistance(
            double vx, double vy, Entity entity,
            double entityOffsetX, double entityOffsetY,
            double dirX, double dirY)
        {
            if (entity is Line line)
            {
                return SpatialQuery.RayEdgeDistance(
                    vx, vy,
                    line.StartPoint.X + entityOffsetX, line.StartPoint.Y + entityOffsetY,
                    line.EndPoint.X + entityOffsetX, line.EndPoint.Y + entityOffsetY,
                    dirX, dirY);
            }

            if (entity is Arc arc)
            {
                return SpatialQuery.RayArcDistance(
                    vx, vy,
                    arc.Center.X + entityOffsetX, arc.Center.Y + entityOffsetY,
                    arc.Radius,
                    arc.StartAngle, arc.EndAngle, arc.IsReversed,
                    dirX, dirY);
            }

            if (entity is Circle circle)
            {
                return SpatialQuery.RayCircleDistance(
                    vx, vy,
                    circle.Center.X + entityOffsetX, circle.Center.Y + entityOffsetY,
                    circle.Radius,
                    dirX, dirY);
            }

            return double.MaxValue;
        }

        private static Vector[] ExtractVerticesFromEntities(List<Entity> entities)
        {
            var vertices = new HashSet<Vector>();

            for (var i = 0; i < entities.Count; i++)
            {
                var entity = entities[i];

                if (entity is Line line)
                {
                    vertices.Add(line.StartPoint);
                    vertices.Add(line.EndPoint);
                }
                else if (entity is Arc arc)
                {
                    vertices.Add(arc.StartPoint());
                    vertices.Add(arc.EndPoint());
                    AddArcExtremes(vertices, arc);
                }
                else if (entity is Circle circle)
                {
                    // Four cardinal points
                    vertices.Add(new Vector(circle.Center.X + circle.Radius, circle.Center.Y));
                    vertices.Add(new Vector(circle.Center.X - circle.Radius, circle.Center.Y));
                    vertices.Add(new Vector(circle.Center.X, circle.Center.Y + circle.Radius));
                    vertices.Add(new Vector(circle.Center.X, circle.Center.Y - circle.Radius));
                }
            }

            return vertices.ToArray();
        }

        private static void AddArcExtremes(HashSet<Vector> points, Arc arc)
        {
            var a1 = arc.StartAngle;
            var a2 = arc.EndAngle;
            var reversed = arc.IsReversed;

            if (reversed)
                Generic.Swap(ref a1, ref a2);

            // Right (0°)
            if (Angle.IsBetweenRad(Angle.TwoPI, a1, a2))
                points.Add(new Vector(arc.Center.X + arc.Radius, arc.Center.Y));

            // Top (90°)
            if (Angle.IsBetweenRad(Angle.HalfPI, a1, a2))
                points.Add(new Vector(arc.Center.X, arc.Center.Y + arc.Radius));

            // Left (180°)
            if (Angle.IsBetweenRad(System.Math.PI, a1, a2))
                points.Add(new Vector(arc.Center.X - arc.Radius, arc.Center.Y));

            // Bottom (270°)
            if (Angle.IsBetweenRad(System.Math.PI * 1.5, a1, a2))
                points.Add(new Vector(arc.Center.X, arc.Center.Y - arc.Radius));
        }

        private static Vector[] ExtractUniqueVertices(List<Line> lines)
        {
            var vertices = new HashSet<Vector>();
            for (var i = 0; i < lines.Count; i++)
            {
                vertices.Add(lines[i].StartPoint);
                vertices.Add(lines[i].EndPoint);
            }
            return vertices.ToArray();
        }

        private static Vector[] FilterVerticesByProjection(
            Vector[] vertices, double dirX, double dirY, bool keepHigh)
        {
            if (vertices.Length == 0)
                return vertices;

            var projections = new double[vertices.Length];
            var min = double.MaxValue;
            var max = double.MinValue;

            for (var i = 0; i < vertices.Length; i++)
            {
                projections[i] = vertices[i].X * dirX + vertices[i].Y * dirY;
                if (projections[i] < min) min = projections[i];
                if (projections[i] > max) max = projections[i];
            }

            var midpoint = (min + max) / 2;
            var count = 0;

            for (var i = 0; i < vertices.Length; i++)
            {
                if (keepHigh ? projections[i] >= midpoint : projections[i] <= midpoint)
                    count++;
            }

            var result = new Vector[count];
            var idx = 0;

            for (var i = 0; i < vertices.Length; i++)
            {
                if (keepHigh ? projections[i] >= midpoint : projections[i] <= midpoint)
                    result[idx++] = vertices[i];
            }

            return result;
        }
    }
}
