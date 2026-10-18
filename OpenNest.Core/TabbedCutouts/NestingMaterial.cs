using System.Collections.Generic;
using System.Linq;
using OpenNest.Geometry;

namespace OpenNest
{
    public static class NestingMaterial
    {
        /// <summary>
        /// The material entities nesting and layout validation place parts against: those
        /// <see cref="SpecialLayers.IsMaterial"/> keeps, except that a tabbed cutout's slug stays
        /// attached while the sheet is cut, so a closed hole with <see cref="SpecialLayers.Tab"/>
        /// spans is dropped and its slug counts as part. Area, overlap diagnostics and cutting
        /// keep the hole. Entities without tab spans pass through unchanged.
        /// </summary>
        public static List<Entity> ForNesting(this IEnumerable<Entity> entities)
        {
            var material = entities.Where(e => SpecialLayers.IsMaterial(e.Layer)).ToList();
            if (!material.Exists(e => e.Layer == SpecialLayers.Tab))
                return material;

            var kept = new List<Entity>();
            foreach (var shape in ShapeBuilder.GetShapes(material))
                if (!shape.Entities.Exists(e => e.Layer == SpecialLayers.Tab))
                    kept.AddRange(shape.Entities);
            return kept;
        }
    }
}
