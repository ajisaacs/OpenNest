using OpenNest.Geometry;
using System;
using System.Collections.Generic;
using System.IO;
using Clipper2Lib;

namespace OpenNest.Engine.Nfp
{
    /// <summary>
    /// NFP-based Bottom-Left Fill (BLF) placement engine.
    /// Places parts one at a time using feasible regions computed from
    /// the Inner-Fit Polygon minus the union of No-Fit Polygons.
    /// </summary>
    public class BottomLeftFill
    {
        private static readonly string DebugLogPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "nest-debug.log");

        private readonly Box workArea;
        private readonly NfpCache nfpCache;

        public BottomLeftFill(Box workArea, NfpCache nfpCache)
        {
            this.workArea = workArea;
            this.nfpCache = nfpCache;
        }

        /// <summary>
        /// Places parts according to the given sequence using NFP-based BLF.
        /// Returns the list of successfully placed parts with their positions.
        /// </summary>
        public List<PlacedPart> Fill(List<SequenceEntry> sequence)
        {
            var placedParts = new List<PlacedPart>();

            using var log = new StreamWriter(DebugLogPath, false);
            log.WriteLine($"[BLF] {DateTime.Now:HH:mm:ss.fff}  workArea: X={workArea.X} Y={workArea.Y} W={workArea.Width} H={workArea.Length}  Right={workArea.Right} Top={workArea.Top}");
            log.WriteLine($"[BLF] Sequence count: {sequence.Count}");

            foreach (var entry in sequence)
            {
                var ifp = nfpCache.GetIfp(entry.DrawingId, entry.Rotation, workArea);

                if (ifp.Vertices.Count < 3)
                {
                    log.WriteLine($"[BLF] DrawingId={entry.DrawingId} rot={entry.Rotation:F3}  SKIPPED (IFP has {ifp.Vertices.Count} verts)");
                    continue;
                }

                log.WriteLine($"[BLF] DrawingId={entry.DrawingId} rot={entry.Rotation:F3}  IFP verts={ifp.Vertices.Count} bounds=({ifp.BoundingBox.X:F2},{ifp.BoundingBox.Y:F2},{ifp.BoundingBox.Width:F2},{ifp.BoundingBox.Length:F2})");

                var nfpPaths = ComputeNfpPaths(placedParts, entry.DrawingId, entry.Rotation, ifp.BoundingBox);
                var feasible = InnerFitPolygon.ComputeFeasibleRegion(ifp, nfpPaths);
                var point = InnerFitPolygon.FindBottomLeftPoint(feasible);

                if (double.IsNaN(point.X))
                {
                    log.WriteLine($"[BLF]   -> NO feasible point (NaN)");
                    continue;
                }

                // Clamp to IFP bounds to correct Clipper2 floating-point drift.
                var ifpBb = ifp.BoundingBox;
                point = new Vector(
                    System.Math.Max(ifpBb.X, System.Math.Min(ifpBb.Right, point.X)),
                    System.Math.Max(ifpBb.Y, System.Math.Min(ifpBb.Top, point.Y)));

                log.WriteLine($"[BLF]   -> placed at ({point.X:F4}, {point.Y:F4})  nfpPaths={nfpPaths.Count} feasibleVerts={feasible.Vertices.Count}");

                placedParts.Add(new PlacedPart
                {
                    DrawingId = entry.DrawingId,
                    Rotation = entry.Rotation,
                    Position = point,
                    Drawing = entry.Drawing
                });
            }

            log.WriteLine($"[BLF] Total placed: {placedParts.Count}/{sequence.Count}");
            return placedParts;
        }

        /// <summary>
        /// Converts placed parts to OpenNest Part instances positioned on the plate.
        /// </summary>
        public static List<Part> ToNestParts(List<PlacedPart> placedParts)
        {
            var parts = new List<Part>(placedParts.Count);

            foreach (var placed in placedParts)
            {
                var part = Part.CreateAtOrigin(placed.Drawing, placed.Rotation);
                // CreateAtOrigin sets Location to compensate for the rotated program's
                // bounding box offset.  The BLF position is a displacement for the
                // origin-normalized polygon, so we ADD it to the existing Location
                // rather than replacing it.
                part.Location = part.Location + placed.Position;
                parts.Add(part);
            }

            return parts;
        }

        /// <summary>
        /// Computes NFPs for a candidate part against all already-placed parts,
        /// returned as Clipper paths with translations applied.
        /// Filters NFPs that don't intersect the target IFP.
        /// </summary>
        private PathsD ComputeNfpPaths(List<PlacedPart> placedParts, int drawingId, double rotation, Box ifpBounds)
        {
            var nfpPaths = new PathsD(placedParts.Count);

            for (var i = 0; i < placedParts.Count; i++)
            {
                var placed = placedParts[i];
                var nfp = nfpCache.Get(placed.DrawingId, placed.Rotation, drawingId, rotation);

                if (nfp != null && nfp.Vertices.Count >= 3)
                {
                    // Spatial pruning: only include NFPs that could actually subtract from the IFP.
                    var nfpBounds = nfp.BoundingBox.Translate(placed.Position);
                    if (nfpBounds.Intersects(ifpBounds))
                    {
                        nfpPaths.Add(NoFitPolygon.ToClipperPath(nfp, placed.Position));
                    }
                }
            }

            return nfpPaths;
        }
    }
}
