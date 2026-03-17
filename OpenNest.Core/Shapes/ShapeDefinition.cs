using System;
using System.Collections.Generic;
using OpenNest.Converters;
using OpenNest.Geometry;

namespace OpenNest.Shapes
{
    public abstract class ShapeDefinition
    {
        public string Name { get; set; }

        protected ShapeDefinition()
        {
            var typeName = GetType().Name;
            Name = typeName.EndsWith("Shape")
                ? typeName.Substring(0, typeName.Length - 5)
                : typeName;
        }

        public abstract Drawing GetDrawing();

        protected Drawing CreateDrawing(List<Entity> entities)
        {
            var pgm = ConvertGeometry.ToProgram(entities);

            if (pgm == null)
                throw new InvalidOperationException(
                    $"Failed to create program for shape '{Name}'. Check that parameters produce valid geometry.");

            return new Drawing(Name, pgm);
        }
    }
}
