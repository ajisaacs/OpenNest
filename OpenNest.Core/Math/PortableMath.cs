using System;
using System.Runtime.CompilerServices;

namespace OpenNest.Math
{
    /// <summary>
    /// Trigonometric functions that return the same bits on every platform. System.Math calls
    /// the C runtime, and Windows and glibc differ in the last bit at many angles that are not
    /// quarter turns (a 6" cross turned 45° has a corner at 2.82842712474619 on Linux and
    /// 2.8284271247461907 on Windows). Nesting turns those bits into different tie and
    /// tolerance decisions, so the same
    /// job nested differently on the desktop and on Linux. These functions use only additions,
    /// multiplications, divisions and square roots, which IEEE 754 defines exactly, so every
    /// platform computes the same result. Accuracy is within one or two units in the last place.
    /// </summary>
    /// <remarks>
    /// Sin, Cos, Atan and Atan2 follow fdlibm 5.3 (k_sin.c, k_cos.c, e_rem_pio2.c, s_sin.c,
    /// s_cos.c, s_atan.c, e_atan2.c): "Copyright (C) 1993 by Sun Microsystems, Inc. All rights
    /// reserved. Developed at SunPro, a Sun Microsystems, Inc. business. Permission to use,
    /// copy, modify, and distribute this software is freely granted, provided that this notice
    /// is preserved." Arguments beyond ±2^19·π/2 are first reduced by an exact remainder of
    /// 2π (as a double), which is deterministic but not a true reduction; geometry never gets
    /// there. Tan, Asin and Acos are built from those functions and Sqrt.
    /// </remarks>
    public static class PortableMath
    {
        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        public static double Sin(double x)
        {
            var ix = High(x) & 0x7fffffff;
            if (ix <= 0x3fe921fb)
                return KernelSin(x, 0, false);
            if (ix >= 0x7ff00000)
                return double.NaN;

            var n = RemPio2(x, out var y0, out var y1);
            switch (n & 3)
            {
                case 0:
                    return KernelSin(y0, y1, true);
                case 1:
                    return KernelCos(y0, y1);
                case 2:
                    return -KernelSin(y0, y1, true);
                default:
                    return -KernelCos(y0, y1);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveOptimization)]

        public static double Cos(double x)
        {
            var ix = High(x) & 0x7fffffff;
            if (ix <= 0x3fe921fb)
                return KernelCos(x, 0);
            if (ix >= 0x7ff00000)
                return double.NaN;

            var n = RemPio2(x, out var y0, out var y1);
            switch (n & 3)
            {
                case 0:
                    return KernelCos(y0, y1);
                case 1:
                    return -KernelSin(y0, y1, true);
                case 2:
                    return -KernelCos(y0, y1);
                default:
                    return KernelSin(y0, y1, true);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveOptimization)]

        public static double Tan(double x) => Sin(x) / Cos(x);

        [MethodImpl(MethodImplOptions.AggressiveOptimization)]

        public static double Asin(double x)
        {
            if (!(System.Math.Abs(x) <= 1))
                return double.NaN;
            return Atan2(x, System.Math.Sqrt((1 - x) * (1 + x)));
        }

        [MethodImpl(MethodImplOptions.AggressiveOptimization)]

        public static double Acos(double x)
        {
            if (!(System.Math.Abs(x) <= 1))
                return double.NaN;
            return Atan2(System.Math.Sqrt((1 - x) * (1 + x)), x);
        }

        private static readonly double[] AtanHi =
        {
            4.63647609000806093515e-01,
            7.85398163397448278999e-01,
            9.82793723247329054082e-01,
            1.57079632679489655800e+00,
        };

        private static readonly double[] AtanLo =
        {
            2.26987774529616870924e-17,
            3.06161699786838301793e-17,
            1.39033110312309984516e-17,
            6.12323399573676603587e-17,
        };

        private static readonly double[] AT =
        {
            3.33333333333329318027e-01,
            -1.99999999998764832476e-01,
            1.42857142725034663711e-01,
            -1.11111104054623557880e-01,
            9.09088713343650656196e-02,
            -7.69187620504482999495e-02,
            6.66107313738753120669e-02,
            -5.83357013379057348645e-02,
            4.97687799461593236017e-02,
            -3.65315727442169155270e-02,
            1.62858201153657823623e-02,
        };

        [MethodImpl(MethodImplOptions.AggressiveOptimization)]

