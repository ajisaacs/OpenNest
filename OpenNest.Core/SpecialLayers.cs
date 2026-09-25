using System.Drawing;
using OpenNest.Geometry;

namespace OpenNest
{
    public static class SpecialLayers
    {
        public static readonly Layer Default = new Layer("0") { Color = Color.White };

        public static readonly Layer Cut = new Layer("CUT") { Color = Color.White };

        public static readonly Layer Rapid = new Layer("RAPID") { Color = Color.Gray };

        public static readonly Layer Display = new Layer("DISPLAY") { Color = Color.Cyan };

        public static readonly Layer Leadin = new Layer("LEADIN") { Color = Color.Brown };

        public static readonly Layer Leadout = new Layer("LEADOUT") { Color = Color.Brown };

        public static readonly Layer Scribe = new Layer("SCRIBE") { Color = Color.Magenta };

        /// <summary>
        /// True when an entity converted from a part program describes part material. Rapids
        /// and scribe/etch marks are excluded: marks are only on the surface, so they never
        /// bound material and must not affect nesting, collision, area, or validation.
        /// </summary>
        public static bool IsMaterial(Layer layer) => layer != Rapid && layer != Scribe;
    }
}
