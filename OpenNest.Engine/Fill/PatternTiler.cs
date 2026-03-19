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

            var cellWidth = cellBox.Width + partSpacing;
            var cellHeight = cellBox.Length + partSpacing;

            if (cellWidth <= 0 || cellHeight <= 0)
                return new List<Part>();

            // Size.Width = X-axis, Size.Length = Y-axis
            var cols = (int)System.Math.Floor(plateSize.Width / cellWidth);
            var rows = (int)System.Math.Floor(plateSize.Length / cellHeight);

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
                    var tileOffset = baseOffset + new Vector(col * cellWidth, row * cellHeight);

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
