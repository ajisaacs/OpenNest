using OpenNest.Geometry;
using System.Collections.Generic;

namespace OpenNest.Engine.Nfp
{
    /// <summary>
    /// NFP-based Bottom-Left Fill (BLF) placement engine.
    /// Places parts one at a time using feasible regions computed from
    /// the Inner-Fit Polygon minus the union of No-Fit Polygons.
    /// </summary>
    public class BottomLeftFill
    {
        private readonly Box workArea;
        private readonly NfpCache nfpCache;

        public BottomLeftFill(Box workArea, NfpCache nfpCache)
        {
            this.workArea = workArea;
            this.nfpCache = nfpCache;
        }

        /// <summary>
        /// Places parts according to the given sequence using NFP-based BLF.
        /// Each entry is (drawingId, rotation) determining what to place and how.
        /// Returns the list of successfully placed parts with their positions.
        /// </summary>
        public List<PlacedPart> Fill(List<(int drawingId, double rotation, Drawing drawing)> sequence)
        {
            var placedParts = new List<PlacedPart>();

            foreach (var (drawingId, rotation, drawing) in sequence)
            {
                var polygon = nfpCache.GetPolygon(drawingId, rotation);

                if (polygon == null || polygon.Vertices.Count < 3)
                    continue;

                // Compute IFP for this part inside the work area.
                var ifp = InnerFitPolygon.Compute(workArea, polygon);

                if (ifp.Vertices.Count < 3)
                    continue;

                // Compute NFPs against all already-placed parts.
                var nfps = new Polygon[placedParts.Count];

                for (var i = 0; i < placedParts.Count; i++)
                {
                    var placed = placedParts[i];
                    var nfp = nfpCache.Get(placed.DrawingId, placed.Rotation, drawingId, rotation);

                    // Translate NFP to the placed part's position.
                    var translated = TranslatePolygon(nfp, placed.Position);
                    nfps[i] = translated;
                }

                // Compute feasible region and find bottom-left point.
                var feasible = InnerFitPolygon.ComputeFeasibleRegion(ifp, nfps);
                var point = InnerFitPolygon.FindBottomLeftPoint(feasible);

                if (double.IsNaN(point.X))
                    continue;

                placedParts.Add(new PlacedPart
                {
                    DrawingId = drawingId,
                    Rotation = rotation,
                    Position = point,
                    Drawing = drawing
                });
            }

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
                var part = new Part(placed.Drawing);

                if (placed.Rotation != 0)
                    part.Rotate(placed.Rotation);

                part.Location = placed.Position;
                parts.Add(part);
            }

            return parts;
        }

        /// <summary>
        /// Creates a translated copy of a polygon.
        /// </summary>
        private static Polygon TranslatePolygon(Polygon polygon, Vector offset)
        {
            var result = new Polygon();

            foreach (var v in polygon.Vertices)
                result.Vertices.Add(new Vector(v.X + offset.X, v.Y + offset.Y));

            return result;
        }
    }

    /// <summary>
    /// Represents a part that has been placed by the BLF algorithm.
    /// </summary>
    public class PlacedPart
    {
        public int DrawingId { get; set; }
        public double Rotation { get; set; }
        public Vector Position { get; set; }
        public Drawing Drawing { get; set; }
    }
}
