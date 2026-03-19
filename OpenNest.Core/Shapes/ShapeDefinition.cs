using OpenNest.Converters;
using OpenNest.Geometry;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace OpenNest.Shapes
{
    public abstract class ShapeDefinition
    {
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        public string Name { get; set; }

        protected ShapeDefinition()
        {
            var typeName = GetType().Name;
            Name = typeName.EndsWith("Shape")
                ? typeName.Substring(0, typeName.Length - 5)
                : typeName;
        }

        public abstract Drawing GetDrawing();

        public static List<T> LoadFromJson<T>(string path) where T : ShapeDefinition
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<List<T>>(json, JsonOptions);
        }

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
