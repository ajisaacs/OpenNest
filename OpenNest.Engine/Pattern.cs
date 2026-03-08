using System.Collections.Generic;
using OpenNest.Geometry;

namespace OpenNest
{
    public class Pattern
    {
        public Pattern()
        {
            Parts = new List<Part>();
        }

        public List<Part> Parts { get; }

        public Box BoundingBox { get; private set; }

        public void UpdateBounds()
        {
            BoundingBox = Parts.GetBoundingBox();
        }

        public Pattern Clone(Vector offset)
        {
            var pattern = new Pattern();

            foreach (var part in Parts)
            {
                var clone = (Part)part.Clone();
                clone.Offset(offset);
                pattern.Parts.Add(clone);
            }

            pattern.UpdateBounds();
            return pattern;
        }
    }
}
