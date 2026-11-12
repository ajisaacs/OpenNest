using System.Drawing;

namespace OpenNest
{
    /// <summary>
    /// The darker shade WinForms' <c>ControlPaint.Dark</c> gives a colour, computed without
    /// WinForms so both desktop front ends outline parts alike. A port of the HLS arithmetic in
    /// dotnet/winforms (ControlPaint.HLSColor, MIT licence), including its SystemColors.Control
    /// special case.
    /// </summary>
    public static class ColorShade
    {
        private const int ShadowAdjustment = -333;
        private const int HLSMax = 240;
        private const int RGBMax = 255;
        private const int Undefined = HLSMax * 2 / 3;

        /// <summary>As <c>ControlPaint.Dark(color)</c>: halfway to the darkest shade.</summary>
        public static Color Dark(Color color) => Dark(color, 0.5f);

        /// <summary>As <c>ControlPaint.Dark(color, percOfDarkDark)</c>.</summary>
        public static Color Dark(Color color, float percOfDarkDark)
        {
            // With the usual colour scheme, ControlDark/DarkDark is not exactly what the HLS
            // arithmetic would give, so WinForms uses the system colours for Control.
            if (color.ToKnownColor() == KnownColor.Control)
            {
                if (percOfDarkDark == 0.0f)
                    return SystemColors.ControlDark;
                if (percOfDarkDark == 1.0f)
                    return SystemColors.ControlDarkDark;
                var dark = SystemColors.ControlDark;
                var darkDark = SystemColors.ControlDarkDark;
                return Color.FromArgb(
                    (byte)(dark.R - (byte)((dark.R - darkDark.R) * percOfDarkDark)),
                    (byte)(dark.G - (byte)((dark.G - darkDark.G) * percOfDarkDark)),
                    (byte)(dark.B - (byte)((dark.B - darkDark.B) * percOfDarkDark)));
            }

            var (hue, luminosity, saturation) = ToHls(color);
            var zeroLum = luminosity * (ShadowAdjustment + 1000) / 1000;
            return FromHls(hue, zeroLum - (int)(zeroLum * percOfDarkDark), saturation);
        }

        private static (int Hue, int Luminosity, int Saturation) ToHls(Color color)
        {
            int r = color.R;
            int g = color.G;
            int b = color.B;

            var max = System.Math.Max(System.Math.Max(r, g), b);
            var min = System.Math.Min(System.Math.Min(r, g), b);
            var sum = max + min;
            var luminosity = ((sum * HLSMax) + RGBMax) / (2 * RGBMax);

            var dif = max - min;
            if (dif == 0)
                return (Undefined, luminosity, 0);

            var saturation = luminosity <= (HLSMax / 2)
                ? ((dif * HLSMax) + (sum / 2)) / sum
                : ((dif * HLSMax) + (2 * RGBMax - sum) / 2) / (2 * RGBMax - sum);

            var rDelta = (((max - r) * (HLSMax / 6)) + (dif / 2)) / dif;
            var gDelta = (((max - g) * (HLSMax / 6)) + (dif / 2)) / dif;
            var bDelta = (((max - b) * (HLSMax / 6)) + (dif / 2)) / dif;

            int hue;
            if (r == max)
                hue = bDelta - gDelta;
            else if (g == max)
                hue = (HLSMax / 3) + rDelta - bDelta;
            else
                hue = (2 * HLSMax / 3) + gDelta - rDelta;

            if (hue < 0)
                hue += HLSMax;
            if (hue > HLSMax)
                hue -= HLSMax;

            return (hue, luminosity, saturation);
        }

        private static Color FromHls(int hue, int luminosity, int saturation)
        {
            byte r, g, b;
            if (saturation == 0)
            {
                r = g = b = (byte)(luminosity * RGBMax / HLSMax);
            }
            else
            {
                var magic2 = luminosity <= (HLSMax / 2)
                    ? ((luminosity * (HLSMax + saturation)) + (HLSMax / 2)) / HLSMax
                    : luminosity + saturation - (((luminosity * saturation) + (HLSMax / 2)) / HLSMax);
                var magic1 = 2 * luminosity - magic2;

                r = (byte)((HueToRgb(magic1, magic2, hue + HLSMax / 3) * RGBMax + (HLSMax / 2)) / HLSMax);
                g = (byte)((HueToRgb(magic1, magic2, hue) * RGBMax + (HLSMax / 2)) / HLSMax);
                b = (byte)((HueToRgb(magic1, magic2, hue - HLSMax / 3) * RGBMax + (HLSMax / 2)) / HLSMax);
            }

            return Color.FromArgb(r, g, b);
        }

        private static int HueToRgb(int n1, int n2, int hue)
        {
            if (hue < 0)
                hue += HLSMax;
            if (hue > HLSMax)
                hue -= HLSMax;

            if (hue < (HLSMax / 6))
                return n1 + (((n2 - n1) * hue + (HLSMax / 12)) / (HLSMax / 6));
            if (hue < (HLSMax / 2))
                return n2;
            if (hue < (HLSMax * 2 / 3))
                return n1 + (((n2 - n1) * ((HLSMax * 2 / 3) - hue) + (HLSMax / 12)) / (HLSMax / 6));
            return n1;
        }
    }
}
