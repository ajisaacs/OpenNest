using System.Collections.Generic;
using System.Linq;

namespace OpenNest.Engine.Sequencing
{
    public class EdgeStartSequencer : IPartSequencer
    {
        public List<SequencedPart> Sequence(IReadOnlyList<Part> parts, Plate plate)
        {
            // Plate(width, length) stores Size with Width/Length swapped internally.
            // Reconstruct the logical plate box using the BoundingBox origin and the
            // corrected extents: Size.Length = X-extent, Size.Width = Y-extent.
            var origin = plate.BoundingBox(false);
            var plateBox = new OpenNest.Geometry.Box(
                origin.X, origin.Y,
                plate.Size.Length,
                plate.Size.Width);

            return parts
                .OrderBy(p => MinEdgeDistance(p.BoundingBox.Center, plateBox))
                .ThenBy(p => p.Location.X)
                .Select(p => new SequencedPart { Part = p })
                .ToList();
        }

        private static double MinEdgeDistance(OpenNest.Geometry.Vector center, OpenNest.Geometry.Box plateBox)
        {
            var distLeft   = center.X - plateBox.Left;
            var distRight  = plateBox.Right - center.X;
            var distBottom = center.Y - plateBox.Bottom;
            var distTop    = plateBox.Top - center.Y;

            return System.Math.Min(System.Math.Min(distLeft, distRight), System.Math.Min(distBottom, distTop));
        }
    }
}
