using OpenNest.Converters;
using OpenNest.Geometry;
using OpenNest.Math;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;

namespace OpenNest.Engine.Nfp
{
    /// <summary>
    /// Mixed-part geometry-aware nesting using NFP-based collision avoidance
    /// and simulated annealing optimization.
    /// </summary>
    public static class AutoNester
    {
        public static List<Part> Nest(List<NestItem> items, Plate plate,
            IProgress<NestProgress> progress = null,
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
            var result = optimizer.Optimize(items, workArea, nfpCache, candidateRotations, progress, cancellation);

            if (result.Sequence == null || result.Sequence.Count == 0)
                return new List<Part>();

            // Final BLF placement with the best solution.
            var blf = new BottomLeftFill(workArea, nfpCache);
            var placedParts = blf.Fill(result.Sequence);
            var parts = BottomLeftFill.ToNestParts(placedParts);

            Debug.WriteLine($"[AutoNest] Result: {parts.Count} parts placed, {result.Iterations} SA iterations");

            NestEngineBase.ReportProgress(progress, NestPhase.Nfp, 0, parts, workArea,
                $"NFP: {parts.Count} parts, {result.Iterations} iterations", isOverallBest: true);

            return parts;
        }

        /// <summary>
        /// Re-places already-positioned parts using NFP-based BLF.
        /// Returns the tighter layout if BLF improves density without losing parts.
        /// </summary>
        public static List<Part> Optimize(List<Part> parts, Plate plate)
        {
            return Optimize(parts, plate.WorkArea(), plate.PartSpacing);
        }

        /// <summary>
        /// Re-places already-positioned parts using NFP-based BLF within the given work area.
        /// Returns the tighter layout if BLF improves density without losing parts.
        /// </summary>
        public static List<Part> Optimize(List<Part> parts, Box workArea, double partSpacing)
        {
            if (parts == null || parts.Count < 2)
                return parts;

            var halfSpacing = partSpacing / 2.0;
            var nfpCache = new NfpCache();
            var registeredRotations = new HashSet<(int id, double rotation)>();

            // Extract polygons for each unique drawing+rotation used by the placed parts.
            foreach (var part in parts)
            {
                var drawing = part.BaseDrawing;
                var rotation = part.Rotation;
                var key = (drawing.Id, rotation);

                if (registeredRotations.Contains(key))
                    continue;

                var perimeterPolygon = ExtractPerimeterPolygon(drawing, halfSpacing);

                if (perimeterPolygon == null)
                    continue;

                var rotatedPolygon = RotatePolygon(perimeterPolygon, rotation);
                nfpCache.RegisterPolygon(drawing.Id, rotation, rotatedPolygon);
                registeredRotations.Add(key);
            }

            if (registeredRotations.Count == 0)
                return parts;

            nfpCache.PreComputeAll();

            // Build BLF sequence sorted by area descending (largest first packs best).
            var sequence = parts
                .OrderByDescending(p => p.BaseDrawing.Area)
                .Select(p => new SequenceEntry(p.BaseDrawing.Id, p.Rotation, p.BaseDrawing))
                .ToList();

            var blf = new BottomLeftFill(workArea, nfpCache);
            var placed = blf.Fill(sequence);
            var optimized = BottomLeftFill.ToNestParts(placed);

            // Only use the NFP result if it kept all parts and improved density.
            if (optimized.Count < parts.Count)
            {
                Debug.WriteLine($"[AutoNest.Optimize] Rejected: placed {optimized.Count}/{parts.Count} parts");
                return parts;
            }

            // Reject if any part landed outside the work area.
            if (!AllPartsInBounds(optimized, workArea))
            {
                Debug.WriteLine("[AutoNest.Optimize] Rejected: parts outside work area");
                return parts;
            }

            var originalScore = Fill.FillScore.Compute(parts, workArea);
            var optimizedScore = Fill.FillScore.Compute(optimized, workArea);

            if (optimizedScore > originalScore)
            {
                Debug.WriteLine($"[AutoNest.Optimize] Improved: density {originalScore.Density:P1} -> {optimizedScore.Density:P1}");
                return optimized;
            }

            Debug.WriteLine($"[AutoNest.Optimize] No improvement: {originalScore.Density:P1} >= {optimizedScore.Density:P1}");
            return parts;
        }

        private static bool AllPartsInBounds(List<Part> parts, Box workArea)
        {
            var logPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "nest-debug.log");

            var allInBounds = true;

            // Append to the log that BLF already started
            using var log = new StreamWriter(logPath, true);
            log.WriteLine($"\n[Bounds] workArea: X={workArea.X} Y={workArea.Y} W={workArea.Width} H={workArea.Length}  Right={workArea.Right} Top={workArea.Top}");

            foreach (var part in parts)
            {
                var bb = part.BoundingBox;
                var outLeft = bb.Left < workArea.X - Tolerance.Epsilon;
                var outBottom = bb.Bottom < workArea.Y - Tolerance.Epsilon;
                var outRight = bb.Right > workArea.Right + Tolerance.Epsilon;
                var outTop = bb.Top > workArea.Top + Tolerance.Epsilon;
                var oob = outLeft || outBottom || outRight || outTop;

                if (oob)
                {
                    log.WriteLine($"[Bounds] OOB  DrawingId={part.BaseDrawing.Id} \"{part.BaseDrawing.Name}\"  loc=({part.Location.X:F4},{part.Location.Y:F4}) rot={part.Rotation:F3}  bb=({bb.Left:F4},{bb.Bottom:F4})-({bb.Right:F4},{bb.Top:F4})  violations: {(outLeft ? "LEFT " : "")}{(outBottom ? "BOTTOM " : "")}{(outRight ? "RIGHT " : "")}{(outTop ? "TOP " : "")}");
                    allInBounds = false;
                }
            }

            if (allInBounds)
                log.WriteLine($"[Bounds] All {parts.Count} parts in bounds.");

            return allInBounds;
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
                var offsetEntity = perimeter.OffsetEntity(halfSpacing, OffsetSide.Left);
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
