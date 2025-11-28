using System;

namespace OpenNest
{
    public static class Tolerance
    {
        public const double Epsilon = 0.00001;

        public static bool IsEqualTo(this double a, double b, double tolerance = Epsilon)
        {
            return Math.Abs(b - a) <= tolerance;
        }
    }
}