        public static double Atan(double x)
        {
            var hx = High(x);
            var ix = hx & 0x7fffffff;
            int id;
            if (ix >= 0x44100000)
            {
                // |x| >= 2^66
                if (double.IsNaN(x))
                    return x + x;
                return hx > 0 ? AtanHi[3] + AtanLo[3] : -AtanHi[3] - AtanLo[3];
            }

            if (ix < 0x3fdc0000)
            {
                // |x| < 0.4375
                if (ix < 0x3e200000)
                    return x; // |x| < 2^-29
                id = -1;
            }
            else
            {
                x = System.Math.Abs(x);
                if (ix < 0x3ff30000)
                {
                    // |x| < 1.1875
                    if (ix < 0x3fe60000)
                    {
                        id = 0;
                        x = (2.0 * x - 1.0) / (2.0 + x);
                    }
                    else
                    {
                        id = 1;
                        x = (x - 1.0) / (x + 1.0);
                    }
                }
                else if (ix < 0x40038000)
                {
                    id = 2;
                    x = (x - 1.5) / (1.0 + 1.5 * x);
                }
                else
                {
                    id = 3;
                    x = -1.0 / x;
                }
            }

            var z = x * x;
            var w = z * z;
            var s1 = z * (AT[0] + w * (AT[2] + w * (AT[4] + w * (AT[6]
                + w * (AT[8] + w * AT[10])))));
            var s2 = w * (AT[1] + w * (AT[3] + w * (AT[5] + w * (AT[7] + w * AT[9]))));
            if (id < 0)
                return x - x * (s1 + s2);

            z = AtanHi[id] - ((x * (s1 + s2) - AtanLo[id]) - x);
            return hx < 0 ? -z : z;
        }

        private const double PiO4 = 7.8539816339744827900E-01;
        private const double PiO2 = 1.5707963267948965580E+00;
        private const double Pi = 3.1415926535897931160E+00;
        private const double PiLo = 1.2246467991473531772E-16;

        [MethodImpl(MethodImplOptions.AggressiveOptimization)]

        public static double Atan2(double y, double x)
        {
            if (double.IsNaN(x) || double.IsNaN(y))
                return x + y;

            var hx = High(x);
            var ix = hx & 0x7fffffff;
            var lx = Low(x);
            var hy = High(y);
            var iy = hy & 0x7fffffff;
            var ly = Low(y);

            if (((hx - 0x3ff00000) | lx) == 0)
                return Atan(y); // x == 1

            var m = ((hy >> 31) & 1) | ((hx >> 30) & 2); // 2 * sign(x) + sign(y)

            if ((iy | ly) == 0)
            {
                // y == ±0
                switch (m)
                {
                    case 0:
                    case 1:
                        return y;
                    case 2:
                        return Pi;
                    default:
                        return -Pi;
                }
            }

            if ((ix | lx) == 0)
                return hy < 0 ? -PiO2 : PiO2; // x == ±0

            if (ix == 0x7ff00000)
            {
                // x == ±∞
                if (iy == 0x7ff00000)
                {
                    switch (m)
                    {
                        case 0:
                            return PiO4;
                        case 1:
                            return -PiO4;
                        case 2:
                            return 3.0 * PiO4;
                        default:
                            return -3.0 * PiO4;
                    }
                }

                switch (m)
                {
                    case 0:
                        return 0.0;
                    case 1:
                        return -0.0;
                    case 2:
                        return Pi;
                    default:
                        return -Pi;
                }
            }

            if (iy == 0x7ff00000)
                return hy < 0 ? -PiO2 : PiO2; // y == ±∞

            double z;
            var k = (iy - ix) >> 20;
            if (k > 60)
                z = PiO2 + 0.5 * PiLo; // |y / x| > 2^60
            else if (hx < 0 && k < -60)
                z = 0.0; // |y| / x < -2^60
            else
                z = Atan(System.Math.Abs(y / x));

            switch (m)
            {
                case 0:
                    return z;
                case 1:
                    return -z;
                case 2:
                    return Pi - (z - PiLo);
                default:
                    return (z - PiLo) - Pi;
            }
        }

        private const double S1 = -1.66666666666666324348e-01;
        private const double S2 = 8.33333333332248946124e-03;
        private const double S3 = -1.98412698298579493134e-04;
        private const double S4 = 2.75573137070700676789e-06;
        private const double S5 = -2.50507602534068634195e-08;
        private const double S6 = 1.58969099521155010221e-10;

        /// <summary>sin(x + y) for |x| ≤ π/4, y the tail of x.</summary>
        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        private static double KernelSin(double x, double y, bool hasTail)
        {
            var ix = High(x) & 0x7fffffff;
            if (ix < 0x3e400000)
                return x; // |x| < 2^-27

            var z = x * x;
            var v = z * x;
            var r = S2 + z * (S3 + z * (S4 + z * (S5 + z * S6)));
            if (!hasTail)
                return x + v * (S1 + z * r);
            return x - ((z * (0.5 * y - v * r) - y) - v * S1);
        }

