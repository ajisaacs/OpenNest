using System;
using System.Collections.Generic;
using System.Drawing;
using OpenNest.Geometry;

namespace OpenNest.Controls
{
    public partial class EntityView
    {
        private const float HolePenWidth = 2.5f;
        private readonly Dictionary<int, Pen> holePenCache = new Dictionary<int, Pen>();
        private IReadOnlyDictionary<Guid, Color> holeColors;

        /// <summary>
        /// While the Hole Table is open, the color of each hole's entities by entity ID: holes are
        /// drawn in their size's color with a heavier line. Null draws every entity normally.
        /// </summary>
        public IReadOnlyDictionary<Guid, Color> HoleColors
        {
            get => holeColors;
            set
            {
                holeColors = value;
                Invalidate();
            }
        }

        private Pen HolePen(Entity entity)
        {
            if (holeColors == null || !holeColors.TryGetValue(entity.Id, out var color))
                return null;

            var argb = color.ToArgb();
            if (!holePenCache.TryGetValue(argb, out var pen))
            {
                pen = new Pen(color, HolePenWidth);
                holePenCache[argb] = pen;
            }
            return pen;
        }

        private void DisposeHolePens()
        {
            foreach (var pen in holePenCache.Values)
                pen.Dispose();
            holePenCache.Clear();
        }
    }
}
