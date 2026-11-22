namespace OpenNest.IO
{
    /// <summary>
    /// How <see cref="Svg"/> writes a plate or entities. The default keeps every layer with the
    /// colors OpenNest reads back; <see cref="LaserSoftware"/> writes a file to cut from directly.
    /// See docs/svg-import-export.md.
    /// </summary>
    public sealed class SvgExportOptions
    {
        /// <summary>Every layer, the plate outline included, in OpenNest's colors.</summary>
        public static SvgExportOptions Default { get; } = new SvgExportOptions();

        /// <summary>
        /// For hobby laser software such as LightBurn, which makes one cut layer per stroke color:
        /// only what is cut or marked, each kind in its own color.
        /// </summary>
        public static SvgExportOptions LaserSoftware { get; } =
            new SvgExportOptions { CutAndMarksOnly = true, LaserLayerColors = true };

        /// <summary>
        /// Leaves out what is never cut: the plate outline and edge spacing, rapids, display-only
        /// geometry and a tabbed cutout's uncut tabs.
        /// </summary>
        public bool CutAndMarksOnly { get; init; }

        /// <summary>
        /// Strokes each kind of cut in its own color from LightBurn's layer palette: cuts (with
        /// their leads and common lines) black, scribe marks blue, break cuts red and any other
        /// layer the next palette color. Off, every cut layer is black.
        /// </summary>
        public bool LaserLayerColors { get; init; }
    }
}
