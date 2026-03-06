using System;

namespace OpenNest.Math
{
    public static class Trigonometry
    {
        /// <summary>
        ///
        /// </summary>
        /// <param name="height">Height</param>
        /// <param name="hypotenuse">Hypotenuse</param>
        /// <returns></returns>
        public static double Base(double height, double hypotenuse)
        {
            return System.Math.Sqrt(hypotenuse * hypotenuse - height * height);
        }

        /// <summary>
        ///
        /// </summary>
        /// <param name="bottom">Base</param>
        /// <param name="hypotenuse">Hypotenuse</param>
        /// <returns></returns>
        public static double Height(double bottom, double hypotenuse)
        {
            return System.Math.Sqrt(hypotenuse * hypotenuse - bottom * bottom);
        }

        /// <summary>
        ///
        /// </summary>
        /// <param name="height">Height</param>
        /// <param name="bottom">Base</param>
        /// <returns></returns>
        public static double Hypotenuse(double height, double bottom)
        {
            return System.Math.Sqrt(height * height + bottom * bottom);
        }
    }
}
