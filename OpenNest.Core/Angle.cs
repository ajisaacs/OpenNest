using System;

namespace OpenNest
{
    public static class Angle
    {
        /// <summary>
        /// Number of radians equal to 1 degree.
        /// </summary>
        public const double RadPerDeg = Math.PI / 180.0;

        /// <summary>
        /// Number of degrees equal to 1 radian.
        /// </summary>
        public const double DegPerRad = 180.0 / Math.PI;

        /// <summary>
        /// Half of PI.
        /// </summary>
        public const double HalfPI = Math.PI * 0.5;

        /// <summary>
        /// 2 x PI
        /// </summary>
        public const double TwoPI = Math.PI * 2.0;

        /// <summary>
        /// Converts radians to degrees.
        /// </summary>
        /// <param name="radians"></param>
        /// <returns></returns>
        public static double ToDegrees(double radians)
        {
            return DegPerRad * radians;
        }

        /// <summary>
        /// Converts degrees to radians.
        /// </summary>
        /// <param name="degrees"></param>
        /// <returns></returns>
        public static double ToRadians(double degrees)
        {
            return RadPerDeg * degrees;
        }

        /// <summary>
        /// Normalizes an angle.
        /// The normalized angle will be in the range from 0 to TwoPI,
        /// where TwoPI itself is not included.
        /// </summary>
        /// <param name="angle"></param>
        /// <returns></returns>
        public static double NormalizeRad(double angle)
        {
            double r = angle % Angle.TwoPI;
            return r < 0 ? Angle.TwoPI + r : r;
        }

        /// <summary>
        /// Normalizes an angle.
        /// The normalized angle will be in the range from 0 to 360,
        /// where 360 itself is not included.
        /// </summary>
        /// <param name="angle"></param>
        /// <returns></returns>
        public static double NormalizeDeg(double angle)
        {
            double r = angle % 360.0;
            return r < 0 ? 360.0 + r : r;
        }

        /// <summary>
        /// Whether the given angle is between the two specified angles (a1 & a2).
        /// </summary>
        /// <param name="angle"></param>
        /// <param name="a1"></param>
        /// <param name="a2"></param>
        /// <param name="reversed"></param>
        /// <returns></returns>
        public static bool IsBetweenRad(double angle, double a1, double a2, bool reversed = false)
        {
            if (reversed)
                Generic.Swap(ref a1, ref a2);

            var diff = Angle.NormalizeRad(a2 - a1);

            // full circle
            if (a2.IsEqualTo(a1))
                return true;

            a1 = Angle.NormalizeRad(angle - a1);
            a2 = Angle.NormalizeRad(a2 - angle);

            return diff >= a1 - Tolerance.Epsilon ||
                   diff >= a2 - Tolerance.Epsilon;
        }

        /// <summary>
        /// Whether the given angle is between the two specified angles (a1 & a2).
        /// </summary>
        /// <param name="angle"></param>
        /// <param name="a1"></param>
        /// <param name="a2"></param>
        /// <param name="reversed"></param>
        /// <returns></returns>
        public static bool IsBetweenDeg(double angle, double a1, double a2, bool reversed = false)
        {
            if (reversed)
                Generic.Swap(ref a1, ref a2);

            var diff = Angle.NormalizeRad(a2 - a1);

            // full circle
            if (a2.IsEqualTo(a1))
                return true;

            a1 = Angle.NormalizeRad(angle - a1);
            a2 = Angle.NormalizeRad(a2 - angle);

            return diff >= a1 - Tolerance.Epsilon ||
                   diff >= a2 - Tolerance.Epsilon;
        }
    }
}