        private const double C1 = 4.16666666666666019037e-02;
        private const double C2 = -1.38888888888741095749e-03;
        private const double C3 = 2.48015872894767294178e-05;
        private const double C4 = -2.75573143513906633035e-07;
        private const double C5 = 2.08757232129817482790e-09;
        private const double C6 = -1.13596475577881948265e-11;

        /// <summary>cos(x + y) for |x| ≤ π/4, y the tail of x.</summary>
        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        private static double KernelCos(double x, double y)
        {
            var ix = High(x) & 0x7fffffff;
            if (ix < 0x3e400000)
                return 1.0; // |x| < 2^-27

            var z = x * x;
            var r = z * (C1 + z * (C2 + z * (C3 + z * (C4 + z * (C5 + z * C6)))));
            if (ix < 0x3fd33333)
                return 1.0 - (0.5 * z - (z * r - x * y)); // |x| < 0.3

            var qx = ix > 0x3fe90000 ? 0.28125 : FromWords(ix - 0x00200000, 0);
            var hz = 0.5 * z - qx;
            var a = 1.0 - qx;
            return a - (hz - (z * r - x * y));
        }

        private const double InvPio2 = 6.36619772367581382433e-01;
        private const double Pio2_1 = 1.57079632673412561417e+00;
        private const double Pio2_1t = 6.07710050650619224932e-11;
        private const double Pio2_2 = 6.07710050630396597660e-11;
        private const double Pio2_2t = 2.02226624879595063154e-21;
        private const double Pio2_3 = 2.02226624871116645580e-21;
        private const double Pio2_3t = 8.47842766036889956997e-32;
        private const double TwoPi = 2 * System.Math.PI;

        /// <summary>
        /// Reduces x to y0 + y1 in [-π/4, π/4] and returns the number of quarter turns removed.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        private static int RemPio2(double x, out double y0, out double y1)
        {
            var hx = High(x);
            var ix = hx & 0x7fffffff;

            if (ix < 0x4002d97c)
            {
                // |x| < 3π/4: one quarter turn
                double z;
                if (hx > 0)
                {
                    z = x - Pio2_1;
                    if (ix != 0x3ff921fb)
                    {
                        y0 = z - Pio2_1t;
                        y1 = (z - y0) - Pio2_1t;
                    }
                    else
                    {
                        z -= Pio2_2;
                        y0 = z - Pio2_2t;
                        y1 = (z - y0) - Pio2_2t;
                    }

                    return 1;
                }

                z = x + Pio2_1;
                if (ix != 0x3ff921fb)
                {
                    y0 = z + Pio2_1t;
                    y1 = (z - y0) + Pio2_1t;
                }
                else
                {
                    z += Pio2_2;
                    y0 = z + Pio2_2t;
                    y1 = (z - y0) + Pio2_2t;
                }

                return -1;
            }

            if (ix > 0x413921fb)
            {
                // Beyond 2^19·π/2: an exact remainder brings it within ±π first.
                return RemPio2(System.Math.IEEERemainder(x, TwoPi), out y0, out y1);
            }

            var t = System.Math.Abs(x);
            var n = (int)(t * InvPio2 + 0.5);
            var fn = (double)n;
            var r = t - fn * Pio2_1;
            var w = fn * Pio2_1t;
            var j = ix >> 20;
            y0 = r - w;
            var i = j - ((High(y0) >> 20) & 0x7ff);
            if (i > 16)
            {
                // 2nd iteration: good to 118 bits
                t = r;
                w = fn * Pio2_2;
                r = t - w;
                w = fn * Pio2_2t - ((t - r) - w);
                y0 = r - w;
                i = j - ((High(y0) >> 20) & 0x7ff);
                if (i > 49)
                {
                    // 3rd iteration: 151 bits
                    t = r;
                    w = fn * Pio2_3;
                    r = t - w;
                    w = fn * Pio2_3t - ((t - r) - w);
                    y0 = r - w;
                }
            }

            y1 = (r - y0) - w;
            if (hx < 0)
            {
                y0 = -y0;
                y1 = -y1;
                return -n;
            }

            return n;
        }

        private static int High(double x) => (int)(BitConverter.DoubleToInt64Bits(x) >> 32);

        private static int Low(double x) => (int)BitConverter.DoubleToInt64Bits(x);

        private static double FromWords(int high, int low) =>
            BitConverter.Int64BitsToDouble(((long)high << 32) | (uint)low);
    }
}
