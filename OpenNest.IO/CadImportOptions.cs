namespace OpenNest.IO
{
    /// <summary>
    /// Options controlling how <see cref="CadImporter"/> loads a CAD file
    /// and builds a <see cref="Drawing"/>.
    /// </summary>
    public class CadImportOptions
    {
        /// <summary>
        /// Detector name to use for bend detection. Null = auto-detect.
        /// </summary>
        public string BendDetectorName { get; set; }

        /// <summary>
        /// When false, skips bend detection entirely. Default true.
        /// </summary>
        public bool DetectBends { get; set; } = true;

        /// <summary>Null (default) disables repair. Explicit units and a small physical limit are required.</summary>
        public Bending.BendRepairOptions BendRepair { get; set; }

        /// <summary>
        /// Override the drawing name. Null = filename without extension.
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Required quantity on the produced drawing. Default 1.
        /// </summary>
        public int Quantity { get; set; } = 1;

        /// <summary>
        /// Customer name on the produced drawing. Default null.
        /// </summary>
        public string Customer { get; set; }

        /// <summary>
        /// The units of the drawing being built. An SVG's real-world size is converted to
        /// these; DXF and DWG numbers are taken as they are.
        /// </summary>
        public Units DrawingUnits { get; set; } = Units.Inches;

        /// <summary>
        /// How many of an SVG's user units make an inch, used only when the file does not state
        /// its real-world size. Null makes such a file fail rather than guess its scale.
        /// </summary>
        public double? SvgUserUnitsPerInch { get; set; }

        /// <summary>
        /// Returns a default options instance.
        /// </summary>
        public static CadImportOptions Default => new CadImportOptions();
    }
}
