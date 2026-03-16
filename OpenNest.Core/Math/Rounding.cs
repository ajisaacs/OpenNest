namespace OpenNest.Math
{
    public static class Rounding
    {
        /// <summary>
        /// Rounds a number down to the nearest factor.
        /// </summary>
        /// <param name="num"></param>
        /// <param name="factor"></param>
        /// <returns></returns>
        public static double RoundDownToNearest(double num, double factor)
        {
            return factor.IsEqualTo(0) ? num : System.Math.Floor(num / factor) * factor;
        }

        /// <summary>
        /// Rounds a number up to the nearest factor.
        /// </summary>
        /// <param name="num"></param>
        /// <param name="factor"></param>
        /// <returns></returns>
        public static double RoundUpToNearest(double num, double factor)
        {
            return factor.IsEqualTo(0) ? num : System.Math.Ceiling(num / factor) * factor;
        }

        /// <summary>
        /// Rounds a number to the nearest factor using midpoint rounding convention.
        /// </summary>
        /// <param name="num"></param>
        /// <param name="factor"></param>
        /// <returns></returns>
        public static double RoundToNearest(double num, double factor)
        {
            return factor.IsEqualTo(0) ? num : System.Math.Round(num / factor) * factor;
        }
    }
}
