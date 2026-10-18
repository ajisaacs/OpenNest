using System;
using System.Collections.Generic;
using System.Linq;
using OpenNest.Converters;
using OpenNest.Geometry;
using static OpenNest.IO.NestFormat;

namespace OpenNest.IO
{
    public static class EntitySerializer
    {
        public static EntitySetDto ToDto(
            List<Entity> entities,
            HashSet<Guid> suppressed,
            IReadOnlyDictionary<Guid, EntityRole> roles = null
        )
        {
            return new EntitySetDto
            {
                Entities = entities.Select(ToEntityDto).ToList(),
                Suppressed = suppressed.Select(id => id.ToString()).ToList(),
                Roles = roles is { Count: > 0 }
                    ? roles.OrderBy(r => r.Key).ToDictionary(r => r.Key.ToString(), r => RoleName(r.Value))
                    : null,
            };
        }

        public static (List<Entity> entities, HashSet<Guid> suppressed) FromDto(EntitySetDto dto)
        {
            var entities = dto.Entities.Select(FromEntityDto).ToList();
            var suppressed = new HashSet<Guid>(dto.Suppressed.Select(Guid.Parse));
            return (entities, suppressed);
        }

        /// <summary>
        /// Role overrides of <paramref name="dto"/>. Unknown role names and malformed IDs are
        /// skipped, so those entities keep their layer's role.
        /// </summary>
        public static Dictionary<Guid, EntityRole> RolesFromDto(EntitySetDto dto)
        {
            var roles = new Dictionary<Guid, EntityRole>();
            if (dto.Roles == null)
                return roles;
            foreach (var (id, name) in dto.Roles)
            {
                if (Guid.TryParse(id, out var guid) && TryParseRole(name, out var role))
                    roles[guid] = role;
            }
            return roles;
        }

        private static string RoleName(EntityRole role) =>
            role switch
            {
                EntityRole.Etch => "etch",
                EntityRole.Display => "display",
                _ => "cut",
            };

        private static bool TryParseRole(string name, out EntityRole role)
        {
            switch (name?.ToLowerInvariant())
            {
                case "cut":
                    role = EntityRole.Cut;
                    return true;
                case "etch":
                    role = EntityRole.Etch;
                    return true;
                case "display":
                    role = EntityRole.Display;
                    return true;
                default:
                    role = EntityRole.Cut;
                    return false;
            }
        }

        private static EntityDto ToEntityDto(Entity entity)
        {
            switch (entity.Type)
            {
                case EntityType.Line:
                    var line = (Line)entity;
                    return new EntityDto
                    {
                        Id = entity.Id.ToString(),
                        Type = "line",
                        Layer = entity.Layer?.Name ?? "",
                        LineType = entity.LineTypeName ?? "",
                        X1 = line.StartPoint.X,
                        Y1 = line.StartPoint.Y,
                        X2 = line.EndPoint.X,
                        Y2 = line.EndPoint.Y,
                    };

                case EntityType.Arc:
                    var arc = (Arc)entity;
                    return new EntityDto
                    {
                        Id = entity.Id.ToString(),
                        Type = "arc",
                        Layer = entity.Layer?.Name ?? "",
                        LineType = entity.LineTypeName ?? "",
                        CX = arc.Center.X,
                        CY = arc.Center.Y,
                        R = arc.Radius,
                        StartAngle = arc.StartAngle,
                        EndAngle = arc.EndAngle,
                        Reversed = arc.IsReversed,
                    };

                case EntityType.Circle:
                    var circle = (Circle)entity;
                    return new EntityDto
                    {
                        Id = entity.Id.ToString(),
                        Type = "circle",
                        Layer = entity.Layer?.Name ?? "",
                        LineType = entity.LineTypeName ?? "",
                        CX = circle.Center.X,
                        CY = circle.Center.Y,
                        R = circle.Radius,
                        Rotation = circle.Rotation == RotationType.CW ? "CW" : "CCW",
                    };

                default:
                    throw new NotSupportedException(
                        $"Entity type {entity.Type} is not supported for serialization."
                    );
            }
        }

        private static Entity FromEntityDto(EntityDto dto)
        {
            Entity entity;

            switch (dto.Type)
            {
                case "line":
                    entity = new Line(new Vector(dto.X1, dto.Y1), new Vector(dto.X2, dto.Y2));
                    break;

                case "arc":
                    entity = new Arc(
                        new Vector(dto.CX, dto.CY),
                        dto.R,
                        dto.StartAngle,
                        dto.EndAngle,
                        dto.Reversed
                    );
                    break;

                case "circle":
                    var circle = new Circle(new Vector(dto.CX, dto.CY), dto.R);
                    circle.Rotation = dto.Rotation == "CW" ? RotationType.CW : RotationType.CCW;
                    entity = circle;
                    break;

                default:
                    throw new NotSupportedException(
                        $"Entity type '{dto.Type}' is not supported for deserialization."
                    );
            }

            entity.Id = Guid.Parse(dto.Id);
            entity.Layer = ResolveLayer(dto.Layer);
            entity.LineTypeName = dto.LineType;

            return entity;
        }

        private static Layer ResolveLayer(string name)
        {
            if (string.IsNullOrEmpty(name) || name == "0")
                return Layer.Default;

            if (string.Equals(name, SpecialLayers.Cut.Name, StringComparison.OrdinalIgnoreCase))
                return SpecialLayers.Cut;
            if (string.Equals(name, SpecialLayers.Rapid.Name, StringComparison.OrdinalIgnoreCase))
                return SpecialLayers.Rapid;
            if (string.Equals(name, SpecialLayers.Display.Name, StringComparison.OrdinalIgnoreCase))
                return SpecialLayers.Display;
            if (string.Equals(name, SpecialLayers.Leadin.Name, StringComparison.OrdinalIgnoreCase))
                return SpecialLayers.Leadin;
            if (string.Equals(name, SpecialLayers.Leadout.Name, StringComparison.OrdinalIgnoreCase))
                return SpecialLayers.Leadout;
            if (string.Equals(name, SpecialLayers.Scribe.Name, StringComparison.OrdinalIgnoreCase))
                return SpecialLayers.Scribe;
            if (string.Equals(name, SpecialLayers.Break.Name, StringComparison.OrdinalIgnoreCase))
                return SpecialLayers.Break;
            if (string.Equals(name, SpecialLayers.Tab.Name, StringComparison.OrdinalIgnoreCase))
                return SpecialLayers.Tab;

            return new Layer(name);
        }
    }
}
