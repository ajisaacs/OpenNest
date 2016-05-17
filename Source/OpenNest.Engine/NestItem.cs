namespace OpenNest
{
    public class NestItem
    {
        /// <summary>
        /// The drawing to be nested.
        /// </summary>
        public Drawing Drawing { get; set; }

        /// <summary>
        /// Priority of the part determines nesting order. Highest priority will be nested first.
        /// </summary>
        public int Priority { get; set; }

        /// <summary>
        /// The number of parts to be nested.
        /// </summary>
        public int Quantity { get; set; }

        /// <summary>
        /// The rotation step in radians.
        /// </summary>
        public double StepAngle { get; set; }

        /// <summary>
        /// The rotation start angle in radians.
        /// </summary>
        public double RotationStart { get; set; }

        /// <summary>
        /// The rotation end angle in radians.
        /// </summary>
        public double RotationEnd { get; set; }
    }
}
