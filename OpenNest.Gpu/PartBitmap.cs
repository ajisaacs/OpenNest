using System;
using System.Collections.Generic;
using System.Linq;
using OpenNest.Converters;
using OpenNest.Geometry;
using OpenNest.Math;

namespace OpenNest.Gpu
{
    public class PartBitmap
    {
        public int[] Cells { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public double CellSize { get; set; }
        public double OriginX { get; set; }
        public double OriginY { get; set; }

        public const double DefaultCellSize = 0.05;

        public static PartBitmap FromDrawing(Drawing drawing, double cellSize = DefaultCellSize, double spacingDilation = 0)
        {
            var polygons = GetClosedPolygons(drawing);
            return Rasterize(polygons, cellSize, spacingDilation);
        }

        public static PartBitmap FromDrawingRotated(Drawing drawing, double rotation, double cellSize = DefaultCellSize, double spacingDilation = 0)
        {
            var polygons = GetClosedPolygons(drawing);

            if (!rotation.IsEqualTo(0))
            {
                foreach (var poly in polygons)
                    poly.Rotate(rotation);
            }

            return Rasterize(polygons, cellSize, spacingDilation);
        }

        /// <summary>
        /// Rasterizes a Part that was created with Part.CreateAtOrigin.
        /// Extracts polygons from the Part's program and offsets by Location,
        /// guaranteeing the bitmap matches the exact coordinate space used
        /// by the CPU PairEvaluator.
        /// </summary>
        public static PartBitmap FromPart(Part part, double cellSize = DefaultCellSize)
        {
            var entities = ConvertProgram.ToGeometry(part.Program)
                .Where(e => e.Layer != SpecialLayers.Rapid);
            var shapes = Helper.GetShapes(entities);

            var polygons = new List<Polygon>();

            foreach (var shape in shapes)
            {
                if (!shape.IsClosed())
                    continue;

                var polygon = shape.ToPolygonWithTolerance(0.05);
                polygon.Close();
                polygon.Offset(part.Location);
                polygons.Add(polygon);
            }

            return Rasterize(polygons, cellSize, 0);
        }

        private static PartBitmap Rasterize(List<Polygon> polygons, double cellSize, double spacingDilation)
        {
            if (polygons.Count == 0)
                return new PartBitmap { Cells = Array.Empty<int>(), Width = 0, Height = 0, CellSize = cellSize };

            var minX = double.MaxValue;
            var minY = double.MaxValue;
            var maxX = double.MinValue;
            var maxY = double.MinValue;

            foreach (var poly in polygons)
            {
                poly.UpdateBounds();
                var bb = poly.BoundingBox;
                if (bb.Left < minX) minX = bb.Left;
                if (bb.Bottom < minY) minY = bb.Bottom;
                if (bb.Right > maxX) maxX = bb.Right;
                if (bb.Top > maxY) maxY = bb.Top;
            }

            minX -= spacingDilation;
            minY -= spacingDilation;
            maxX += spacingDilation;
            maxY += spacingDilation;

            var width = (int)System.Math.Ceiling((maxX - minX) / cellSize);
            var height = (int)System.Math.Ceiling((maxY - minY) / cellSize);

            if (width <= 0 || height <= 0)
                return new PartBitmap { Cells = Array.Empty<int>(), Width = 0, Height = 0, CellSize = cellSize };

            var cells = new int[width * height];

            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var px = minX + (x + 0.5) * cellSize;
                    var py = minY + (y + 0.5) * cellSize;
                    var pt = new Vector(px, py);

                    foreach (var poly in polygons)
                    {
                        if (poly.ContainsPoint(pt))
                        {
                            cells[y * width + x] = 1;
                            break;
                        }
                    }
                }
            }

            var dilationCells = (int)System.Math.Ceiling(spacingDilation / cellSize);

            if (dilationCells > 0)
                Dilate(cells, width, height, dilationCells);

            return new PartBitmap
            {
                Cells = cells,
                Width = width,
                Height = height,
                CellSize = cellSize,
                OriginX = minX,
                OriginY = minY
            };
        }

