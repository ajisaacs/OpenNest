using System;

namespace OpenNest.Math
{
    public static class Tolerance
    {
        public const double Epsilon = 0.00001;

        public static bool IsEqualTo(this double a, double b, double tolerance = Epsilon)
        {
            return System.Math.Abs(b - a) <= tolerance;
        }
    }
}
