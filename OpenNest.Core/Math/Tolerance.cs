using System;

namespace OpenNest.Math
{
    public static class Tolerance
    {
        public const double Epsilon = 0.00001;

        /// <summary>
        /// Tolerance for chaining entity endpoints when building shapes from DXF imports.
        /// Larger than Epsilon to handle floating-point gaps at entity junctions.
        /// </summary>
        public const double ChainTolerance = 0.0001;

        public static bool IsEqualTo(this double a, double b, double tolerance = Epsilon)
        {
            return System.Math.Abs(b - a) <= tolerance;
        }
    }
}
