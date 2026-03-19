using OpenNest.Geometry;
using System.Linq;

namespace OpenNest.Engine.ML
{
    public class PartFeatures
    {
        // --- Geometric Features ---
        public double Area { get; set; }
        public double Convexity { get; set; }        // Area / Convex Hull Area
        public double AspectRatio { get; set; }      // Width / Length
        public double BoundingBoxFill { get; set; }  // Area / (Width * Length)
        public double Circularity { get; set; }      // 4 * PI * Area / Perimeter^2
        public double PerimeterToAreaRatio { get; set; } // Perimeter / Area — spacing sensitivity
        public int VertexCount { get; set; }

        // --- Normalized Bitmask (32x32 = 1024 features) ---
        public byte[] Bitmask { get; set; }

        public override string ToString()
        {
            return $"{Area:F2},{Convexity:F4},{AspectRatio:F4},{BoundingBoxFill:F4},{Circularity:F4},{PerimeterToAreaRatio:F4},{VertexCount}";
        }
    }

    public static class FeatureExtractor
    {
        public static PartFeatures Extract(Drawing drawing)
        {
            var entities = OpenNest.Converters.ConvertProgram.ToGeometry(drawing.Program)
                .Where(e => e.Layer != SpecialLayers.Rapid)
                .ToList();

            var profile = new ShapeProfile(entities);
            var perimeter = profile.Perimeter;

            if (perimeter == null) return null;

            var polygon = perimeter.ToPolygonWithTolerance(0.01);
            polygon.UpdateBounds();
            var bb = polygon.BoundingBox;

            var hull = ConvexHull.Compute(polygon.Vertices);
            var hullArea = hull.Area();

            var features = new PartFeatures
            {
                Area = drawing.Area,
                Convexity = drawing.Area / (hullArea > 0 ? hullArea : 1.0),
                AspectRatio = bb.Width / (bb.Length > 0 ? bb.Length : 1.0),
                BoundingBoxFill = drawing.Area / (bb.Area() > 0 ? bb.Area() : 1.0),
                VertexCount = polygon.Vertices.Count,
                Bitmask = GenerateBitmask(polygon, 32)
            };

            // Circularity = 4 * PI * Area / Perimeter^2
            var perimeterLen = polygon.Perimeter();
            features.Circularity = (4 * System.Math.PI * drawing.Area) / (perimeterLen * perimeterLen);
            features.PerimeterToAreaRatio = drawing.Area > 0 ? perimeterLen / drawing.Area : 0;

            return features;
        }

        private static byte[] GenerateBitmask(Polygon polygon, int size)
        {
            var mask = new byte[size * size];
            polygon.UpdateBounds();
            var bb = polygon.BoundingBox;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    // Map grid coordinate (0..size) to bounding box coordinate
                    var px = bb.Left + (x + 0.5) * (bb.Width / size);
                    var py = bb.Bottom + (y + 0.5) * (bb.Length / size);

                    if (polygon.ContainsPoint(new Vector(px, py)))
                    {
                        mask[y * size + x] = 1;
                    }
                }
            }

            return mask;
        }
    }
}
