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
        /// Returns a default options instance.
        /// </summary>
        public static CadImportOptions Default => new CadImportOptions();
    }
}
