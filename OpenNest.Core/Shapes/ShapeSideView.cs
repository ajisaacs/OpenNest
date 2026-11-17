using System.Collections.Generic;
using OpenNest.Geometry;

namespace OpenNest.Shapes
{
    /// <summary>
    /// A sheet metal part's cross-section, which the Shape Library draws beside the flat blank with
    /// its own dimensions. Annotation only: it never affects the drawing.
    /// </summary>
    public sealed class ShapeSideView
    {
        public ShapeSideView(IReadOnlyList<Entity> entities, IReadOnlyList<ShapeDimension> dimensions)
        {
            Entities = entities;
            Dimensions = dimensions;
        }

        /// <summary>The section's closed outlines, in the view's own coordinates.</summary>
        public IReadOnlyList<Entity> Entities { get; }

        /// <summary>The section's dimensions, in the same coordinates as <see cref="Entities"/>.</summary>
        public IReadOnlyList<ShapeDimension> Dimensions { get; }
    }
}
