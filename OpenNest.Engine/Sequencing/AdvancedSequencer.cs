using System.Collections.Generic;
using System.Linq;
using OpenNest.CNC.CuttingStrategy;
using OpenNest.Math;

namespace OpenNest.Engine.Sequencing
{
    public class AdvancedSequencer : IPartSequencer
    {
        private readonly SequenceParameters _parameters;

        public AdvancedSequencer(SequenceParameters parameters)
        {
            _parameters = parameters;
        }

        public List<SequencedPart> Sequence(IReadOnlyList<Part> parts, Plate plate)
        {
            if (parts.Count == 0)
                return new List<SequencedPart>();

            var exit = PlateHelper.GetExitPoint(plate);

            // Group parts into rows by Y proximity
            var rows = GroupIntoRows(parts, _parameters.MinDistanceBetweenRowsColumns);

            // Sort rows bottom-to-top (ascending Y)
            rows.Sort((a, b) => a.RowY.CompareTo(b.RowY));

            // Determine initial direction based on exit point
            var leftToRight = exit.X > plate.Size.Width * 0.5;

            var result = new List<SequencedPart>(parts.Count);
            foreach (var row in rows)
            {
                var sorted = leftToRight
                    ? row.Parts.OrderBy(p => p.BoundingBox.Center.X).ToList()
                    : row.Parts.OrderByDescending(p => p.BoundingBox.Center.X).ToList();

                foreach (var p in sorted)
                    result.Add(new SequencedPart { Part = p });

                if (_parameters.AlternateRowsColumns)
                    leftToRight = !leftToRight;
            }

            return result;
        }

        private static List<PartRow> GroupIntoRows(IReadOnlyList<Part> parts, double minDistance)
        {
            // Sort parts by Y center
            var sorted = parts
                .OrderBy(p => p.BoundingBox.Center.Y)
                .ToList();

            var rows = new List<PartRow>();

            foreach (var part in sorted)
            {
                var y = part.BoundingBox.Center.Y;
                var placed = false;

                foreach (var row in rows)
                {
                    if (System.Math.Abs(y - row.RowY) <= minDistance + Tolerance.Epsilon)
                    {
                        row.Parts.Add(part);
                        placed = true;
                        break;
                    }
                }

                if (!placed)
                {
                    var row = new PartRow(y);
                    row.Parts.Add(part);
                    rows.Add(row);
                }
            }

            return rows;
        }

        private class PartRow
        {
            public double RowY { get; }
            public List<Part> Parts { get; } = new List<Part>();

            public PartRow(double rowY)
            {
                RowY = rowY;
            }
        }
    }
}
