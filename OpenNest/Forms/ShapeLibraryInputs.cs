using System;
using System.Collections.Generic;
using System.Text.Json;

namespace OpenNest.Forms
{
    /// <summary>
    /// The Shape Library's last inputs: the shape last shown, the quantity and each shape's
    /// last configuration and values. Kept as JSON in the <c>ShapeLibraryInputs</c> user setting.
    /// </summary>
    public sealed class ShapeLibraryInputs
    {
        /// <summary>The <see cref="Type.Name"/> of the shape last shown.</summary>
        public string LastShape { get; set; }

        public int Quantity { get; set; } = 1;

        /// <summary>Each shape's last inputs, keyed by the shape's <see cref="Type.Name"/>.</summary>
        public Dictionary<string, ShapeInputs> Shapes { get; set; } = new(StringComparer.Ordinal);

        public sealed class ShapeInputs
        {
            /// <summary>The configuration the values started from, for a shape that has configurations.</summary>
            public string Configuration { get; set; }

            /// <summary>Parameter values by property name.</summary>
            public Dictionary<string, JsonElement> Values { get; set; } = new(StringComparer.Ordinal);
        }

        /// <summary>Reads saved inputs; anything unreadable starts afresh.</summary>
        public static ShapeLibraryInputs Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return new ShapeLibraryInputs();

            try
            {
                var inputs = JsonSerializer.Deserialize<ShapeLibraryInputs>(json) ?? new ShapeLibraryInputs();
                inputs.Shapes ??= new(StringComparer.Ordinal);
                return inputs;
            }
            catch (JsonException)
            {
                return new ShapeLibraryInputs();
            }
        }

        public string ToJson() => JsonSerializer.Serialize(this);

        public static ShapeLibraryInputs Load() =>
            Parse(Properties.Settings.Default.ShapeLibraryInputs);

        public void Save()
        {
            Properties.Settings.Default.ShapeLibraryInputs = ToJson();
            Properties.Settings.Default.Save();
        }
    }
}
