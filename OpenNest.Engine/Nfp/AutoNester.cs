using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using OpenNest.Converters;
using OpenNest.Geometry;
using OpenNest.Math;

namespace OpenNest
{
    /// <summary>
    /// Mixed-part geometry-aware nesting using NFP-based collision avoidance
    /// and simulated annealing optimization.
    /// </summary>
    public static class AutoNester
    {
        public static List<Part> Nest(List<NestItem> items, Plate plate,
            CancellationToken cancellation = default)
        {
            var workArea = plate.WorkArea();
            var halfSpacing = plate.PartSpacing / 2.0;
            var nfpCache = new NfpCache();
            var candidateRotations = new Dictionary<int, List<double>>();

            // Extract perimeter polygons for each unique drawing.
            foreach (var item in items)
            {
                var drawing = item.Drawing;

                if (candidateRotations.ContainsKey(drawing.Id))
                    continue;

                var perimeterPolygon = ExtractPerimeterPolygon(drawing, halfSpacing);

                if (perimeterPolygon == null)
                {
                    Debug.WriteLine($"[AutoNest] Skipping drawing '{drawing.Name}': no valid perimeter");
                    continue;
                }

                // Compute candidate rotations for this drawing.
                var rotations = ComputeCandidateRotations(item, perimeterPolygon, workArea);
                candidateRotations[drawing.Id] = rotations;

                // Register polygons at each candidate rotation.
                foreach (var rotation in rotations)
                {
                    var rotatedPolygon = RotatePolygon(perimeterPolygon, rotation);
                    nfpCache.RegisterPolygon(drawing.Id, rotation, rotatedPolygon);
                }
            }

            if (candidateRotations.Count == 0)
                return new List<Part>();

            // Pre-compute all NFPs.
            nfpCache.PreComputeAll();

            Debug.WriteLine($"[AutoNest] NFP cache: {nfpCache.Count} entries for {candidateRotations.Count} drawings");

            // Run simulated annealing optimizer.
            var optimizer = new SimulatedAnnealing();
            var result = optimizer.Optimize(items, workArea, nfpCache, candidateRotations, cancellation);

            if (result.Sequence == null || result.Sequence.Count == 0)
                return new List<Part>();

            // Final BLF placement with the best solution.
            var blf = new BottomLeftFill(workArea, nfpCache);
            var placedParts = blf.Fill(result.Sequence);
            var parts = BottomLeftFill.ToNestParts(placedParts);

            Debug.WriteLine($"[AutoNest] Result: {parts.Count} parts placed, {result.Iterations} SA iterations");

            return parts;
        }

        /// <summary>
        /// Extracts the perimeter polygon from a drawing, inflated by half-spacing.
        /// </summary>
        private static Polygon ExtractPerimeterPolygon(Drawing drawing, double halfSpacing)
        {
            var entities = ConvertProgram.ToGeometry(drawing.Program)
                .Where(e => e.Layer != SpecialLayers.Rapid)
                .ToList();

            if (entities.Count == 0)
                return null;

            var definedShape = new ShapeProfile(entities);
            var perimeter = definedShape.Perimeter;

            if (perimeter == null)
                return null;

            // Inflate by half-spacing if spacing is non-zero.
            Shape inflated;

            if (halfSpacing > 0)
            {
                var offsetEntity = perimeter.OffsetEntity(halfSpacing, OffsetSide.Right);
                inflated = offsetEntity as Shape ?? perimeter;
            }
            else
            {
                inflated = perimeter;
            }

            // Convert to polygon with circumscribed arcs for tight nesting.
            var polygon = inflated.ToPolygonWithTolerance(0.01, circumscribe: true);

            if (polygon.Vertices.Count < 3)
                return null;

            // Normalize: move reference point to origin.
            polygon.UpdateBounds();
            var bb = polygon.BoundingBox;
            polygon.Offset(-bb.Left, -bb.Bottom);

            return polygon;
        }

        /// <summary>
        /// Computes candidate rotation angles for a drawing.
        /// </summary>
        private static List<double> ComputeCandidateRotations(NestItem item,
            Polygon perimeterPolygon, Box workArea)
        {
            var rotations = new List<double> { 0 };

            // Add hull-edge angles from the polygon itself.
            var hullAngles = ComputeHullEdgeAngles(perimeterPolygon);

            foreach (var angle in hullAngles)
            {
                if (!rotations.Any(r => r.IsEqualTo(angle)))
                    rotations.Add(angle);
            }

            // Add 90-degree rotation.
            if (!rotations.Any(r => r.IsEqualTo(Angle.HalfPI)))
                rotations.Add(Angle.HalfPI);

            // For narrow work areas, add sweep angles.
            var partBounds = perimeterPolygon.BoundingBox;
            var partLongest = System.Math.Max(partBounds.Width, partBounds.Length);
            var workShort = System.Math.Min(workArea.Width, workArea.Length);

            if (workShort < partLongest)
            {
                var step = Angle.ToRadians(5);

                for (var a = 0.0; a < System.Math.PI; a += step)
                {
                    if (!rotations.Any(r => r.IsEqualTo(a)))
                        rotations.Add(a);
                }
            }

            return rotations;
        }

        /// <summary>
        /// Computes convex hull edge angles from a polygon for candidate rotations.
        /// </summary>
        private static List<double> ComputeHullEdgeAngles(Polygon polygon)
        {
            var angles = new List<double>();

            if (polygon.Vertices.Count < 3)
                return angles;

            var hull = ConvexHull.Compute(polygon.Vertices);
            var verts = hull.Vertices;
            var n = hull.IsClosed() ? verts.Count - 1 : verts.Count;

            for (var i = 0; i < n; i++)
            {
                var next = (i + 1) % n;
                var dx = verts[next].X - verts[i].X;
                var dy = verts[next].Y - verts[i].Y;

                if (dx * dx + dy * dy < Tolerance.Epsilon)
                    continue;

                var angle = -System.Math.Atan2(dy, dx);

                if (!angles.Any(a => a.IsEqualTo(angle)))
                    angles.Add(angle);
            }

            return angles;
        }

        /// <summary>
        /// Creates a rotated copy of a polygon around the origin.
        /// </summary>
        private static Polygon RotatePolygon(Polygon polygon, double angle)
        {
            if (angle.IsEqualTo(0))
                return polygon;

            var result = new Polygon();
            var cos = System.Math.Cos(angle);
            var sin = System.Math.Sin(angle);

            foreach (var v in polygon.Vertices)
            {
                result.Vertices.Add(new Vector(
                    v.X * cos - v.Y * sin,
                    v.X * sin + v.Y * cos));
            }

            // Re-normalize to origin.
            result.UpdateBounds();
            var bb = result.BoundingBox;
            result.Offset(-bb.Left, -bb.Bottom);

            return result;
        }
    }
}
