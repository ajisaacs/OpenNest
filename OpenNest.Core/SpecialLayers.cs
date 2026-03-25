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

        public static readonly Layer Leadin = new Layer("LEADIN") { Color = Color.Yellow };

        public static readonly Layer Leadout = new Layer("LEADOUT") { Color = Color.Yellow };

        public static readonly Layer Scribe = new Layer("SCRIBE") { Color = Color.Magenta };
    }
}
