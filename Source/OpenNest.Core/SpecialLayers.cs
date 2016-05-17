using OpenNest.Geometry;

namespace OpenNest
{
    public static class SpecialLayers
    {
        public static readonly Layer Default = new Layer("0");

        public static readonly Layer Cut = new Layer("CUT");

        public static readonly Layer Rapid = new Layer("RAPID");

        public static readonly Layer Display = new Layer("DISPLAY");

        public static readonly Layer Leadin = new Layer("LEADIN");

        public static readonly Layer Leadout = new Layer("LEADOUT");

        public static readonly Layer Scribe = new Layer("SCRIBE");
    }
}
