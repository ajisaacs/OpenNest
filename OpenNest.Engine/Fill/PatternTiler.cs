using OpenNest.Geometry;
using System.Collections.Generic;

namespace OpenNest.Engine.Fill
{
    public static class PatternTiler
    {
        public static List<Part> Tile(List<Part> cell, Size plateSize, double partSpacing)
        {
            if (cell == null || cell.Count == 0)
                return new List<Part>();

            var cellBox = cell.GetBoundingBox();
            var halfSpacing = partSpacing / 2;

            var cellW = cellBox.Width + partSpacing;
            var cellL = cellBox.Length + partSpacing;

            if (cellW <= 0 || cellL <= 0)
                return new List<Part>();

            // Width = Y axis, Length = X axis
            var cols = (int)System.Math.Floor(plateSize.Length / cellL);
            var rows = (int)System.Math.Floor(plateSize.Width / cellW);

            if (cols <= 0 || rows <= 0)
                return new List<Part>();

            // Shift cell so parts start at halfSpacing inset, ensuring symmetric
            // spacing between adjacent tiled cells on all sides.
            var cellOrigin = cellBox.Location;
            var baseOffset = new Vector(halfSpacing - cellOrigin.X, halfSpacing - cellOrigin.Y);

            var result = new List<Part>(cols * rows * cell.Count);

            for (var row = 0; row < rows; row++)
            {
                for (var col = 0; col < cols; col++)
                {
                    var tileOffset = baseOffset + new Vector(col * cellL, row * cellW);

                    foreach (var part in cell)
                    {
                        result.Add(part.CloneAtOffset(tileOffset));
                    }
                }
            }

            return result;
        }
    }
}