        private static List<Polygon> GetClosedPolygons(Drawing drawing)
        {
            var entities = ConvertProgram.ToGeometry(drawing.Program)
                .Where(e => e.Layer != SpecialLayers.Rapid);
            var shapes = Helper.GetShapes(entities);

            var polygons = new List<Polygon>();

            foreach (var shape in shapes)
            {
                if (!shape.IsClosed())
                    continue;

                var polygon = shape.ToPolygonWithTolerance(0.05);
                polygon.Close();
                polygons.Add(polygon);
            }

            return polygons;
        }

        /// <summary>
        /// Blits two locally-rasterized bitmaps into a shared world-space grid.
        /// B is placed at the given world-space offset. Returns two aligned int[]
        /// arrays of identical dimensions with no fractional offset math.
        /// </summary>
        public static (int[] cellsA, int[] cellsB, int width, int height) BlitPair(
            PartBitmap bitmapA, PartBitmap bitmapB, double offsetX, double offsetY)
        {
            var cellSize = bitmapA.CellSize;

            // B's world-space origin when placed at the offset
            var bWorldOriginX = bitmapB.OriginX + offsetX;
            var bWorldOriginY = bitmapB.OriginY + offsetY;

            // Combined world-space bounds
            var combinedMinX = System.Math.Min(bitmapA.OriginX, bWorldOriginX);
            var combinedMinY = System.Math.Min(bitmapA.OriginY, bWorldOriginY);
            var combinedMaxX = System.Math.Max(
                bitmapA.OriginX + bitmapA.Width * cellSize,
                bWorldOriginX + bitmapB.Width * cellSize);
            var combinedMaxY = System.Math.Max(
                bitmapA.OriginY + bitmapA.Height * cellSize,
                bWorldOriginY + bitmapB.Height * cellSize);

            var sharedWidth = (int)System.Math.Ceiling((combinedMaxX - combinedMinX) / cellSize);
            var sharedHeight = (int)System.Math.Ceiling((combinedMaxY - combinedMinY) / cellSize);

            if (sharedWidth <= 0 || sharedHeight <= 0)
                return (Array.Empty<int>(), Array.Empty<int>(), 0, 0);

            var cellsA = new int[sharedWidth * sharedHeight];
            var cellsB = new int[sharedWidth * sharedHeight];

            // Integer cell offsets for A within the shared grid
            var aOffX = (int)System.Math.Round((bitmapA.OriginX - combinedMinX) / cellSize);
            var aOffY = (int)System.Math.Round((bitmapA.OriginY - combinedMinY) / cellSize);

            // Integer cell offsets for B within the shared grid
            var bOffX = (int)System.Math.Round((bWorldOriginX - combinedMinX) / cellSize);
            var bOffY = (int)System.Math.Round((bWorldOriginY - combinedMinY) / cellSize);

            // Blit A into the shared grid
            for (var y = 0; y < bitmapA.Height; y++)
            {
                for (var x = 0; x < bitmapA.Width; x++)
                {
                    if (bitmapA.Cells[y * bitmapA.Width + x] == 1)
                    {
                        var sx = aOffX + x;
                        var sy = aOffY + y;
                        if (sx >= 0 && sx < sharedWidth && sy >= 0 && sy < sharedHeight)
                            cellsA[sy * sharedWidth + sx] = 1;
                    }
                }
            }

            // Blit B into the shared grid
            for (var y = 0; y < bitmapB.Height; y++)
            {
                for (var x = 0; x < bitmapB.Width; x++)
                {
                    if (bitmapB.Cells[y * bitmapB.Width + x] == 1)
                    {
                        var sx = bOffX + x;
                        var sy = bOffY + y;
                        if (sx >= 0 && sx < sharedWidth && sy >= 0 && sy < sharedHeight)
                            cellsB[sy * sharedWidth + sx] = 1;
                    }
                }
            }

            return (cellsA, cellsB, sharedWidth, sharedHeight);
        }

        private static void Dilate(int[] cells, int width, int height, int radius)
        {
            var source = (int[])cells.Clone();

            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    if (source[y * width + x] != 1)
                        continue;

                    for (var dy = -radius; dy <= radius; dy++)
                    {
                        for (var dx = -radius; dx <= radius; dx++)
                        {
                            var nx = x + dx;
                            var ny = y + dy;

                            if (nx >= 0 && nx < width && ny >= 0 && ny < height)
                                cells[ny * width + nx] = 1;
                        }
                    }
                }
            }
        }
    }
}
