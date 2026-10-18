using System;
using System.Collections.Generic;
using OpenNest.Geometry;

namespace OpenNest.Converters
{
    /// <summary>What a drawing does with a source entity.</summary>
    public enum EntityRole
    {
        /// <summary>Cut through the material.</summary>
        Cut,

        /// <summary>Marked on the surface (a Scribe pass): never material.</summary>
        Etch,

        /// <summary>
        /// Shown with the part for reference only. It is kept out of the program, so it is never
        /// cut, posted, nested against or verified.
        /// </summary>
        Display,
    }

    /// <summary>
    /// Applies per-entity role overrides. An entity without an override keeps the role its layer
    /// implies: mark layers (ETCH, SCRIBE, ENGRAVE) etch, everything else cuts.
    /// </summary>
    public static class EntityRoles
    {
        public static EntityRole Default(Entity entity) =>
            ScribeLayerRepair.IsMarkLayer(entity.Layer) ? EntityRole.Etch : EntityRole.Cut;

        public static EntityRole Of(Entity entity, IReadOnlyDictionary<Guid, EntityRole> overrides) =>
            overrides != null && overrides.TryGetValue(entity.Id, out var role) ? role : Default(entity);

        /// <summary>
        /// The entities a program is built from: display-only entities are left out and entities
        /// whose override differs from their layer's role are replaced by copies, with the same
        /// ID, on a layer the converters read as that role. Inputs are never mutated.
        /// </summary>
        public static List<Entity> ForProgram(IEnumerable<Entity> entities, IReadOnlyDictionary<Guid, EntityRole> overrides)
        {
            var result = new List<Entity>();
            foreach (var entity in entities)
            {
                if (overrides == null || !overrides.TryGetValue(entity.Id, out var role) || role == Default(entity))
                {
                    result.Add(entity);
                    continue;
                }
                if (role == EntityRole.Display)
                    continue;

                var copy = entity.Clone();
                copy.Id = entity.Id;
                copy.Layer = new Layer(role == EntityRole.Etch ? SpecialLayers.Scribe.Name : SpecialLayers.Cut.Name)
                {
                    Color = entity.Layer?.Color ?? SpecialLayers.Cut.Color,
                    IsVisible = true,
                };
                result.Add(copy);
            }
            return result;
        }

        /// <summary>
        /// A drawing's unsuppressed display-only entities as copies in its program frame (source
        /// coordinates shifted by -<c>Source.Offset</c>, before any program rotation).
        /// </summary>
        public static List<Entity> DisplayGeometry(Drawing drawing)
        {
            var result = new List<Entity>();
            if (drawing?.SourceEntities == null || drawing.EntityRoles == null || drawing.EntityRoles.Count == 0)
                return result;

            var offset = drawing.Source?.Offset ?? Vector.Zero;
            foreach (var entity in drawing.SourceEntities)
            {
                if (drawing.SuppressedEntityIds.Contains(entity.Id) || Of(entity, drawing.EntityRoles) != EntityRole.Display)
                    continue;
                var copy = entity.Clone();
                copy.Id = entity.Id;
                copy.Offset(-offset.X, -offset.Y);
                result.Add(copy);
            }
            return result;
        }
    }
}
