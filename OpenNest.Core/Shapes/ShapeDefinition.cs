using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using OpenNest.Converters;
using OpenNest.Geometry;

namespace OpenNest.Shapes
{
    public abstract class ShapeDefinition
    {
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
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

        public virtual string GenerateName()
        {
            var typeName = GetType().Name;
            return typeName.EndsWith("Shape")
                ? typeName.Substring(0, typeName.Length - 5)
                : typeName;
        }

        public virtual void SetPreviewDefaults() { }

        /// <summary>
        /// Where the Shape Library draws each parameter's dimension, in the coordinates of the
        /// entities <see cref="GetDrawing"/> builds. Parameters without one are shown as notes.
        /// </summary>
        public virtual IReadOnlyList<ShapeDimension> GetDimensions() => Array.Empty<ShapeDimension>();

        /// <summary>
        /// A sheet metal part's cross-section that the Shape Library draws beside the flat blank,
        /// with the dimensions of parameters the blank cannot show; null for a flat shape.
        /// </summary>
        public virtual ShapeSideView GetSideView() => null;

        public static List<T> LoadFromJson<T>(string path)
            where T : ShapeDefinition
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<List<T>>(json, JsonOptions);
        }

        protected static string Dim(double value) => value.ToString("0.###");

        /// <summary>
        /// Builds the shape's drawing with the CAD converter's cut directions: the outside
        /// clockwise and every cutout counter-clockwise (see <see cref="ShapeProfile.NormalizeEntities"/>).
        /// </summary>
        protected Drawing CreateDrawing(List<Entity> entities)
        {
            var pgm = ConvertGeometry.ToProgram(ShapeProfile.NormalizeEntities(entities));

            if (pgm == null)
                throw new InvalidOperationException(
                    $"Failed to create program for shape '{Name}'. Check that parameters produce valid geometry."
                );

            return new Drawing(Name, pgm);
        }
    }
}
