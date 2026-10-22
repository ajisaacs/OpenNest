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

        public static readonly Layer Break = new Layer("BREAK") { Color = Color.Orange };

        /// <summary>A tabbed cutout's uncut tabs: material boundary that is never cut.</summary>
        public static readonly Layer Tab = new Layer("TAB") { Color = Color.Orange };

        /// <summary>A supplied blank's shown-only outside edge: material boundary that is never cut.</summary>
        public static readonly Layer DisplayPerimeter = new Layer("DISPLAY PERIMETER") { Color = Color.DarkCyan };

        /// <summary>
        /// True when an entity converted from a part program describes part material. Rapids
        /// and scribe/etch marks are excluded: marks are only on the surface, so they never
        /// bound material and must not affect nesting, collision, area, or validation. Break
        /// cuts are excluded too: they run through material that stays attached at tabs. Tabs are
        /// material: they close a tabbed cutout's boundary. A display perimeter is material: it is the
        /// supplied blank's edge.
        /// </summary>
        public static bool IsMaterial(Layer layer) => layer != Rapid && layer != Scribe && layer != Break;
    }
}
