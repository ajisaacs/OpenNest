using OpenNest.Geometry;
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

            // Pre-filter vertices per unique direction (typically 4 cardinal directions).
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

                // Case 1: Leading moving vertices → stationary edges
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

                // Case 2: Facing stationary vertices → moving edges (opposite direction)
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

        /// <summary>
        /// Filters vertices by their projection onto the push direction.
        /// keepHigh=true returns the leading half (front face, closest to target).
        /// keepHigh=false returns the facing half (side facing the approaching part).
        /// </summary>
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
